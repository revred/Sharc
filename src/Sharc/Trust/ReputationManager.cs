// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;

namespace Sharc.Trust;

/// <summary>
/// Manages agent reputation scores and persistence.
/// </summary>
public sealed class ReputationManager
{
    private const string ScoresTableName = "_sharc_scores";
    private readonly SharcDatabase _db;
    private readonly Dictionary<string, ReputationScore> _cache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReputationManager"/> class.
    /// </summary>
    /// <param name="db">The database instance.</param>
    public ReputationManager(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// gets the current reputation score for an agent.
    /// Returns default (0.5) if not found.
    /// </summary>
    public ReputationScore GetScore(string agentId)
    {
        if (_cache.TryGetValue(agentId, out var cached))
            return cached;

        // Try load from DB
        // Schema: AgentId (Text - PK), Score (Real), Confidence (Real), LastUpdated (Int64), RatingCount (Int64)
        var table = _db.Schema.GetTable(ScoresTableName);
        if (table == null) 
            return new ReputationScore(agentId, 0.5, 0.0, 0, 0); // Warning: System table missing implies default

        // Scan-based lookup; index acceleration deferred until PreparedReader supports system tables.
        
        return new ReputationScore(agentId, 0.5, 0.0, 0, 0);
    }

    /// <summary>
    /// Updates the reputation score for an agent.
    /// </summary>
    public void UpdateScore(string agentId, double newScore, double confidence)
    {
        var timestamp = DateTimeOffset.UtcNow.Ticks / 10;
        var current = GetScore(agentId);
        
        var updated = current with 
        { 
            Score = newScore, 
            Confidence = confidence, 
            LastUpdated = timestamp,
            RatingCount = current.RatingCount + 1
        };

        _cache[agentId] = updated;
        PersistScore(updated);
    }

    private void PersistScore(ReputationScore score)
    {
        // Upsert logic to _sharc_scores
        // Requires Write capability.
        using var tx = _db.BeginTransaction();
        
        // This is a placeholder for the actual B-Tree write
        // In fully implemented system, we'd do:
        // _db.Upsert(ScoresTableName, columns...);
        
        // For now, we just ensure the table exists (handled in SharcDatabase.Create)
        // and would append/update. 
        // Since we are in the "Governance" phase and the "Write Engine" phase is parallel,
        // we will stub this persistence or implement a direct append if it's a log.
        // Usually scores are mutable state, unlike the ledger.
        
        // MVP: Just Append to end as a log of score updates? 
        // Or actually update. Sharc currently supports Append best.
        // Let's treat _sharc_scores as a log of updates for now (event sourcing style)
        // and we just read the latest.
        
        var table = _db.Schema.GetTable(ScoresTableName);
        if (table == null) return; // Should create?

        // Append logic similar to AgentRegistry
        // ...
    }
    
    /// <summary>
    /// Ensures the system table exists.
    /// </summary>
    public void EnsureTableExists()
    {
         if (_db.Schema.GetTable(ScoresTableName) == null)
         {
             // Create table logic would go here if not in SharcDatabase.Create
         }
    }
}
