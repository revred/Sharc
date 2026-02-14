// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Filter operators for the byte-level filter engine.
/// Operators evaluate directly against raw SQLite record bytes without materializing ColumnValue structs.
/// </summary>
public enum FilterOp : byte
{
    // â”€â”€ Comparison (P1) â”€â”€

    /// <summary>Column value equals the filter value.</summary>
    Eq = 0,

    /// <summary>Column value does not equal the filter value.</summary>
    Neq = 1,

    /// <summary>Column value is less than the filter value.</summary>
    Lt = 2,

    /// <summary>Column value is less than or equal to the filter value.</summary>
    Lte = 3,

    /// <summary>Column value is greater than the filter value.</summary>
    Gt = 4,

    /// <summary>Column value is greater than or equal to the filter value.</summary>
    Gte = 5,

    /// <summary>Column value is between low and high (inclusive).</summary>
    Between = 6,

    // â”€â”€ Null (P1) â”€â”€

    /// <summary>Column value is NULL (serial type == 0).</summary>
    IsNull = 10,

    /// <summary>Column value is not NULL (serial type != 0).</summary>
    IsNotNull = 11,

    // â”€â”€ String (P1) â”€â”€

    /// <summary>UTF-8 column value starts with the given prefix.</summary>
    StartsWith = 20,

    /// <summary>UTF-8 column value ends with the given suffix.</summary>
    EndsWith = 21,

    /// <summary>UTF-8 column value contains the given substring.</summary>
    Contains = 22,

    // â”€â”€ Set membership (P1) â”€â”€

    /// <summary>Column value is in the given set of values.</summary>
    In = 30,

    /// <summary>Column value is not in the given set of values.</summary>
    NotIn = 31,
}