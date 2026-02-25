// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using Sharc.Arena.Wasm.Models;
using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// The Master Agent that reads the Ledger, cryptographically verifies sensors,
/// and maintains the "True" state of the aircraft via Byzantine consensus.
/// </summary>
public sealed class AutoPilotAgent
{
    private readonly LedgerManager _ledger;
    private readonly AgentRegistry _registry;

    // Per-type reading buffer: SensorType → (AgentId → SensorReading)
    private readonly Dictionary<SensorType, Dictionary<string, SensorReading>> _readingBuffer = new();

    // Dynamic reputation scores
    private readonly Dictionary<string, int> _reputationScores = new();

    // Inference telemetry surfaced in the simulator UI.
    public int BufferedReadingsCount { get; private set; }
    public int ConsensusRounds { get; private set; }
    public int OutlierRejections { get; private set; }
    public int WarningRejections { get; private set; }
    public int AuthorityRejections { get; private set; }
    public int UnknownAgentRejections { get; private set; }
    public int ParseErrors { get; private set; }
    public int FallbackInferences { get; private set; }
    public string LastInferenceSummary { get; private set; } = "No consensus rounds yet.";

    /// <summary>Callback when a sensor's reputation changes.</summary>
    public Action<string, int>? OnReputationChanged { get; set; }

    /// <summary>Minimum readings required before consensus fires.</summary>
    public int QuorumThreshold { get; set; } = 3;

    /// <summary>Fraction of median beyond which a reading is an outlier (10%).</summary>
    public double OutlierThreshold { get; set; } = 0.10;

    // The clean "True" state of the aircraft after consensus
    public double Altitude { get; private set; } = 35000;
    public double Speed { get; private set; } = 250;
    public double Pitch { get; private set; }
    public double Roll { get; private set; }
    public double EngineLeft { get; private set; } = 650;
    public double EngineRight { get; private set; } = 648;
    public double Heading { get; private set; } = 270;
    public double VerticalSpeed { get; private set; }
    public double Throttle { get; private set; } = 0.72;
    public double FlapPosition { get; private set; }
    public double TurnRate { get; private set; }

    public AutoPilotMode Mode { get; set; } = AutoPilotMode.Suggestive;

    public List<string> ActiveWarnings { get; } = new();

    public AutoPilotAgent(LedgerManager ledger, AgentRegistry registry)
    {
        _ledger = ledger;
        _registry = registry;
    }

    /// <summary>
    /// Seed reputation scores from initial worker agent values.
    /// </summary>
    public void InitializeSensors(List<WorkerAgent> workers)
    {
        foreach (var w in workers)
            _reputationScores[w.AgentId] = w.ReputationScore;
    }

    /// <summary>
    /// Get the current reputation score for an agent.
    /// </summary>
    public int GetReputation(string agentId)
    {
        return _reputationScores.TryGetValue(agentId, out var score) ? score : 0;
    }

    /// <summary>
    /// Processes a new payload and returns whether it was accepted.
    /// For measurement types, buffers readings until quorum is reached, then applies consensus.
    /// </summary>
    public bool TryProcessReading(TrustPayload payload, string signerId, out string reason)
    {
        var agentInfo = _registry.GetAgent(signerId);

        if (agentInfo == null)
        {
            UnknownAgentRejections++;
            reason = "Unknown Agent";
            return false;
        }

        if (payload.EconomicValue > agentInfo.AuthorityCeiling)
        {
            AuthorityRejections++;
            reason = "Authority Exceeded";
            return false;
        }

        try
        {
            var reading = JsonSerializer.Deserialize<SensorReading>(
                payload.Content, FlightSimulatorTelemetryContext.Default.SensorReading);
            if (reading == null)
            {
                reason = "Invalid payload";
                return false;
            }

            // Warnings bypass consensus but require minimum reputation
            if (reading.Type == SensorType.Warning)
            {
                var rep = GetReputation(signerId);
                if (rep < 50)
                {
                    WarningRejections++;
                    reason = "Low Reputation (Warning Rejected)";
                    return false;
                }

                if (!ActiveWarnings.Contains(reading.Message))
                    ActiveWarnings.Add(reading.Message);
                reason = "Warning Accepted";
                return true;
            }

            // Buffer the measurement reading
            if (!_readingBuffer.TryGetValue(reading.Type, out var agentReadings))
            {
                agentReadings = new Dictionary<string, SensorReading>();
                _readingBuffer[reading.Type] = agentReadings;
            }
            agentReadings[signerId] = reading;

            // Check if quorum reached
            if (agentReadings.Count >= QuorumThreshold)
            {
                var consensus = ComputeConsensus(agentReadings, reading.Type);

                // Adjust reputations
                foreach (var kvp in agentReadings)
                {
                    if (consensus.Outliers.Contains(kvp.Key))
                        AdjustReputation(kvp.Key, -6);
                    else
                        AdjustReputation(kvp.Key, +1);
                }

                // Apply consensus value to True State
                ApplyToTrueState(reading.Type, consensus.Value);

                ConsensusRounds++;
                OutlierRejections += consensus.Outliers.Count;
                if (consensus.UsedFallback)
                    FallbackInferences++;
                LastInferenceSummary = consensus.Summary;

                // Clear buffer for next round
                agentReadings.Clear();

                if (consensus.Outliers.Contains(signerId))
                {
                    reason = "Outlier (Consensus Rejected)";
                    return false;
                }

                if (consensus.UsedFallback)
                {
                    reason = "Consensus Accepted (stabilized inference)";
                    return true;
                }

                reason = $"Consensus Accepted ({consensus.AcceptedCount}/{consensus.TotalCount} inliers)";
                return true;
            }

            BufferedReadingsCount++;
            reason = "Buffered";
            return true;
        }
        catch (JsonException)
        {
            ParseErrors++;
            reason = "Parse Error";
            return false;
        }
    }

    [Obsolete("Use TryProcessReading(..., out reason) for clearer intent.")]
    public (bool Accepted, string Reason) ProcessReading(TrustPayload payload, string signerId)
    {
        var accepted = TryProcessReading(payload, signerId, out var reason);
        return (accepted, reason);
    }

    /// <summary>
    /// Computes consensus from buffered readings using median outlier detection,
    /// reputation-weighted inlier fusion, and temporal stabilization.
    /// </summary>
    private ConsensusResult ComputeConsensus(Dictionary<string, SensorReading> readings, SensorType sensorType)
    {
        var sorted = readings.OrderBy(r => r.Value.Value).ToArray();
        var outliers = new HashSet<string>();

        // Compute median
        int count = sorted.Length;
        double median;
        if (count % 2 == 0)
            median = (sorted[count / 2 - 1].Value.Value + sorted[count / 2].Value.Value) / 2.0;
        else
            median = sorted[count / 2].Value.Value;

        // Threshold is relative and type-specific so heavy-noise scenarios are stress-tested
        // without collapsing consensus on normal jitter.
        double threshold = Math.Max(Math.Abs(median) * OutlierThreshold, GetNoiseFloor(sensorType));

        double weightedSum = 0;
        double weightTotal = 0;
        int accepted = 0;

        foreach (var kvp in sorted)
        {
            double deviation = Math.Abs(kvp.Value.Value - median);
            int reputation = GetReputation(kvp.Key);
            double toleranceScale = GetToleranceScale(reputation);

            if (deviation > threshold * toleranceScale)
            {
                outliers.Add(kvp.Key);
            }
            else
            {
                double weight = 1.0 + (reputation / 100.0);
                weightedSum += kvp.Value.Value * weight;
                weightTotal += weight;
                accepted++;
            }
        }

        double inferred = accepted > 0
            ? weightedSum / Math.Max(weightTotal, double.Epsilon)
            : median;

        bool usedFallback = accepted == 0;
        double stabilized = StabilizeInference(sensorType, inferred, ref usedFallback);

        var summary = $"{sensorType}: inliers={accepted}/{count}, outliers={outliers.Count}, value={stabilized:F1}";
        return new ConsensusResult(stabilized, outliers, accepted, count, usedFallback, summary);
    }

    /// <summary>
    /// Adjusts reputation for an agent, clamped to 0-100.
    /// </summary>
    public void AdjustReputation(string agentId, int delta)
    {
        if (!_reputationScores.TryGetValue(agentId, out var current))
            current = 50;

        int newScore = Math.Clamp(current + delta, 0, 100);
        _reputationScores[agentId] = newScore;
        OnReputationChanged?.Invoke(agentId, newScore);
    }

    public void ResetInferenceTelemetry()
    {
        BufferedReadingsCount = 0;
        ConsensusRounds = 0;
        OutlierRejections = 0;
        WarningRejections = 0;
        AuthorityRejections = 0;
        UnknownAgentRejections = 0;
        ParseErrors = 0;
        FallbackInferences = 0;
        LastInferenceSummary = "No consensus rounds yet.";
    }

    private void ApplyToTrueState(SensorType type, double value)
    {
        switch (type)
        {
            case SensorType.Altimeter:
                Altitude = value;
                break;
            case SensorType.Airspeed:
                Speed = value;
                break;
            case SensorType.Pitch:
                Pitch = value;
                break;
            case SensorType.Roll:
                Roll = value;
                break;
            case SensorType.EngineTempLeft:
                EngineLeft = value;
                break;
            case SensorType.EngineTempRight:
                EngineRight = value;
                break;
            case SensorType.Heading:
                Heading = value;
                break;
            case SensorType.VerticalSpeed:
                VerticalSpeed = value;
                break;
            case SensorType.Throttle:
                Throttle = value;
                break;
            case SensorType.FlapPosition:
                FlapPosition = value;
                break;
            case SensorType.TurnRate:
                TurnRate = value;
                break;
            case SensorType.GpsAltitude:
                // Cross-check: treat as altimeter consensus input
                Altitude = value;
                break;
            case SensorType.Yaw:
                // Yaw data accepted via consensus but not applied to a specific state property.
                break;
        }
    }

    /// <summary>
    /// Clears the reading buffer. Call at scenario boundaries to prevent stale readings
    /// from a previous scenario leaking into the next one's quorum.
    /// </summary>
    public void ClearReadingBuffer() => _readingBuffer.Clear();

    public void ClearWarnings() => ActiveWarnings.Clear();

    private static double GetNoiseFloor(SensorType type)
    {
        return type switch
        {
            SensorType.Altimeter => 35.0,
            SensorType.GpsAltitude => 45.0,
            SensorType.Airspeed => 5.0,
            SensorType.Heading => 4.0,
            SensorType.VerticalSpeed => 150.0,
            SensorType.Pitch => 1.5,
            SensorType.Roll => 1.5,
            SensorType.Yaw => 2.0,
            SensorType.EngineTempLeft => 10.0,
            SensorType.EngineTempRight => 10.0,
            SensorType.Throttle => 0.03,
            SensorType.FlapPosition => 1.0,
            SensorType.TurnRate => 1.0,
            _ => 10.0
        };
    }

    private static double GetToleranceScale(int reputation)
    {
        if (reputation >= 80) return 1.2;
        if (reputation >= 50) return 1.0;
        if (reputation >= 25) return 0.85;
        return 0.70;
    }

    private double StabilizeInference(SensorType sensorType, double inferredValue, ref bool usedFallback)
    {
        double currentValue = GetCurrentStateValue(sensorType);
        double maxStep = GetMaxStep(sensorType);
        double delta = inferredValue - currentValue;

        if (Math.Abs(delta) <= maxStep)
            return inferredValue;

        usedFallback = true;
        return currentValue + Math.Sign(delta) * maxStep;
    }

    private double GetCurrentStateValue(SensorType type)
    {
        return type switch
        {
            SensorType.Altimeter => Altitude,
            SensorType.GpsAltitude => Altitude,
            SensorType.Airspeed => Speed,
            SensorType.Pitch => Pitch,
            SensorType.Roll => Roll,
            SensorType.EngineTempLeft => EngineLeft,
            SensorType.EngineTempRight => EngineRight,
            SensorType.Heading => Heading,
            SensorType.VerticalSpeed => VerticalSpeed,
            SensorType.Throttle => Throttle,
            SensorType.FlapPosition => FlapPosition,
            SensorType.TurnRate => TurnRate,
            _ => 0
        };
    }

    private static double GetMaxStep(SensorType type)
    {
        return type switch
        {
            SensorType.Altimeter => 1200.0,
            SensorType.GpsAltitude => 1200.0,
            SensorType.Airspeed => 25.0,
            SensorType.Pitch => 4.0,
            SensorType.Roll => 4.0,
            SensorType.Yaw => 5.0,
            SensorType.EngineTempLeft => 120.0,
            SensorType.EngineTempRight => 120.0,
            SensorType.Heading => 12.0,
            SensorType.VerticalSpeed => 1200.0,
            SensorType.Throttle => 0.20,
            SensorType.FlapPosition => 8.0,
            SensorType.TurnRate => 4.0,
            _ => 10.0
        };
    }

    private readonly record struct ConsensusResult(
        double Value,
        HashSet<string> Outliers,
        int AcceptedCount,
        int TotalCount,
        bool UsedFallback,
        string Summary);
}
