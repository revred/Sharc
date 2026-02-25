// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Arc.Tests;

public sealed class GoogleSheetsImporterTests
{
    [Fact]
    public void ExtractId_FullEditUrl()
    {
        string id = GoogleSheetsImporter.ExtractSpreadsheetId(
            "https://docs.google.com/spreadsheets/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms/edit#gid=0");
        Assert.Equal("1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms", id);
    }

    [Fact]
    public void ExtractId_TrailingSlash()
    {
        string id = GoogleSheetsImporter.ExtractSpreadsheetId(
            "https://docs.google.com/spreadsheets/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms/");
        Assert.Equal("1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms", id);
    }

    [Fact]
    public void ExtractId_BareId()
    {
        string id = GoogleSheetsImporter.ExtractSpreadsheetId("1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms");
        Assert.Equal("1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms", id);
    }

    [Fact]
    public void ExtractId_QueryString()
    {
        string id = GoogleSheetsImporter.ExtractSpreadsheetId(
            "https://docs.google.com/spreadsheets/d/ABC123?usp=sharing");
        Assert.Equal("ABC123", id);
    }

    [Fact]
    public void ExtractId_InvalidUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GoogleSheetsImporter.ExtractSpreadsheetId("https://example.com/something"));
    }

    [Fact]
    public void BuildCsvUrl_Basic()
    {
        string url = GoogleSheetsImporter.BuildCsvExportUrl("ABC123");
        Assert.Equal("https://docs.google.com/spreadsheets/d/ABC123/export?format=csv", url);
    }

    [Fact]
    public void BuildCsvUrl_WithGid()
    {
        string url = GoogleSheetsImporter.BuildCsvExportUrl("ABC123", "12345");
        Assert.Contains("gid=12345", url);
    }
}
