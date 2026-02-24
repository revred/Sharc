// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharc.Core.Trust;

/// <summary>
/// Represents the dynamic reputation score of an agent using a Bayesian Beta distribution model.
/// </summary>
/// <param name="AgentId">The agent being scored.</param>
/// <param name="Score">A normalized value between 0.0 (untrusted) and 1.0 (fully trusted). Derived as Alpha / (Alpha + Beta).</param>
/// <param name="Confidence">Certainty based on observation count: 1 - 2/(Alpha + Beta + 1). Range 0.0-1.0.</param>
/// <param name="LastUpdated">Unix timestamp (microseconds) of the last update.</param>
/// <param name="RatingCount">Number of events contributing to this score.</param>
/// <param name="Alpha">Bayesian Beta distribution alpha parameter (positive observations + prior).</param>
/// <param name="Beta">Bayesian Beta distribution beta parameter (negative observations + prior).</param>
public record ReputationScore(
    string AgentId,
    double Score,
    double Confidence,
    long LastUpdated,
    int RatingCount,
    double Alpha = 1.0,
    double Beta = 1.0)
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
