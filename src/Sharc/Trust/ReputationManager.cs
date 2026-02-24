// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;
using Sharc.Core.Storage;
using Sharc.Core.Trust;

namespace Sharc.Trust;

/// <summary>
/// Manages agent reputation scores using a Bayesian Beta distribution model.
/// Scores are persisted to the <c>_sharc_scores</c> system table via append-only log.
/// </summary>
/// <remarks>
/// The Beta distribution model:
/// - Prior: Alpha=1, Beta=1 (uniform — no opinion)
/// - Positive observation (success=true): Alpha += weight
/// - Negative observation (success=false): Beta += weight
/// - Score = Alpha / (Alpha + Beta) — expected value of the Beta distribution
/// - Confidence = 1 - 2 / (Alpha + Beta + 1) — increases with more observations
///
/// Time decay: older Alpha/Beta values decay toward the prior (1.0) over time,
/// ensuring stale agents don't retain high/low scores indefinitely.
/// </remarks>
public sealed class ReputationManager
{
    private const string ScoresTableName = "_sharc_scores";

    /// <summary>Default prior alpha (uniform distribution).</summary>
    private const double DefaultAlpha = 1.0;

    /// <summary>Default prior beta (uniform distribution).</summary>
    private const double DefaultBeta = 1.0;

    /// <summary>Time decay half-life in microseconds (30 days).</summary>
    private const long DecayHalfLifeUs = 30L * 24 * 3600 * 1_000_000;

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
    /// Gets the current reputation score for an agent.
    /// Returns default (0.5 score, Alpha=1, Beta=1) if not found.
    /// </summary>
    public ReputationScore GetScore(string agentId)
    {
        if (_cache.TryGetValue(agentId, out var cached))
            return cached;

        var loaded = LoadFromDatabase(agentId);
        if (loaded != null)
        {
            _cache[agentId] = loaded;
            return loaded;
        }

        return DefaultScore(agentId);
    }

    /// <summary>
    /// Records an observation (positive or negative) for an agent and updates the score.
    /// </summary>
    /// <param name="agentId">The agent to update.</param>
    /// <param name="success">True for positive observation, false for negative.</param>
    /// <param name="weight">Observation weight (default 1.0). Higher values have more impact.</param>
    public void RecordObservation(string agentId, bool success, double weight = 1.0)
    {
        var current = GetScore(agentId);
        var now = DateTimeOffset.UtcNow.Ticks / 10; // microseconds

        // Apply time decay to existing parameters
        double decayedAlpha = ApplyDecay(current.Alpha, DefaultAlpha, current.LastUpdated, now);
        double decayedBeta = ApplyDecay(current.Beta, DefaultBeta, current.LastUpdated, now);

        // Update parameters
        double newAlpha = success ? decayedAlpha + weight : decayedAlpha;
        double newBeta = success ? decayedBeta : decayedBeta + weight;

        // Compute derived values
        double score = newAlpha / (newAlpha + newBeta);
        double confidence = 1.0 - 2.0 / (newAlpha + newBeta + 1.0);

        var updated = new ReputationScore(
            agentId, score, confidence, now, current.RatingCount + 1, newAlpha, newBeta);

        _cache[agentId] = updated;
        PersistScore(updated);
    }

    /// <summary>
    /// Updates the reputation score for an agent directly (legacy API).
    /// Maps the provided score to Alpha/Beta parameters.
    /// </summary>
    public void UpdateScore(string agentId, double newScore, double confidence)
    {
        var current = GetScore(agentId);
        var now = DateTimeOffset.UtcNow.Ticks / 10;

        // Reverse-engineer Alpha/Beta from score and confidence.
        // score = alpha/(alpha+beta), confidence = 1 - 2/(alpha+beta+1)
        // total = alpha+beta = (2/(1-confidence)) - 1 (when confidence < 1)
        double total = confidence < 0.999 ? (2.0 / (1.0 - confidence)) - 1.0 : 100.0;
        total = Math.Max(total, 2.0); // minimum total
        double alpha = newScore * total;
        double beta = total - alpha;

        var updated = new ReputationScore(
            agentId, newScore, confidence, now, current.RatingCount + 1, alpha, beta);

        _cache[agentId] = updated;
        PersistScore(updated);
    }

    /// <summary>
    /// Applies exponential time decay toward the prior value.
    /// </summary>
    private static double ApplyDecay(double value, double prior, long lastUpdatedUs, long nowUs)
    {
        if (lastUpdatedUs <= 0 || nowUs <= lastUpdatedUs)
            return value;

        long elapsed = nowUs - lastUpdatedUs;
        double decayFactor = Math.Pow(0.5, (double)elapsed / DecayHalfLifeUs);
        return prior + (value - prior) * decayFactor;
    }

    private void PersistScore(ReputationScore score)
    {
        var table = _db.Schema.GetTable(ScoresTableName);
        if (table == null) return; // System table not provisioned

        var rootPage = SystemStore.GetRootPage(_db.Schema, ScoresTableName);
        using var tx = _db.BeginTransaction();

        var cols = new[]
        {
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(score.AgentId)),
            ColumnValue.FromDouble(score.Score),
            ColumnValue.FromDouble(score.Confidence),
            ColumnValue.FromInt64(0, score.LastUpdated),
            ColumnValue.FromInt64(0, score.RatingCount),
            ColumnValue.FromDouble(score.Alpha),
            ColumnValue.FromDouble(score.Beta),
        };

        // Append-only: each UpdateScore creates a new row with incrementing rowid.
        // GetScore reads the latest entry for an agentId.
        long rowId = score.LastUpdated; // Use timestamp as rowid for natural ordering
        SystemStore.InsertRecord(tx.GetShadowSource(), _db.Header.UsablePageSize, rootPage, rowId, cols);
        tx.Commit();
    }

    private ReputationScore? LoadFromDatabase(string agentId)
    {
        var table = _db.Schema.GetTable(ScoresTableName);
        if (table == null) return null;

        // Scan the scores table for the latest entry matching this agentId.
        // Since we append with timestamp-based rowids, the last matching row is the latest.
        using var reader = _db.CreateReader(ScoresTableName);
        ReputationScore? latest = null;

        while (reader.Read())
        {
            if (reader.IsNull(0)) continue;
            var id = reader.GetString(0);
            if (!string.Equals(id, agentId, StringComparison.Ordinal)) continue;

            latest = new ReputationScore(
                id,
                reader.IsNull(1) ? 0.5 : reader.GetDouble(1),
                reader.IsNull(2) ? 0.0 : reader.GetDouble(2),
                reader.IsNull(3) ? 0 : reader.GetInt64(3),
                reader.IsNull(4) ? 0 : (int)reader.GetInt64(4),
                reader.IsNull(5) ? DefaultAlpha : reader.GetDouble(5),
                reader.IsNull(6) ? DefaultBeta : reader.GetDouble(6));
        }

        return latest;
    }

    private static ReputationScore DefaultScore(string agentId) =>
        new(agentId, 0.5, 0.0, 0, 0, DefaultAlpha, DefaultBeta);
}
