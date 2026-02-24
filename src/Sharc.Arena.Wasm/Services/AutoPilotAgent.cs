using System.Text.Json;
using Sharc.Arena.Wasm.Models;
using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// The Master Agent that reads the Ledger, cryptographically verifies sensors,
/// and maintains the "True" state of the aircraft via Byzantine consensus.
/// </summary>
public class AutoPilotAgent
{
    private readonly LedgerManager _ledger;
    private readonly AgentRegistry _registry;

    // Per-type reading buffer: SensorType → (AgentId → SensorReading)
    private readonly Dictionary<SensorType, Dictionary<string, SensorReading>> _readingBuffer = new();

    // Dynamic reputation scores
    private readonly Dictionary<string, int> _reputationScores = new();

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
    /// Processes a new payload. Returns acceptance status and reason.
    /// For measurement types, buffers readings until quorum is reached, then applies consensus.
    /// </summary>
    public (bool Accepted, string Reason) ProcessReading(TrustPayload payload, string signerId)
    {
        var agentInfo = _registry.GetAgent(signerId);

        if (agentInfo == null)
            return (false, "Unknown Agent");

        if (payload.EconomicValue > agentInfo.AuthorityCeiling)
            return (false, "Authority Exceeded");

        try
        {
            var reading = JsonSerializer.Deserialize<SensorReading>(
                payload.Content, FlightSimulatorTelemetryContext.Default.SensorReading);
            if (reading == null) return (false, "Invalid payload");

            // Warnings bypass consensus but require minimum reputation
            if (reading.Type == SensorType.Warning)
            {
                var rep = GetReputation(signerId);
                if (rep < 50)
                    return (false, "Low Reputation (Warning Rejected)");

                if (!ActiveWarnings.Contains(reading.Message))
                    ActiveWarnings.Add(reading.Message);
                return (true, "Warning Accepted");
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
                var (consensusValue, outliers) = ComputeConsensus(agentReadings);

                // Adjust reputations
                foreach (var kvp in agentReadings)
                {
                    if (outliers.Contains(kvp.Key))
                        AdjustReputation(kvp.Key, -5);
                    else
                        AdjustReputation(kvp.Key, +1);
                }

                // Apply consensus value to True State
                ApplyToTrueState(reading.Type, consensusValue);

                // Clear buffer for next round
                agentReadings.Clear();

                if (outliers.Contains(signerId))
                    return (false, "Outlier (Consensus Rejected)");

                return (true, "Consensus Accepted");
            }

            return (true, "Buffered");
        }
        catch
        {
            return (false, "Parse Error");
        }
    }

    /// <summary>
    /// Computes consensus from buffered readings using median-based outlier detection.
    /// </summary>
    private (double ConsensusValue, HashSet<string> Outliers) ComputeConsensus(
        Dictionary<string, SensorReading> readings)
    {
        var sorted = readings.OrderBy(r => r.Value.Value).ToList();
        var outliers = new HashSet<string>();

        // Compute median
        double median;
        int count = sorted.Count;
        if (count % 2 == 0)
            median = (sorted[count / 2 - 1].Value.Value + sorted[count / 2].Value.Value) / 2.0;
        else
            median = sorted[count / 2].Value.Value;

        // Threshold: at least 10 units absolute, or 10% of median magnitude
        double threshold = Math.Max(Math.Abs(median) * OutlierThreshold, 10.0);

        double sum = 0;
        int accepted = 0;

        foreach (var kvp in sorted)
        {
            double deviation = Math.Abs(kvp.Value.Value - median);
            if (deviation > threshold)
            {
                outliers.Add(kvp.Key);
            }
            else
            {
                sum += kvp.Value.Value;
                accepted++;
            }
        }

        double consensusValue = accepted > 0 ? sum / accepted : median;
        return (consensusValue, outliers);
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
        }
    }

    public void ClearWarnings() => ActiveWarnings.Clear();
}
