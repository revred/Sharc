// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// Operations supported in a query intent predicate.
/// Values are grouped by category for fast dispatch.
/// </summary>
public enum IntentOp : byte
{
    /// <summary>Equal (==).</summary>
    Eq = 0,
    /// <summary>Not equal (!=).</summary>
    Neq = 1,
    /// <summary>Less than (&lt;).</summary>
    Lt = 2,
    /// <summary>Less than or equal (&lt;=).</summary>
    Lte = 3,
    /// <summary>Greater than (&gt;).</summary>
    Gt = 4,
    /// <summary>Greater than or equal (&gt;=).</summary>
    Gte = 5,
    /// <summary>Inclusive range (BETWEEN low AND high).</summary>
    Between = 6,

    /// <summary>IS NULL test.</summary>
    IsNull = 10,
    /// <summary>IS NOT NULL test.</summary>
    IsNotNull = 11,

    /// <summary>String starts with prefix.</summary>
    StartsWith = 20,
    /// <summary>String ends with suffix.</summary>
    EndsWith = 21,
    /// <summary>String contains substring.</summary>
    Contains = 22,

    /// <summary>Value in set.</summary>
    In = 30,
    /// <summary>Value not in set.</summary>
    NotIn = 31,

    /// <summary>LIKE pattern match.</summary>
    Like = 40,
    /// <summary>NOT LIKE pattern match.</summary>
    NotLike = 41,

    /// <summary>Logical AND of children.</summary>
    And = 100,
    /// <summary>Logical OR of children.</summary>
    Or = 101,
    /// <summary>Logical NOT of child.</summary>
    Not = 102,
}
