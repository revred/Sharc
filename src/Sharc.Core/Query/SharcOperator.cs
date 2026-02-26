// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Query;

/// <summary>
/// Comparison operators for row filtering during table scans.
/// </summary>
public enum SharcOperator
{
    /// <summary>Column value equals the filter value.</summary>
    Equal,

    /// <summary>Column value does not equal the filter value.</summary>
    NotEqual,

    /// <summary>Column value is less than the filter value.</summary>
    LessThan,

    /// <summary>Column value is greater than the filter value.</summary>
    GreaterThan,

    /// <summary>Column value is less than or equal to the filter value.</summary>
    LessOrEqual,

    /// <summary>Column value is greater than or equal to the filter value.</summary>
    GreaterOrEqual
}
