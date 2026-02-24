// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using Sharc.Arena.Wasm.Models;
using Sharc.Core.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>Holds the definition of a single simulation scenario.</summary>
public sealed class ScenarioContext
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Func<List<WorkerAgent>, AutoPilotAgent, SimulationEngine, Task>? ScenarioLogic { get; set; }
}

/// <summary>
/// Drives the 15-scenario flight simulation loop, emitting sensor readings
/// and orchestrating agent interactions per tick.
/// </summary>
public sealed class SimulationEngine
{
    private readonly AutoPilotAgent _autoPilot;
    private readonly List<WorkerAgent> _workers;
    private readonly Func<TrustPayload, string, Task> _onAgentEmit;

    private List<ScenarioContext> _scenarios = new();
    private int _ticksInCurrentScenario;
    private readonly Random _rng = new(42);

    public int CurrentScenarioIndex { get; private set; }
    public int ScenarioCount => _scenarios.Count;

    public SimulationEngine(AutoPilotAgent autoPilot, List<WorkerAgent> workers, Func<TrustPayload, string, Task> onAgentEmit)
    {
        _autoPilot = autoPilot;
        _workers = workers;
        _onAgentEmit = onAgentEmit;
        BuildScenarios();
    }

    public void MoveNextScenario()
    {
        CurrentScenarioIndex = (CurrentScenarioIndex + 1) % _scenarios.Count;
        _ticksInCurrentScenario = 0;
    }

    public ScenarioContext GetCurrentScenario() => _scenarios[CurrentScenarioIndex];

    public async Task TickAsync()
    {
        var scenario = _scenarios[CurrentScenarioIndex];
        if (scenario.ScenarioLogic != null)
            await scenario.ScenarioLogic.Invoke(_workers, _autoPilot, this);
        _ticksInCurrentScenario++;
    }

    public async Task EmitReadingAsync(WorkerAgent agent, SensorReading reading)
    {
        var payload = agent.GenerateTelemetryPayload(reading);
        await _onAgentEmit.Invoke(payload, agent.AgentId);
    }

    private WorkerAgent Reliable(int index) => _workers.Where(w => w.IsReliable).ElementAt(index);
    private WorkerAgent Unreliable() => _workers.First(w => !w.IsReliable);
    private int Tick => _ticksInCurrentScenario;

    private void BuildScenarios()
    {
        // 1. PRE-FLIGHT CHECK — All sensors agree, baseline trust established
        _scenarios.Add(new ScenarioContext
        {
            Id = 1,
            Name = "PRE-FLIGHT CHECK",
            Description = "All sensors reporting nominal. Baseline trust scores established across all agents.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                foreach (var w in workers)
                {
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, 35000));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Airspeed, 250));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Heading, 270));
                }
            }
        });

        // 2. TAKEOFF ROLL — Speed ramps up, noise within tolerance, all accepted
        _scenarios.Add(new ScenarioContext
        {
            Id = 2,
            Name = "TAKEOFF ROLL",
            Description = "Speed climbing through V1/VR/V2. All sensors within noise tolerance.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                double baseSpeed = 80 + sim.Tick * 15;
                foreach (var w in workers)
                {
                    double noise = sim._rng.NextDouble() * 4 - 2; // ±2 kts noise
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Airspeed, baseSpeed + noise, "kts"));
                }
                foreach (var w in workers.Where(x => x.IsReliable))
                {
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Pitch, 5 + sim.Tick * 0.5));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Throttle, 0.95));
                }
            }
        });

        // 3. CLIMB TO CRUISE — Altitude/speed climb, VerticalSpeed positive
        _scenarios.Add(new ScenarioContext
        {
            Id = 3,
            Name = "CLIMB TO CRUISE",
            Description = "Climbing through FL250 to FL350. Positive vertical speed, all sensors agree.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                double baseAlt = 25000 + sim.Tick * 500;
                foreach (var w in workers)
                {
                    double noise = sim._rng.NextDouble() * 20 - 10;
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, baseAlt + noise, "ft"));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.VerticalSpeed, 1800 + noise));
                }
            }
        });

        // 4. ROUTINE CRUISE — Stable flight, reputation slowly recovering
        _scenarios.Add(new ScenarioContext
        {
            Id = 4,
            Name = "ROUTINE CRUISE",
            Description = "FL350 level flight. All sensors stable. Penalized agents slowly recover reputation.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                foreach (var w in workers)
                {
                    double noise = sim._rng.NextDouble() * 6 - 3;
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, 35000 + noise));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Airspeed, 250 + noise * 0.5));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Heading, 270 + noise * 0.2));
                }
            }
        });

        // 5. PITOT TUBE ICING — REL-1 freezes at 250 kts while others show deceleration
        _scenarios.Add(new ScenarioContext
        {
            Id = 5,
            Name = "PITOT TUBE ICING",
            Description = "REL-1 pitot tube freezes at 250 kts. Other sensors show deceleration to 220 kts. Outlier detection triggers.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                double realSpeed = 250 - sim.Tick * 5; // decelerating
                var reliables = workers.Where(w => w.IsReliable).ToList();
                // REL-1 frozen
                await sim.EmitReadingAsync(reliables[0], SensorReading.Create(SensorType.Airspeed, 250, "kts"));
                // Others report true deceleration
                for (int i = 1; i < reliables.Count; i++)
                    await sim.EmitReadingAsync(reliables[i], SensorReading.Create(SensorType.Airspeed, realSpeed + sim._rng.NextDouble() * 2));
                // UNREL also reports true (confirming consensus)
                var unrel = workers.First(w => !w.IsReliable);
                await sim.EmitReadingAsync(unrel, SensorReading.Create(SensorType.Airspeed, realSpeed + sim._rng.NextDouble() * 3));
            }
        });

        // 6. GYRO DRIFT — REL-2 pitch creeps up slowly
        _scenarios.Add(new ScenarioContext
        {
            Id = 6,
            Name = "GYRO DRIFT",
            Description = "REL-2 gyroscope slowly drifts pitch upward. Detected once deviation exceeds 10% of median.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                double truePitch = 2.0;
                double driftedPitch = 2.0 + sim.Tick * 1.5; // creeping up
                var reliables = workers.Where(w => w.IsReliable).ToList();

                await sim.EmitReadingAsync(reliables[0], SensorReading.Create(SensorType.Pitch, truePitch));
                await sim.EmitReadingAsync(reliables[1], SensorReading.Create(SensorType.Pitch, driftedPitch)); // drifting
                await sim.EmitReadingAsync(reliables[2], SensorReading.Create(SensorType.Pitch, truePitch + 0.5));
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable), SensorReading.Create(SensorType.Pitch, truePitch));
            }
        });

        // 7. ALTIMETER FAILURE — REL-3 drops to 0
        _scenarios.Add(new ScenarioContext
        {
            Id = 7,
            Name = "ALTIMETER FAILURE",
            Description = "REL-3 altimeter hard-fails to 0 ft. Consensus rejects as extreme outlier.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                var reliables = workers.Where(w => w.IsReliable).ToList();
                await sim.EmitReadingAsync(reliables[0], SensorReading.Create(SensorType.Altimeter, 35000));
                await sim.EmitReadingAsync(reliables[1], SensorReading.Create(SensorType.Altimeter, 35010));
                await sim.EmitReadingAsync(reliables[2], SensorReading.Create(SensorType.Altimeter, 0)); // FAILED
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable), SensorReading.Create(SensorType.Altimeter, 35005));
            }
        });

        // 8. ALTITUDE SPOOFING — UNREL-1 claims rapid descent
        _scenarios.Add(new ScenarioContext
        {
            Id = 8,
            Name = "ALTITUDE SPOOFING",
            Description = "UNREL-1 reports 20000 ft (rapid descent). All reliable sensors at FL350. Byzantine rejection.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                foreach (var w in workers.Where(x => x.IsReliable))
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, 35000 + sim._rng.NextDouble() * 10));
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable), SensorReading.Create(SensorType.Altimeter, 20000));
            }
        });

        // 9. FALSE FIRE WARNING — UNREL-1 issues critical warning (authority ceiling exceeded)
        _scenarios.Add(new ScenarioContext
        {
            Id = 9,
            Name = "FALSE FIRE WARNING",
            Description = "UNREL-1 attempts critical fire warning. Payload value=100 exceeds authority ceiling=10. Rejected.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                // Unreliable agent attempts a critical warning (economic value = 100, ceiling = 10)
                var badAgent = workers.First(w => !w.IsReliable);
                await sim.EmitReadingAsync(badAgent, SensorReading.CreateWarning("L-ENG FIRE DETECTED"));
                // Reliable sensors show normal engine temps
                foreach (var w in workers.Where(x => x.IsReliable))
                {
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.EngineTempLeft, 650 + sim._rng.NextDouble() * 5));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.EngineTempRight, 648 + sim._rng.NextDouble() * 5));
                }
            }
        });

        // 10. REPLAY / TAMPER — UNREL-1 sends economicValue=999 (authority rejection)
        _scenarios.Add(new ScenarioContext
        {
            Id = 10,
            Name = "REPLAY / TAMPER",
            Description = "UNREL-1 submits payload with inflated economic value (999). Authority ceiling blocks the attempt.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                var badAgent = workers.First(w => !w.IsReliable);
                // Manually craft a high-value payload to trigger authority rejection
                var reading = SensorReading.Create(SensorType.Altimeter, 10000);
                var json = JsonSerializer.Serialize(reading, FlightSimulatorTelemetryContext.Default.SensorReading);
                var tampered = new TrustPayload(PayloadType.Text, json) { EconomicValue = 999 };
                await sim._onAgentEmit.Invoke(tampered, badAgent.AgentId);
                // Normal traffic from reliable
                foreach (var w in workers.Where(x => x.IsReliable))
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, 35000));
            }
        });

        // 11. COORDINATED ATTACK — UNREL-1 + REL-1 both report 20000ft, REL-2+3 report 35000ft
        _scenarios.Add(new ScenarioContext
        {
            Id = 11,
            Name = "COORDINATED ATTACK",
            Description = "UNREL-1 + REL-1 report 20000 ft. REL-2 + REL-3 report 35000 ft. Consensus trusts the median.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                var reliables = workers.Where(w => w.IsReliable).ToList();
                await sim.EmitReadingAsync(reliables[0], SensorReading.Create(SensorType.Altimeter, 20000)); // compromised reliable
                await sim.EmitReadingAsync(reliables[1], SensorReading.Create(SensorType.Altimeter, 35000));
                await sim.EmitReadingAsync(reliables[2], SensorReading.Create(SensorType.Altimeter, 35010));
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable), SensorReading.Create(SensorType.Altimeter, 20000));
            }
        });

        // 12. AUTO-PILOT CORRECTION — Mode switches to A/P, overrides via consensus
        _scenarios.Add(new ScenarioContext
        {
            Id = 12,
            Name = "AUTO-PILOT CORRECTION",
            Description = "Mode switches to Auto-Pilot. Master agent takes direct control using consensus-derived True Data.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                ap.Mode = AutoPilotMode.AutoPilot;
                // All sensors agree on corrective descent
                double targetAlt = 35000 - sim.Tick * 100;
                foreach (var w in workers)
                {
                    double noise = sim._rng.NextDouble() * 8 - 4;
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, targetAlt + noise));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.VerticalSpeed, -500 + noise));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Pitch, -2 + noise * 0.1));
                }
            }
        });

        // 13. GPS vs BARO CROSS-CHECK — UNREL-1 GPS way off
        _scenarios.Add(new ScenarioContext
        {
            Id = 13,
            Name = "GPS vs BARO CROSS-CHECK",
            Description = "GPS and barometric altitude compared. UNREL-1 GPS reports 45000 ft. Reliable baro consensus at FL350.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                foreach (var w in workers.Where(x => x.IsReliable))
                {
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, 35000 + sim._rng.NextDouble() * 10));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.GpsAltitude, 35005 + sim._rng.NextDouble() * 10));
                }
                // UNREL GPS way off
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable), SensorReading.Create(SensorType.GpsAltitude, 45000));
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable), SensorReading.Create(SensorType.Altimeter, 35000));
            }
        });

        // 14. GENUINE ENGINE FIRE — All reliable sensors see temp rise, warning at 950°C
        _scenarios.Add(new ScenarioContext
        {
            Id = 14,
            Name = "GENUINE ENGINE FIRE",
            Description = "All reliable sensors detect L-ENG temp climbing past 950°C. Legitimate critical warning issued.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                double engTemp = 650 + sim.Tick * 60; // climbing rapidly
                foreach (var w in workers.Where(x => x.IsReliable))
                {
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.EngineTempLeft, engTemp + sim._rng.NextDouble() * 10));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.EngineTempRight, 648 + sim._rng.NextDouble() * 3));
                }
                if (engTemp > 950)
                {
                    var firstReliable = workers.First(w => w.IsReliable);
                    await sim.EmitReadingAsync(firstReliable, SensorReading.CreateWarning("L-ENG FIRE DETECTED — TEMP > 950°C"));
                }
                // UNREL denies the fire
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable),
                    SensorReading.Create(SensorType.EngineTempLeft, 650));
            }
        });

        // 15. RAPID DECOMPRESSION — All reliable agree on descent, unreliable denies
        _scenarios.Add(new ScenarioContext
        {
            Id = 15,
            Name = "RAPID DECOMPRESSION",
            Description = "Cabin altitude warning. All reliable sensors agree on emergency descent. UNREL-1 denies altitude change.",
            ScenarioLogic = async (workers, ap, sim) =>
            {
                ap.Mode = AutoPilotMode.AutoPilot;
                double descAlt = 35000 - sim.Tick * 800; // aggressive descent
                foreach (var w in workers.Where(x => x.IsReliable))
                {
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.Altimeter, descAlt + sim._rng.NextDouble() * 20));
                    await sim.EmitReadingAsync(w, SensorReading.Create(SensorType.VerticalSpeed, -4000 + sim._rng.NextDouble() * 50));
                }
                // UNREL denies descent
                await sim.EmitReadingAsync(workers.First(w => !w.IsReliable),
                    SensorReading.Create(SensorType.Altimeter, 35000));
                if (sim.Tick == 0)
                {
                    var firstReliable = workers.First(w => w.IsReliable);
                    await sim.EmitReadingAsync(firstReliable, SensorReading.CreateWarning("CABIN ALT WARNING — RAPID DECOMPRESSION"));
                }
            }
        });
    }
}
