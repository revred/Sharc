// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

/// <summary>
/// SQLite storage classes as exposed by Sharc.
/// </summary>
public enum SharcColumnType
{
    /// <summary>NULL value.</summary>
    Null = 0,

    /// <summary>Signed integer (1, 2, 3, 4, 6, or 8 bytes).</summary>
    Integral = 1,

    /// <summary>IEEE 754 64-bit float.</summary>
    Real = 2,

    /// <summary>UTF-8 text string.</summary>
    Text = 3,

    /// <summary>Binary large object.</summary>
    Blob = 4
}
