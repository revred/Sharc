// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Cypher;

/// <summary>
/// Token kinds for the minimal Cypher tokenizer.
/// Uses Sharc's pipe-heavy edge syntax: |> (outgoing), &lt;| (incoming), &lt;|> (bidirectional).
/// </summary>
internal enum CypherTokenKind : byte
{
    /// <summary>End of input.</summary>
    Eof = 0,

    // ── Keywords ──
    /// <summary>MATCH keyword.</summary>
    Match,
    /// <summary>RETURN keyword.</summary>
    Return,
    /// <summary>WHERE keyword.</summary>
    Where,
    /// <summary>AND keyword.</summary>
    And,
    /// <summary>shortestPath keyword.</summary>
    ShortestPath,

    // ── Punctuation ──
    /// <summary>( open paren.</summary>
    LParen,
    /// <summary>) close paren.</summary>
    RParen,
    /// <summary>[ open bracket.</summary>
    LBracket,
    /// <summary>] close bracket.</summary>
    RBracket,
    /// <summary>: colon.</summary>
    Colon,
    /// <summary>. dot.</summary>
    Dot,
    /// <summary>, comma.</summary>
    Comma,
    /// <summary>= equals sign.</summary>
    Equals,
    /// <summary>* star/asterisk.</summary>
    Star,

    // ── Edge operators (shark tooth, matching SharqTokenKind) ──
    /// <summary>|> outgoing edge.</summary>
    Edge,
    /// <summary>&lt;| incoming edge.</summary>
    BackEdge,
    /// <summary>&lt;|> bidirectional edge.</summary>
    BidiEdge,

    // ── Literals ──
    /// <summary>Integer literal.</summary>
    Integer,
    /// <summary>Identifier (variable name, label).</summary>
    Identifier,

    // ── Range ──
    /// <summary>.. range operator (in *..N).</summary>
    DotDot,

    /// <summary>Unrecognized token.</summary>
    Error
}
