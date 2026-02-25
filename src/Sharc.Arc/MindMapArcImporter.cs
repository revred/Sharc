// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Xml;
using Sharc.Core;

namespace Sharc.Arc;

/// <summary>
/// Imports mind map files into .arc files with graph structure.
/// Nodes become rows in a "nodes" table; parent-child relationships become
/// rows in an "edges" table — ready for graph traversal via Sharc.Graph.
/// <para>
/// <b>Supported formats:</b>
/// <list type="bullet">
///   <item>FreeMind (.mm) — XML with nested &lt;node&gt; elements</item>
///   <item>OPML (.opml) — XML with nested &lt;outline&gt; elements</item>
/// </list>
/// </para>
/// </summary>
public static class MindMapArcImporter
{
    /// <summary>Options for mind map import.</summary>
    public sealed class MindMapImportOptions
    {
        /// <summary>Arc file name. Default: "mindmap.arc".</summary>
        public string ArcName { get; set; } = "mindmap.arc";

        /// <summary>Table name for nodes. Default: "nodes".</summary>
        public string NodesTable { get; set; } = "nodes";

        /// <summary>Table name for edges. Default: "edges".</summary>
        public string EdgesTable { get; set; } = "edges";
    }

    /// <summary>A parsed mind map node.</summary>
    public sealed class MindMapNode
    {
        /// <summary>Auto-assigned node ID (1-based).</summary>
        public long Id { get; init; }

        /// <summary>Node text/title.</summary>
        public string Text { get; init; } = "";

        /// <summary>Depth level (0 = root).</summary>
        public int Depth { get; init; }

        /// <summary>Parent node ID (0 = no parent / root).</summary>
        public long ParentId { get; init; }

        /// <summary>Optional note/body text.</summary>
        public string? Note { get; init; }
    }

    /// <summary>
    /// Imports a FreeMind (.mm) file from text.
    /// </summary>
    public static ArcHandle ImportFreeMind(string xmlText, MindMapImportOptions? options = null)
    {
        var nodes = ParseFreeMind(xmlText);
        return BuildArc(nodes, options);
    }

    /// <summary>
    /// Imports an OPML file from text.
    /// </summary>
    public static ArcHandle ImportOpml(string xmlText, MindMapImportOptions? options = null)
    {
        var nodes = ParseOpml(xmlText);
        return BuildArc(nodes, options);
    }

    /// <summary>
    /// Imports from a file, auto-detecting format by extension.
    /// </summary>
    public static ArcHandle ImportFile(string path, MindMapImportOptions? options = null)
    {
        string text = File.ReadAllText(path);
        options ??= new MindMapImportOptions();
        if (options.ArcName == "mindmap.arc")
            options.ArcName = Path.GetFileNameWithoutExtension(path) + ".arc";

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mm" => ImportFreeMind(text, options),
            ".opml" => ImportOpml(text, options),
            _ => throw new ArgumentException($"Unsupported mind map format: {ext}. Use .mm (FreeMind) or .opml.")
        };
    }

    /// <summary>Parses FreeMind XML into a flat node list.</summary>
    public static List<MindMapNode> ParseFreeMind(string xmlText)
    {
        var nodes = new List<MindMapNode>();
        long nextId = 1;

        using var reader = XmlReader.Create(new StringReader(xmlText));
        var parentStack = new Stack<long>(); // parent IDs
        parentStack.Push(0); // root parent = 0

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "node")
            {
                string text = reader.GetAttribute("TEXT") ?? reader.GetAttribute("text") ?? "";
                long parentId = parentStack.Peek();
                int depth = parentStack.Count - 1;

                long id = nextId++;
                nodes.Add(new MindMapNode
                {
                    Id = id,
                    Text = text,
                    Depth = depth,
                    ParentId = parentId
                });

                if (!reader.IsEmptyElement)
                    parentStack.Push(id);
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "node")
            {
                if (parentStack.Count > 1) parentStack.Pop();
            }
        }

        return nodes;
    }

    /// <summary>Parses OPML XML into a flat node list.</summary>
    public static List<MindMapNode> ParseOpml(string xmlText)
    {
        var nodes = new List<MindMapNode>();
        long nextId = 1;

        using var reader = XmlReader.Create(new StringReader(xmlText));
        var parentStack = new Stack<long>();
        parentStack.Push(0);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "outline")
            {
                string text = reader.GetAttribute("text")
                    ?? reader.GetAttribute("title")
                    ?? reader.GetAttribute("TEXT") ?? "";
                string? note = reader.GetAttribute("_note");
                long parentId = parentStack.Peek();
                int depth = parentStack.Count - 1;

                long id = nextId++;
                nodes.Add(new MindMapNode
                {
                    Id = id,
                    Text = text,
                    Depth = depth,
                    ParentId = parentId,
                    Note = note
                });

                if (!reader.IsEmptyElement)
                    parentStack.Push(id);
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "outline")
            {
                if (parentStack.Count > 1) parentStack.Pop();
            }
        }

        return nodes;
    }

    // ── Private ──

    private static ArcHandle BuildArc(List<MindMapNode> nodes, MindMapImportOptions? options)
    {
        options ??= new MindMapImportOptions();
        if (nodes.Count == 0)
            throw new ArgumentException("Mind map contains no nodes.");

        var handle = ArcHandle.CreateInMemory(options.ArcName);
        var db = handle.Database;

        // Create tables
        using (var tx = db.BeginTransaction())
        {
            tx.Execute($"CREATE TABLE [{options.NodesTable}] (id INTEGER PRIMARY KEY, text TEXT, depth INTEGER, note TEXT)");
            tx.Execute($"CREATE TABLE [{options.EdgesTable}] (parent_id INTEGER, child_id INTEGER, relation TEXT)");
            tx.Commit();
        }

        // Insert nodes
        using var writer = SharcWriter.From(db);
        foreach (var node in nodes)
        {
            var textBytes = Encoding.UTF8.GetBytes(node.Text);
            if (node.Note != null)
            {
                var noteBytes = Encoding.UTF8.GetBytes(node.Note);
                writer.Insert(options.NodesTable,
                    ColumnValue.FromInt64(4, node.Id),
                    ColumnValue.Text(textBytes.Length * 2 + 13, textBytes),
                    ColumnValue.FromInt64(1, node.Depth),
                    ColumnValue.Text(noteBytes.Length * 2 + 13, noteBytes));
            }
            else
            {
                writer.Insert(options.NodesTable,
                    ColumnValue.FromInt64(4, node.Id),
                    ColumnValue.Text(textBytes.Length * 2 + 13, textBytes),
                    ColumnValue.FromInt64(1, node.Depth),
                    ColumnValue.Null());
            }
        }

        // Insert edges (parent → child)
        var relationBytes = Encoding.UTF8.GetBytes("child_of");
        foreach (var node in nodes)
        {
            if (node.ParentId > 0)
            {
                writer.Insert(options.EdgesTable,
                    ColumnValue.FromInt64(4, node.ParentId),
                    ColumnValue.FromInt64(4, node.Id),
                    ColumnValue.Text(relationBytes.Length * 2 + 13, relationBytes));
            }
        }

        return handle;
    }
}
