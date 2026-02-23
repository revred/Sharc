// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Sharq;

/// <summary>
/// Token types produced by the Sharq tokenizer.
/// </summary>
internal enum SharqTokenKind : byte
{
    // Sentinel
    Eof = 0,

    // Literals
    Identifier,
    Integer,
    Float,
    String,

    // Keywords
    Select,
    From,
    Where,
    Group,
    By,
    Order,
    Asc,
    Desc,
    Limit,
    Offset,
    And,
    Or,
    Not,
    In,
    Between,
    Like,
    Is,
    Null,
    True,
    False,
    As,
    Distinct,
    Case,
    When,
    Then,
    Else,
    End,
    Having,
    Cast,
    With,
    Over,
    Partition,
    Nulls,
    Union,
    Intersect,
    Except,
    Exists,
    UnionAll,       // |a (shorthand for UNION ALL)

    // Execution hints
    Direct,         // DIRECT (explicit default tier)
    Cached,         // CACHED (auto-Prepare tier)
    Jit,            // JIT (auto-Jit tier)

    // DDL
    Create,
    View,

    // Joins
    Join,
    Inner,
    Left,
    Right,
    Cross,
    On,

    // Comparison operators
    Equal,          // =
    NotEqual,       // != or <>
    LessThan,       // <
    GreaterThan,    // >
    LessOrEqual,    // <=
    GreaterOrEqual, // >=

    // Full-text match operators
    Match,          // @@
    MatchAnd,       // @AND@
    MatchOr,        // @OR@

    // Arithmetic
    Plus,           // +
    Minus,          // -
    Slash,          // /
    Percent,        // %

    // Punctuation
    Star,           // *
    Comma,          // ,
    Dot,            // .
    LeftParen,      // (
    RightParen,     // )
    Semicolon,      // ;
    Colon,          // :

    // Parameter reference
    Parameter,      // $identifier

    // Edge operators (graph traversal — shark tooth)
    Edge,           // |>
    BackEdge,       // <|
    BidiEdge,       // <|>
}
