namespace Sharc.Analysis;

/// <summary>
/// Represents a suspicious finding in the ledger.
/// </summary>
/// <param name="AgentId">The agent involved in the anomaly.</param>
/// <param name="Type">The classification of the anomaly (e.g., "VelocitySpike").</param>
/// <param name="Description">Human-readable description of the issue.</param>
/// <param name="Timestamp">When the anomaly occurred (or was detected).</param>
public record AnomalyReport(string AgentId, string Type, string Description, long Timestamp);

/// <summary>
/// Scans the ledger for suspicious patterns and anomalies.
/// </summary>
public class AnomalyScanner
{
    private readonly SharcDatabase _db;
    private const string LedgerTableName = "_sharc_ledger";

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalyScanner"/> class.
    /// </summary>
    /// <param name="db">The database instance.</param>
    public AnomalyScanner(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Detects agents exceeding a transaction frequency threshold within a time window.
    /// </summary>
    public List<AnomalyReport> DetectVelocitySpikes(int windowSeconds, int countThreshold)
    {
        var anomalies = new List<AnomalyReport>();
        var table = _db.Schema.GetTable(LedgerTableName);
        if (table == null) return anomalies;

        // In-memory analysis for now (scan full ledger).
        // For production, this should be streaming or indexed.
        
        var history = new List<(string AgentId, long Timestamp)>();

        using var reader = _db.CreateReader(LedgerTableName);
        while (reader.Read())
        {
            // Col 2: AgentId, Col 1: Timestamp (microseconds)
            string agentId = reader.GetString(2);
            long ts = reader.GetInt64(1);
            history.Add((agentId, ts));
        }

        // Sort by time
        history.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Analyze per agent
        var byAgent = history.GroupBy(x => x.AgentId);
        
        foreach (var group in byAgent)
        {
            var agentId = group.Key;
            var entries = group.ToList();
            
            // Sliding window check
            for (int i = 0; i < entries.Count; i++)
            {
                int count = 1;
                long startTs = entries[i].Timestamp;
                long endTs = startTs + (windowSeconds * 1000000L); // Convert to micros

                for (int j = i + 1; j < entries.Count; j++)
                {
                    if (entries[j].Timestamp <= endTs)
                    {
                        count++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (count > countThreshold)
                {
                    anomalies.Add(new AnomalyReport(
                        agentId, 
                        "VelocitySpike", 
                        $"Agent {agentId} created {count} entries in {windowSeconds}s (Threshold: {countThreshold})", 
                        startTs
                    ));
                    
                    // Skip ahead to avoid duplicate reporting for same spike
                    i += count - 1; 
                }
            }
        }

        return anomalies;
    }

    /// <summary>
    /// Detects circular references in the trust chain (e.g., A -> B -> A).
    /// This is a simplified implementation that checks for direct loops or short cycles
    /// based on "Evidence" links if available, or sequential interaction patterns.
    /// Since the current ledger is linear, we infer "links" based on interactions if payloads contained targets.
    /// For this version, we will detect if Agent A and Agent B sequentially dominate the ledger in a ping-pong fashion.
    /// </summary>
    public List<AnomalyReport> DetectCircularReferences(int minCycleCount = 3)
    {
        var anomalies = new List<AnomalyReport>();
        // Real circular reference detection requires parsing payloads for "TargetAgentId" or similar.
        // Assuming typical "endorsement" payloads where Content might enable graph construction.
        // For now, we'll implement a "Ping-Pong" detector: A, B, A, B, A, B...
        
        var history = new List<string>();
        using var reader = _db.CreateReader(LedgerTableName);
        while (reader.Read())
        {
            history.Add(reader.GetString(2));
        }
        
        if (history.Count < minCycleCount * 2) return anomalies;

        for (int i = 0; i < history.Count - 3; i++)
        {
            string a = history[i];
            string b = history[i+1];
            
            if (a == b) continue; // Self-loop is different

            // Check for A, B, A, B pattern
            int cycleMatches = 0;
            for (int k = 0; k < minCycleCount; k++)
            {
                if (i + k * 2 + 1 >= history.Count) break;
                
                if (history[i + k * 2] == a && history[i + k * 2 + 1] == b)
                {
                    cycleMatches++;
                }
                else
                {
                    break;
                }
            }

            if (cycleMatches >= minCycleCount)
            {
                anomalies.Add(new AnomalyReport(
                    a,
                    "CircularReference",
                    $"Ping-Pong pattern detected between {a} and {b} for {cycleMatches} cycles.",
                    0 // Timestamp not easily mapped to a single event
                ));
                
                // Skip this sequence
                i += (cycleMatches * 2) - 1;
            }
        }

        return anomalies;
    }
}
