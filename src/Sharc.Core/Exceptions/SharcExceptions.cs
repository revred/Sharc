// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
/// Encryption/decryption error Ã¢â‚¬â€ wrong password, tampered data, or unsupported cipher.
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