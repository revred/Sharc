// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Cypher;

/// <summary>
/// Recursive descent parser for minimal Cypher syntax with Sharc's pipe-heavy edge operators.
/// Supported grammar:
///   MATCH (a) |> [r:KIND] |> (b) RETURN b
///   MATCH (a) |> [r:KIND*..N] |> (b) WHERE a.key = X RETURN b
///   MATCH (a) &lt;| [r:KIND] &lt;| (b) WHERE a.key = X RETURN b
///   MATCH p = shortestPath((a) |> [*] |> (b)) WHERE a.key = X AND b.key = Y RETURN p
/// </summary>
internal ref struct CypherParser
{
    private CypherTokenizer _tokenizer;
    private CypherToken _current;
    private readonly ReadOnlySpan<char> _input;

    public CypherParser(ReadOnlySpan<char> input)
    {
        _input = input;
        _tokenizer = new CypherTokenizer(input);
        _current = _tokenizer.Next();
    }

    public CypherMatchStatement Parse()
    {
        Expect(CypherTokenKind.Match);

        var stmt = new CypherMatchStatement();

        // Check for path variable: p = shortestPath(...)
        if (_current.Kind == CypherTokenKind.Identifier)
        {
            string possibleVar = _current.GetText(_input).ToString();
            Advance();

            if (_current.Kind == CypherTokenKind.Equals)
            {
                stmt.PathVariable = possibleVar;
                Advance(); // consume '='

                if (_current.Kind == CypherTokenKind.ShortestPath)
                {
                    stmt.IsShortestPath = true;
                    Advance(); // consume 'shortestPath'
                    Expect(CypherTokenKind.LParen); // outer paren

                    ParsePattern(stmt);

                    Expect(CypherTokenKind.RParen); // close outer paren
                }
                else
                {
                    throw new FormatException("Expected 'shortestPath' after '='.");
                }
            }
            else
            {
                throw new FormatException(
                    $"Unexpected token after identifier '{possibleVar}'. " +
                    "Expected '(' for node pattern or '=' for path assignment.");
            }
        }
        else
        {
            ParsePattern(stmt);
        }

        // WHERE clause
        if (_current.Kind == CypherTokenKind.Where)
        {
            Advance();
            ParseWhereClause(stmt);
        }

        // RETURN clause
        if (_current.Kind == CypherTokenKind.Return)
        {
            Advance();
            ParseReturnClause(stmt);
        }

        return stmt;
    }

    private void ParsePattern(CypherMatchStatement stmt)
    {
        // Start node: (a)
        stmt.StartNode = ParseNodePattern();

        // Edge operator: |> (outgoing), <| (incoming), <|> (bidirectional)
        if (_current.Kind is CypherTokenKind.Edge or CypherTokenKind.BackEdge or CypherTokenKind.BidiEdge)
        {
            var edgeKind = _current.Kind;
            Advance(); // consume edge operator

            CypherRelPattern rel;
            if (_current.Kind == CypherTokenKind.LBracket)
            {
                rel = ParseRelPattern();
            }
            else
            {
                rel = new CypherRelPattern();
            }

            rel.Direction = edgeKind switch
            {
                CypherTokenKind.Edge => CypherDirection.Outgoing,
                CypherTokenKind.BackEdge => CypherDirection.Incoming,
                CypherTokenKind.BidiEdge => CypherDirection.Both,
                _ => CypherDirection.Outgoing
            };

            // Expect closing edge operator before end node
            if (_current.Kind is CypherTokenKind.Edge or CypherTokenKind.BackEdge or CypherTokenKind.BidiEdge)
                Advance();

            stmt.Relationship = rel;
            stmt.EndNode = ParseNodePattern();
        }
    }

    private CypherNodePattern ParseNodePattern()
    {
        Expect(CypherTokenKind.LParen);
        var node = new CypherNodePattern();

        if (_current.Kind == CypherTokenKind.Identifier)
        {
            node.Variable = _current.GetText(_input).ToString();
            Advance();
        }

        Expect(CypherTokenKind.RParen);
        return node;
    }

    private CypherRelPattern ParseRelPattern()
    {
        Expect(CypherTokenKind.LBracket);
        var rel = new CypherRelPattern();

        // Optional variable name
        if (_current.Kind == CypherTokenKind.Identifier)
        {
            rel.Variable = _current.GetText(_input).ToString();
            Advance();
        }

        // Optional :KIND
        if (_current.Kind == CypherTokenKind.Colon)
        {
            Advance();
            if (_current.Kind == CypherTokenKind.Identifier)
            {
                string kindName = _current.GetText(_input).ToString();
                if (int.TryParse(kindName, out int kindVal))
                    rel.Kind = kindVal;
                else
                    rel.Kind = ResolveKindName(kindName);
                Advance();
            }
            else if (_current.Kind == CypherTokenKind.Integer)
            {
                rel.Kind = SafeCastToInt(_current.IntegerValue, "kind");
                Advance();
            }
        }

        // Optional *..N (variable length)
        if (_current.Kind == CypherTokenKind.Star)
        {
            rel.IsVariableLength = true;
            Advance();

            if (_current.Kind == CypherTokenKind.DotDot)
            {
                Advance();
                if (_current.Kind == CypherTokenKind.Integer)
                {
                    rel.MaxHops = SafeCastToInt(_current.IntegerValue, "max hops");
                    Advance();
                }
            }
            else if (_current.Kind == CypherTokenKind.Integer)
            {
                // *N shorthand
                rel.MaxHops = SafeCastToInt(_current.IntegerValue, "max hops");
                Advance();
            }
        }

        Expect(CypherTokenKind.RBracket);
        return rel;
    }

    private void ParseWhereClause(CypherMatchStatement stmt)
    {
        do
        {
            var constraint = ParseWhereConstraint();
            stmt.WhereConstraints.Add(constraint);

        } while (_current.Kind == CypherTokenKind.And && AdvanceAndContinue());
    }

    private CypherWhereClause ParseWhereConstraint()
    {
        var clause = new CypherWhereClause();

        if (_current.Kind != CypherTokenKind.Identifier)
            throw new FormatException("Expected identifier in WHERE clause.");

        clause.Variable = _current.GetText(_input).ToString();
        Advance();

        Expect(CypherTokenKind.Dot);

        if (_current.Kind != CypherTokenKind.Identifier)
            throw new FormatException("Expected property name after '.'.");

        clause.Property = _current.GetText(_input).ToString();
        Advance();

        Expect(CypherTokenKind.Equals);

        if (_current.Kind != CypherTokenKind.Integer)
            throw new FormatException("Expected integer value in WHERE clause.");

        clause.Value = _current.IntegerValue;
        Advance();

        return clause;
    }

    private void ParseReturnClause(CypherMatchStatement stmt)
    {
        if (_current.Kind == CypherTokenKind.Identifier)
        {
            stmt.ReturnVariables.Add(_current.GetText(_input).ToString());
            Advance();

            while (_current.Kind == CypherTokenKind.Comma)
            {
                Advance();
                if (_current.Kind == CypherTokenKind.Identifier)
                {
                    stmt.ReturnVariables.Add(_current.GetText(_input).ToString());
                    Advance();
                }
            }
        }
    }

    private void Expect(CypherTokenKind kind)
    {
        if (_current.Kind != kind)
            throw new FormatException($"Expected {kind} but got {_current.Kind} at position {_current.Start}.");
        Advance();
    }

    private void Advance()
    {
        _current = _tokenizer.Next();
    }

    private bool AdvanceAndContinue()
    {
        Advance();
        return true;
    }

    /// <summary>
    /// Safely casts a long integer value to int, throwing FormatException on overflow.
    /// </summary>
    private static int SafeCastToInt(long value, string context)
    {
        if (value < int.MinValue || value > int.MaxValue)
            throw new FormatException($"Value {value} for {context} exceeds Int32 range.");
        return (int)value;
    }

    /// <summary>
    /// Resolves a RelationKind name to its integer value.
    /// </summary>
    private static int ResolveKindName(string name) => name.ToUpperInvariant() switch
    {
        "CONTAINS" => 10,
        "DEFINES" => 11,
        "IMPORTS" => 12,
        "INHERITS" => 13,
        "IMPLEMENTS" => 14,
        "CALLS" => 15,
        "INSTANTIATES" => 16,
        "READS" => 17,
        "WRITES" => 18,
        "ADDRESSES" => 19,
        "EXPLAINS" => 20,
        "MENTIONEDIN" => 21,
        "REFERSTO" => 30,
        "FOLLOWS" => 31,
        "AUTHORED" => 40,
        "MODIFIED" => 41,
        "PARENTOF" => 42,
        "COMODIFIED" => 43,
        "BLAMEDFOR" => 44,
        "PRODUCEDBY" => 45,
        "ANNOTATESAT" => 46,
        "REVERTSTO" => 47,
        "BRANCHESFROM" => 48,
        "OWNEDBY" => 49,
        "SNAPSHOTOF" => 50,
        "CONTAINSANNOTATION" => 51,
        _ => int.TryParse(name, out int v) ? v : throw new FormatException($"Unknown relation kind: '{name}'")
    };
}
