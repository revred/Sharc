// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Diagnostics.CodeAnalysis;
using Sharc.Exceptions;

namespace Sharc.Core;

/// <summary>
/// Centralized throw methods for hot paths.
/// Keeping throw logic out of the caller enables the JIT to inline the fast path
/// while the cold (error) path remains out-of-line.
/// </summary>
internal static class ThrowHelper
{
    /// <summary>Throws a <see cref="CorruptPageException"/> for a specific page.</summary>
    [DoesNotReturn]
    public static void ThrowCorruptPage(uint pageNumber, string message)
        => throw new CorruptPageException(pageNumber, message);

    /// <summary>Throws an <see cref="InvalidDatabaseException"/>.</summary>
    [DoesNotReturn]
    public static void ThrowInvalidDatabase(string message)
        => throw new InvalidDatabaseException(message);

    /// <summary>Throws an <see cref="UnsupportedFeatureException"/>.</summary>
    [DoesNotReturn]
    public static void ThrowUnsupportedFeature(string feature)
        => throw new UnsupportedFeatureException(feature);

    /// <summary>Throws an <see cref="InvalidOperationException"/>.</summary>
    [DoesNotReturn]
    public static void ThrowInvalidOperation(string message)
        => throw new InvalidOperationException(message);

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/>.</summary>
    [DoesNotReturn]
    public static void ThrowArgumentOutOfRange(string paramName, object? actualValue, string message)
        => throw new ArgumentOutOfRangeException(paramName, actualValue, message);
}