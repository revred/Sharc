using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharc.Core.Trust;

/// <summary>
/// Represents the dynamic reputation score of an agent.
/// </summary>
/// <param name="AgentId">The agent being scored.</param>
/// <param name="Score">A normalized value between 0.0 (untrusted) and 1.0 (fully trusted).</param>
/// <param name="Confidence">Metric of certainty based on history depth (0.0 - 1.0).</param>
/// <param name="LastUpdated">Unix timestamp (microseconds) of the last update.</param>
/// <param name="RatingCount">Number of events contributing to this score.</param>
public record ReputationScore(
    string AgentId,
    double Score,
    double Confidence,
    long LastUpdated,
    int RatingCount)
{
    /// <summary>
    /// Serializes the score to a JSON byte array.
    /// </summary>
    public byte[] ToBytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this, ReputationJsonContext.Default.ReputationScore);
    }

    /// <summary>
    /// Deserializes the score from a byte array.
    /// </summary>
    public static ReputationScore? FromBytes(ReadOnlySpan<byte> data)
    {
        return JsonSerializer.Deserialize(data, ReputationJsonContext.Default.ReputationScore);
    }
}

/// <summary>
/// JSON context for source-generated serialization of ReputationScore.
/// </summary>
[JsonSerializable(typeof(ReputationScore))]
public partial class ReputationJsonContext : JsonSerializerContext
{
}
