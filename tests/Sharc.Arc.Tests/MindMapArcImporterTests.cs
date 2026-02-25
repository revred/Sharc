// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Arc.Tests;

public sealed class MindMapArcImporterTests
{
    private const string FreeMindSample = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<map version=""1.0.1"">
  <node TEXT=""Project Plan"">
    <node TEXT=""Research"">
      <node TEXT=""Market Analysis""/>
      <node TEXT=""Competitor Review""/>
    </node>
    <node TEXT=""Implementation"">
      <node TEXT=""Backend""/>
      <node TEXT=""Frontend""/>
    </node>
  </node>
</map>";

    private const string OpmlSample = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<opml version=""2.0"">
  <head><title>Brain Dump</title></head>
  <body>
    <outline text=""Ideas"">
      <outline text=""Product Features"" _note=""Priority items"">
        <outline text=""Search""/>
        <outline text=""Export""/>
      </outline>
      <outline text=""Marketing""/>
    </outline>
  </body>
</opml>";

    [Fact]
    public void ParseFreeMind_ExtractsAllNodes()
    {
        var nodes = MindMapArcImporter.ParseFreeMind(FreeMindSample);
        Assert.Equal(7, nodes.Count); // root + Research + 2 sub + Implementation + 2 sub
        Assert.Equal("Project Plan", nodes[0].Text);
        Assert.Equal(0, nodes[0].Depth);
    }

    [Fact]
    public void ParseFreeMind_ParentChildRelationships()
    {
        var nodes = MindMapArcImporter.ParseFreeMind(FreeMindSample);
        var root = nodes[0];
        var research = nodes.Find(n => n.Text == "Research")!;
        var market = nodes.Find(n => n.Text == "Market Analysis")!;

        Assert.Equal(0, root.ParentId); // root has no parent
        Assert.Equal(root.Id, research.ParentId);
        Assert.Equal(research.Id, market.ParentId);
    }

    [Fact]
    public void ParseFreeMind_DepthTracking()
    {
        var nodes = MindMapArcImporter.ParseFreeMind(FreeMindSample);
        Assert.Equal(0, nodes[0].Depth); // root
        Assert.Equal(1, nodes.Find(n => n.Text == "Research")!.Depth);
        Assert.Equal(2, nodes.Find(n => n.Text == "Market Analysis")!.Depth);
    }

    [Fact]
    public void ImportFreeMind_CreatesQueryableArc()
    {
        using var arc = MindMapArcImporter.ImportFreeMind(FreeMindSample);

        // Verify nodes table
        using var reader = arc.Database.CreateReader("nodes");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(7, count);

        // Verify edges table
        using var edgeReader = arc.Database.CreateReader("edges");
        int edgeCount = 0;
        while (edgeReader.Read()) edgeCount++;
        Assert.Equal(6, edgeCount); // 6 parent-child edges (root has no parent)
    }

    [Fact]
    public void ParseOpml_ExtractsAllOutlines()
    {
        var nodes = MindMapArcImporter.ParseOpml(OpmlSample);
        Assert.Equal(5, nodes.Count); // Ideas + Features + Search + Export + Marketing
        Assert.Equal("Ideas", nodes[0].Text);
    }

    [Fact]
    public void ParseOpml_CapturesNotes()
    {
        var nodes = MindMapArcImporter.ParseOpml(OpmlSample);
        var features = nodes.Find(n => n.Text == "Product Features")!;
        Assert.Equal("Priority items", features.Note);
    }

    [Fact]
    public void ImportOpml_CreatesGraphEdges()
    {
        using var arc = MindMapArcImporter.ImportOpml(OpmlSample);

        using var edgeReader = arc.Database.CreateReader("edges");
        int edgeCount = 0;
        while (edgeReader.Read()) edgeCount++;
        Assert.Equal(4, edgeCount); // Ideas→Features, Features→Search, Features→Export, Ideas→Marketing
    }

    [Fact]
    public void ImportFreeMind_FusesWithOtherArcs()
    {
        using var mindmap = MindMapArcImporter.ImportFreeMind(FreeMindSample);
        using var csv = CsvArcImporter.Import("name,role\nAlice,PM",
            new CsvArcImporter.CsvImportOptions { ArcName = "team.arc" });

        using var fused = new FusedArcContext();
        fused.Mount(mindmap);
        fused.Mount(csv);

        var tables = fused.DiscoverTables();
        Assert.True(tables.ContainsKey("nodes"));
        Assert.True(tables.ContainsKey("edges"));
        Assert.True(tables.ContainsKey("data"));
    }
}
