namespace Sharc.Core.Trust;

/// <summary>
/// Represents a secondary signature on a payload, used for multi-party authorization.
/// </summary>
/// <param name="SignerId">The ID of the co-signing agent.</param>
/// <param name="Signature">The cryptographic signature of the payload.</param>
/// <param name="Timestamp">When the co-signature was applied.</param>
public record CoSignature(
    string SignerId,
    byte[] Signature,
    long Timestamp);
