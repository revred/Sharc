// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Arc.Tests;

/// <summary>
/// Tests for <see cref="CsvArcImporter"/> — CSV to .arc file import.
/// </summary>
public sealed class CsvArcImporterTests
{
    [Fact]
    public void Import_BasicCsv_CreatesQueryableArc()
    {
        var csv = "name,age\nAlice,30\nBob,25";

        using var arc = CsvArcImporter.Import(csv);
        Assert.Equal("imported.arc", arc.Name);

        using var reader = arc.Database.CreateReader("data");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Import_InfersIntegerType()
    {
        var csv = "id,value\n1,100\n2,200\n3,300";

        var schema = CsvArcImporter.InferSchemaOnly(csv);
        Assert.Equal(2, schema.Length);
        Assert.Equal("INTEGER", schema[0].SqlType);
        Assert.Equal("INTEGER", schema[1].SqlType);
    }

    [Fact]
    public void Import_InfersRealType()
    {
        var csv = "metric,score\naccuracy,0.95\nprecision,0.87";

        var schema = CsvArcImporter.InferSchemaOnly(csv);
        Assert.Equal("TEXT", schema[0].SqlType);
        Assert.Equal("REAL", schema[1].SqlType);
    }

    [Fact]
    public void Import_MixedTypes_FallsBackToText()
    {
        var csv = "id,data\n1,hello\n2,world";

        var schema = CsvArcImporter.InferSchemaOnly(csv);
        Assert.Equal("INTEGER", schema[0].SqlType);
        Assert.Equal("TEXT", schema[1].SqlType);
    }

    [Fact]
    public void Import_CustomTableName()
    {
        var csv = "name\nAlice";
        var options = new CsvArcImporter.CsvImportOptions { TableName = "employees" };

        using var arc = CsvArcImporter.Import(csv, options);
        var table = arc.Database.Schema.GetTable("employees");
        Assert.NotNull(table);
    }

    [Fact]
    public void Import_NoHeader_GeneratesColumnNames()
    {
        var csv = "Alice,30\nBob,25";
        var options = new CsvArcImporter.CsvImportOptions { HasHeader = false };

        var schema = CsvArcImporter.InferSchemaOnly(csv, options);
        Assert.Equal("col1", schema[0].Name);
        Assert.Equal("col2", schema[1].Name);
    }

    [Fact]
    public void Import_QuotedFields_ParsedCorrectly()
    {
        var csv = "name,city\n\"O'Brien\",\"New York\"\n\"Smith, Jr.\",London";

        using var arc = CsvArcImporter.Import(csv);
        using var reader = arc.Database.CreateReader("data");

        Assert.True(reader.Read());
        Assert.Equal("O'Brien", reader.GetString(0));
        Assert.Equal("New York", reader.GetString(1));

        Assert.True(reader.Read());
        Assert.Equal("Smith, Jr.", reader.GetString(0));
    }

    [Fact]
    public void Import_EscapedQuotes_ParsedCorrectly()
    {
        var row = CsvArcImporter.ParseRow("\"He said \"\"hello\"\"\",world", ',');
        Assert.Equal("He said \"hello\"", row[0]);
        Assert.Equal("world", row[1]);
    }

    [Fact]
    public void Import_TabDelimited()
    {
        var csv = "name\tage\nAlice\t30";
        var options = new CsvArcImporter.CsvImportOptions { Delimiter = '\t' };

        using var arc = CsvArcImporter.Import(csv, options);
        using var reader = arc.Database.CreateReader("data");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
    }

    [Fact]
    public void Import_MaxRows_Limits()
    {
        var csv = "id\n1\n2\n3\n4\n5";
        var options = new CsvArcImporter.CsvImportOptions { MaxRows = 3 };

        using var arc = CsvArcImporter.Import(csv, options);
        using var reader = arc.Database.CreateReader("data");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(3, count);
    }

    [Fact]
    public void Import_EmptyCells_StoredAsNull()
    {
        var csv = "name,age\nAlice,\n,25";

        using var arc = CsvArcImporter.Import(csv);
        using var reader = arc.Database.CreateReader("data");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.True(reader.IsNull(1)); // empty age

        Assert.True(reader.Read());
        Assert.True(reader.IsNull(0)); // empty name
    }

    [Fact]
    public void Import_SanitizesColumnNames()
    {
        var csv = "First Name,Last-Name,123id\nAlice,Smith,1";

        var schema = CsvArcImporter.InferSchemaOnly(csv);
        Assert.Equal("First_Name", schema[0].Name);
        Assert.Equal("Last_Name", schema[1].Name);
        Assert.Equal("_123id", schema[2].Name); // digit-first gets underscore prefix
    }

    [Fact]
    public void Import_ThenFuse_WorksTogether()
    {
        var csv1 = "dept,budget\nEngineering,500000\nMarketing,300000";
        var csv2 = "dept,headcount\nEngineering,50\nMarketing,30";

        using var arc1 = CsvArcImporter.Import(csv1,
            new CsvArcImporter.CsvImportOptions { TableName = "departments", ArcName = "budgets.arc" });
        using var arc2 = CsvArcImporter.Import(csv2,
            new CsvArcImporter.CsvImportOptions { TableName = "departments", ArcName = "headcount.arc" });

        using var fused = new FusedArcContext();
        fused.Mount(arc1);
        fused.Mount(arc2);

        var rows = fused.Query("departments");
        Assert.Equal(4, rows.Count); // 2 from each arc
        Assert.Contains(rows, r => r.SourceArc == "budgets.arc");
        Assert.Contains(rows, r => r.SourceArc == "headcount.arc");
    }

    [Fact]
    public void Import_EmptyCsv_Throws()
    {
        Assert.Throws<ArgumentException>(() => CsvArcImporter.Import(""));
    }

    [Fact]
    public void Import_HeaderOnly_Throws()
    {
        Assert.Throws<ArgumentException>(() => CsvArcImporter.Import("name,age"));
    }
}
