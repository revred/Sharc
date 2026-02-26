// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharc.Core.Trust;

/// <summary>
/// Defines the type of content in a trust payload.
/// </summary>
public enum PayloadType
{
    /// <summary>
    /// Unstructured text content.
    /// </summary>
    Text = 0,
    
    /// <summary>
    /// A financial transaction or commitment.
    /// </summary>
    Financial = 1,
    
    /// <summary>
    /// An approval or authorization decision.
    /// </summary>
    Approval = 2,
    
    /// <summary>
    /// System-level configuration or command.
    /// </summary>
    System = 3
}

/// <summary>
/// Represents a cryptographic reference to a specific row in a database table.
/// When <see cref="RowGuid"/> is present, it provides a globally unique identity
/// that is portable across databases — unlike <see cref="RowId"/> which is local.
/// </summary>
/// <param name="Table">The name of the table containing the evidence.</param>
/// <param name="RowId">The local row ID of the specific record (performance hint).</param>
/// <param name="RowHash">The cryptographic hash of the row content at the time of citation.</param>
/// <param name="RowGuid">Optional globally unique identifier for the row, portable across databases.</param>
public record EvidenceRef(string Table, long RowId, byte[] RowHash, Guid? RowGuid = null);

/// <summary>
/// Represents a structured payload in the distributed ledger.
/// </summary>
/// <param name="Type">The semantic type of the payload.</param>
/// <param name="Content">The primary content (e.g., JSON, text, or XML).</param>
/// <param name="EconomicValue">Optional economic value for authority enforcement (e.g., transaction amount).</param>
/// <param name="Evidence">List of cryptographic references to supporting data.</param>
/// <param name="CoSignatures">Optional list of additional signatures for multi-party approval.</param>
public record TrustPayload(
    PayloadType Type,
    string Content,
    decimal? EconomicValue = null,
    List<EvidenceRef>? Evidence = null,
    List<CoSignature>? CoSignatures = null)
{
    /// <summary>
    /// Serializes the payload to a JSON byte array.
    /// </summary>
    public byte[] ToBytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this, TrustPayloadJsonContext.Default.TrustPayload);
    }

    /// <summary>
    /// Deserializes the payload from a byte array.
    /// </summary>
    public static TrustPayload? FromBytes(ReadOnlySpan<byte> data)
    {
        return JsonSerializer.Deserialize(data, TrustPayloadJsonContext.Default.TrustPayload);
    }
}

/// <summary>
/// JSON context for source-generated serialization of TrustPayload.
/// </summary>
[JsonSerializable(typeof(TrustPayload))]
[JsonSerializable(typeof(EvidenceRef))]
[JsonSerializable(typeof(CoSignature))]
public partial class TrustPayloadJsonContext : JsonSerializerContext
{
}
