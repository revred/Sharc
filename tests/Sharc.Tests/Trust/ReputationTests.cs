using Xunit;
using Sharc.Trust;

namespace Sharc.Tests.Trust;

public class ReputationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private readonly ReputationManager _manager;

    public ReputationTests()
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
    public void GetScore_UnknownAgent_ReturnsDefault()
    {
        var score = _manager.GetScore("unknown-agent");
        
        Assert.Equal("unknown-agent", score.AgentId);
        Assert.Equal(0.5, score.Score);
        Assert.Equal(0.0, score.Confidence);
        Assert.Equal(0, score.RatingCount);
    }

    [Fact]
    public void UpdateScore_UpdatesCache()
    {
        _manager.UpdateScore("agent-smith", 0.9, 0.8);
        
        var score = _manager.GetScore("agent-smith");
        
        Assert.Equal(0.9, score.Score);
        Assert.Equal(0.8, score.Confidence);
        Assert.True(score.RatingCount > 0);
    }

    [Fact]
    public void UpdateScore_Twice_IncrementsRatingCount()
    {
        _manager.UpdateScore("agent-smith", 0.7, 0.5);
        var initial = _manager.GetScore("agent-smith");
        
        _manager.UpdateScore("agent-smith", 0.8, 0.6);
        var updated = _manager.GetScore("agent-smith");
        
        Assert.Equal(initial.RatingCount + 1, updated.RatingCount);
        Assert.True(updated.LastUpdated >= initial.LastUpdated);
    }
}
