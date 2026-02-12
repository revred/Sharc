/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Exceptions;

/// <summary>
/// Base exception for all Sharc errors.
/// </summary>
public class SharcException : Exception
{
    /// <inheritdoc />
    public SharcException(string message) : base(message) { }
    /// <inheritdoc />
    public SharcException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The file is not a valid SQLite database or is corrupted.
/// </summary>
public sealed class InvalidDatabaseException : SharcException
{
    /// <inheritdoc />
    public InvalidDatabaseException(string message) : base(message) { }
    /// <inheritdoc />
    public InvalidDatabaseException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A database page is corrupted or fails integrity checks.
/// </summary>
public sealed class CorruptPageException : SharcException
{
    /// <summary>The page number that failed validation.</summary>
    public uint PageNumber { get; }

    /// <summary>Initializes a new instance with the faulting page number and message.</summary>
    public CorruptPageException(uint pageNumber, string message)
        : base($"Page {pageNumber}: {message}")
    {
        PageNumber = pageNumber;
    }
}

/// <summary>
/// Encryption/decryption error â€” wrong password, tampered data, or unsupported cipher.
/// </summary>
public sealed class SharcCryptoException : SharcException
{
    /// <inheritdoc />
    public SharcCryptoException(string message) : base(message) { }
    /// <inheritdoc />
    public SharcCryptoException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The database uses a feature not yet supported by Sharc.
/// </summary>
public sealed class UnsupportedFeatureException : SharcException
{
    /// <summary>Initializes a new instance for the unsupported feature.</summary>
    public UnsupportedFeatureException(string feature)
        : base($"Unsupported SQLite feature: {feature}") { }
}
