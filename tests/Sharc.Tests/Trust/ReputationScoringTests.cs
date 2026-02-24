// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;
using Sharc.Trust;

namespace Sharc.Tests.Trust;

public class ReputationScoringTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private readonly ReputationManager _manager;

    public ReputationScoringTests()
    {
        _dbPath = Path.GetTempFileName();
        var data = TrustTestFixtures.CreateTrustDatabase();
        File.WriteAllBytes(_dbPath, data);

        _db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        _manager = new ReputationManager(_db);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetScore_UnknownAgent_ReturnsDefaultHalf()
    {
        var score = _manager.GetScore("unknown-agent");

        Assert.Equal("unknown-agent", score.AgentId);
        Assert.Equal(0.5, score.Score);
        Assert.Equal(0.0, score.Confidence);
        Assert.Equal(0, score.RatingCount);
        Assert.Equal(1.0, score.Alpha);
        Assert.Equal(1.0, score.Beta);
    }

    [Fact]
    public void RecordObservation_SinglePositive_IncreasesScore()
    {
        _manager.RecordObservation("agent-a", success: true);
        var score = _manager.GetScore("agent-a");

        Assert.True(score.Score > 0.5, $"Score should increase above 0.5, was {score.Score}");
        Assert.Equal(1, score.RatingCount);
        Assert.True(score.Alpha > 1.0);
        Assert.Equal(1.0, score.Beta, 5); // Beta unchanged
    }

    [Fact]
    public void RecordObservation_SingleNegative_DecreasesScore()
    {
        _manager.RecordObservation("agent-b", success: false);
        var score = _manager.GetScore("agent-b");

        Assert.True(score.Score < 0.5, $"Score should decrease below 0.5, was {score.Score}");
        Assert.Equal(1, score.RatingCount);
        Assert.Equal(1.0, score.Alpha, 5); // Alpha unchanged
        Assert.True(score.Beta > 1.0);
    }

    [Fact]
    public void RecordObservation_MultiplePositive_ConvergesToHigh()
    {
        for (int i = 0; i < 10; i++)
            _manager.RecordObservation("agent-c", success: true);

        var score = _manager.GetScore("agent-c");

        Assert.True(score.Score > 0.8, $"Score should be > 0.8 after 10 positive, was {score.Score}");
        Assert.True(score.Confidence > 0.5, $"Confidence should grow, was {score.Confidence}");
        Assert.Equal(10, score.RatingCount);
    }

    [Fact]
    public void RecordObservation_MixedObservations_ReflectsRatio()
    {
        // 7 positive, 3 negative → expect score around 0.7
        for (int i = 0; i < 7; i++)
            _manager.RecordObservation("agent-d", success: true);
        for (int i = 0; i < 3; i++)
            _manager.RecordObservation("agent-d", success: false);

        var score = _manager.GetScore("agent-d");

        Assert.True(score.Score > 0.55 && score.Score < 0.85,
            $"Score should reflect ~70% positive ratio, was {score.Score}");
        Assert.Equal(10, score.RatingCount);
    }

    [Fact]
    public void UpdateScore_RoundTrip_PersistsAndReloads()
    {
        _manager.RecordObservation("agent-persist", success: true);
        _manager.RecordObservation("agent-persist", success: true);
        _manager.RecordObservation("agent-persist", success: false);

        var original = _manager.GetScore("agent-persist");

        // Create a new manager to force reload from database
        var manager2 = new ReputationManager(_db);
        var reloaded = manager2.GetScore("agent-persist");

        Assert.Equal(original.AgentId, reloaded.AgentId);
        Assert.Equal(original.Score, reloaded.Score, 5);
        Assert.Equal(original.RatingCount, reloaded.RatingCount);
        Assert.Equal(original.Alpha, reloaded.Alpha, 5);
        Assert.Equal(original.Beta, reloaded.Beta, 5);
    }

    [Fact]
    public void UpdateScore_Legacy_MapsToAlphaBeta()
    {
        _manager.UpdateScore("agent-legacy", 0.9, 0.8);
        var score = _manager.GetScore("agent-legacy");

        Assert.Equal(0.9, score.Score, 2);
        Assert.Equal(0.8, score.Confidence, 2);
        Assert.True(score.Alpha > 1.0);
        Assert.True(score.Beta > 0.0);
        Assert.True(score.Alpha > score.Beta, "Alpha should be > Beta for score > 0.5");
    }

    [Fact]
    public void Confidence_IncreasesWithObservations()
    {
        _manager.RecordObservation("agent-conf", success: true);
        var after1 = _manager.GetScore("agent-conf");

        for (int i = 0; i < 9; i++)
            _manager.RecordObservation("agent-conf", success: true);
        var after10 = _manager.GetScore("agent-conf");

        Assert.True(after10.Confidence > after1.Confidence,
            $"Confidence should grow: after1={after1.Confidence}, after10={after10.Confidence}");
    }

    [Fact]
    public void RecordObservation_Weight_HasProportionalEffect()
    {
        // Weight=3 positive should increase score more than weight=1
        _manager.RecordObservation("agent-w1", success: true, weight: 1.0);
        var score1 = _manager.GetScore("agent-w1");

        _manager.RecordObservation("agent-w3", success: true, weight: 3.0);
        var score3 = _manager.GetScore("agent-w3");

        Assert.True(score3.Score > score1.Score,
            $"Higher weight should give higher score: w1={score1.Score}, w3={score3.Score}");
    }
}
