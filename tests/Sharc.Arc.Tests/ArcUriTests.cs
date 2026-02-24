// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Xunit;

namespace Sharc.Arc.Tests;

public class ArcUriTests
{
    // ── TryParse: valid URIs ────────────────────────────────────────

    [Fact]
    public void TryParse_LocalAbsolutePath_ReturnsCorrectComponents()
    {
        Assert.True(ArcUri.TryParse("arc://local/C:/Data/telemetry.arc", out var uri));
        Assert.Equal("local", uri.Authority);
        Assert.Equal("C:/Data/telemetry.arc", uri.Path);
        Assert.Null(uri.Table);
        Assert.Equal(-1, uri.RowId);
        Assert.False(uri.HasRowReference);
    }

    [Fact]
    public void TryParse_LocalRelativePath_ReturnsCorrectComponents()
    {
        Assert.True(ArcUri.TryParse("arc://local/./arcs/telemetry.arc", out var uri));
        Assert.Equal("local", uri.Authority);
        Assert.Equal("./arcs/telemetry.arc", uri.Path);
    }

    [Fact]
    public void TryParse_UncPath_ReturnsCorrectComponents()
    {
        Assert.True(ArcUri.TryParse("arc://local//server/share/data.arc", out var uri));
        Assert.Equal("local", uri.Authority);
        Assert.Equal("/server/share/data.arc", uri.Path);
    }

    [Fact]
    public void TryParse_HttpsAuthority_ParsesCorrectly()
    {
        Assert.True(ArcUri.TryParse("arc://https/drive.google.com/file/d/abc123", out var uri));
        Assert.Equal("https", uri.Authority);
        Assert.Equal("drive.google.com/file/d/abc123", uri.Path);
    }

    [Fact]
    public void TryParse_GitAuthority_ParsesCorrectly()
    {
        Assert.True(ArcUri.TryParse("arc://git/github.com/org/repo/main/data.arc", out var uri));
        Assert.Equal("git", uri.Authority);
        Assert.Equal("github.com/org/repo/main/data.arc", uri.Path);
    }

    [Fact]
    public void TryParse_WithTableFragment_ExtractsTable()
    {
        Assert.True(ArcUri.TryParse("arc://local/data.arc#events", out var uri));
        Assert.Equal("events", uri.Table);
        Assert.Equal(-1, uri.RowId);
        Assert.True(uri.HasRowReference);
    }

    [Fact]
    public void TryParse_WithTableAndRowId_ExtractsBoth()
    {
        Assert.True(ArcUri.TryParse("arc://local/data.arc#events/42", out var uri));
        Assert.Equal("events", uri.Table);
        Assert.Equal(42L, uri.RowId);
        Assert.True(uri.HasRowReference);
    }

    [Fact]
    public void TryParse_AuthorityCaseInsensitive()
    {
        Assert.True(ArcUri.TryParse("arc://LOCAL/data.arc", out var uri));
        Assert.Equal("local", uri.Authority);
    }

    // ── TryParse: invalid URIs ──────────────────────────────────────

    [Fact]
    public void TryParse_InvalidFormat_ReturnsFalse()
    {
        Assert.False(ArcUri.TryParse("not-a-uri", out _));
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        Assert.False(ArcUri.TryParse("", out _));
    }

    [Fact]
    public void TryParse_NullString_ReturnsFalse()
    {
        Assert.False(ArcUri.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_WrongScheme_ReturnsFalse()
    {
        Assert.False(ArcUri.TryParse("http://local/data.arc", out _));
    }

    [Fact]
    public void TryParse_NoAuthority_ReturnsFalse()
    {
        Assert.False(ArcUri.TryParse("arc:///data.arc", out _));
    }

    [Fact]
    public void TryParse_NoPath_ReturnsFalse()
    {
        Assert.False(ArcUri.TryParse("arc://local/", out _));
    }

    // ── Parse ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_InvalidFormat_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => ArcUri.Parse("garbage"));
    }

    [Fact]
    public void Parse_ValidUri_ReturnsArcUri()
    {
        var uri = ArcUri.Parse("arc://local/test.arc");
        Assert.Equal("local", uri.Authority);
        Assert.Equal("test.arc", uri.Path);
    }

    // ── FromLocalPath ───────────────────────────────────────────────

    [Fact]
    public void FromLocalPath_AbsolutePath_CreatesLocalUri()
    {
        var uri = ArcUri.FromLocalPath("/tmp/test.arc");
        Assert.Equal("local", uri.Authority);
        Assert.Equal("/tmp/test.arc", uri.Path);
    }

    [Fact]
    public void FromLocalPath_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ArcUri.FromLocalPath(""));
    }

    // ── Equality & ToString ─────────────────────────────────────────

    [Fact]
    public void Equals_SameUri_ReturnsTrue()
    {
        var a = ArcUri.Parse("arc://local/test.arc#events/42");
        var b = ArcUri.Parse("arc://local/test.arc#events/42");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_DifferentUri_ReturnsFalse()
    {
        var a = ArcUri.Parse("arc://local/a.arc");
        var b = ArcUri.Parse("arc://local/b.arc");
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void ToString_RoundTrips_WithParse()
    {
        var original = "arc://local/data.arc#events/42";
        var uri = ArcUri.Parse(original);
        Assert.Equal(original, uri.ToString());
    }

    [Fact]
    public void ToString_NoFragment_OmitsHash()
    {
        var uri = ArcUri.Parse("arc://local/data.arc");
        Assert.Equal("arc://local/data.arc", uri.ToString());
    }

    [Fact]
    public void GetHashCode_EqualUris_SameHash()
    {
        var a = ArcUri.Parse("arc://local/test.arc");
        var b = ArcUri.Parse("arc://local/test.arc");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
