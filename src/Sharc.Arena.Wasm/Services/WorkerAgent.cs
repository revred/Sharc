using System.Text.Json;
using Sharc.Arena.Wasm.Models;
using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Simulates a sensor node pushing telemetry into the Sharc Trust Layer.
/// </summary>
public class WorkerAgent
{
    public string AgentId { get; }
    public bool IsReliable { get; }
    public ISharcSigner Signer { get; }
    
    // In reality, these would be in the Registry, but we cache for the simulation UI.
    public ulong AuthorityCeiling { get; set; }
    public int ReputationScore { get; set; }

    public WorkerAgent(string agentId, ISharcSigner signer, bool isReliable, int reputation, ulong authority)
    {
        AgentId = agentId;
        Signer = signer;
        IsReliable = isReliable;
        ReputationScore = reputation;
        AuthorityCeiling = authority;
    }

    /// <summary>
    /// serializes a reading and returns the TrustPayload to be appended.
    /// </summary>
    public TrustPayload GenerateTelemetryPayload(SensorReading reading)
    {
        string json = JsonSerializer.Serialize(reading, FlightSimulatorTelemetryContext.Default.SensorReading);
        
        // Use EconomicValue to denote the "Importance" or "Severity" of the reading.
        // Routine telemetry = 1. Warnings = 50. Critical failure = 100.
        ulong importance = reading.Type == SensorType.Warning ? 100ul : 1ul;
        
        return new TrustPayload(PayloadType.Text, json)
        {
            EconomicValue = importance
        };
    }
}
