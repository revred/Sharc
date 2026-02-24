// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Xunit;

namespace Sharc.Arc.Tests;

public class ArcResolverTests : IDisposable
{
    private readonly ArcResolver _resolver = new();
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            if (File.Exists(f)) File.Delete(f);
            if (File.Exists(f + ".journal")) File.Delete(f + ".journal");
        }
        GC.SuppressFinalize(this);
    }

    private string CreateValidArc()
    {
        var p = Path.Combine(Path.GetTempPath(), $"sharc_resolver_{Guid.NewGuid()}.arc");
        _tempFiles.Add(p);
        var db = SharcDatabase.Create(p);
        db.Dispose();
        return p;
    }

    // ── Threat: Unsupported authority in URI ────────────────────────

    [Fact]
    public void Resolve_UnsupportedAuthority_ReturnsUnsupported()
    {
        var uri = ArcUri.Parse("arc://https/drive.google.com/file");

        using var result = _resolver.Resolve(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.UnsupportedAuthority, result.Availability);
        Assert.Contains("https", result.ErrorMessage!);
    }

    [Fact]
    public void Resolve_GitAuthority_ReturnsUnsupported()
    {
        var uri = ArcUri.Parse("arc://git/github.com/org/repo/main/data.arc");

        using var result = _resolver.Resolve(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.UnsupportedAuthority, result.Availability);
    }

    // ── Threat: Invalid URI string ──────────────────────────────────

    [Fact]
    public void Resolve_InvalidUriString_ReturnsUnreachable()
    {
        using var result = _resolver.Resolve("not-a-valid-uri");

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Unreachable, result.Availability);
        Assert.Contains("Invalid", result.ErrorMessage!);
    }

    [Fact]
    public void Resolve_NullUriString_ReturnsUnreachable()
    {
        using var result = _resolver.Resolve((string)null!);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Unreachable, result.Availability);
    }

    // ── Happy paths ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_LocalUri_DelegatesToLocalLocator()
    {
        var path = CreateValidArc();
        var uri = ArcUri.FromLocalPath(path);

        using var result = _resolver.Resolve(uri, new ArcOpenOptions { ValidateOnOpen = false });

        Assert.True(result.IsAvailable);
        Assert.NotNull(result.Handle);
    }

    [Fact]
    public void Resolve_FromString_Works()
    {
        var path = CreateValidArc();
        var uriStr = $"arc://local/{path.Replace('\\', '/')}";

        using var result = _resolver.Resolve(uriStr, new ArcOpenOptions { ValidateOnOpen = false });

        Assert.True(result.IsAvailable);
    }

    [Fact]
    public void Register_CustomLocator_OverridesDefault()
    {
        var mockLocator = new MockLocator("local");
        _resolver.Register(mockLocator);

        var uri = ArcUri.FromLocalPath("anything.arc");
        using var result = _resolver.Resolve(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal("mock", result.ErrorMessage);
        Assert.True(mockLocator.WasCalled);
    }

    // ── Threat: Arc unreachable at resolve time ─────────────────────

    [Fact]
    public void Resolve_MissingFile_NeverThrows()
    {
        var uri = ArcUri.FromLocalPath("/this/does/not/exist.arc");

        // Must not throw — returns Unreachable
        using var result = _resolver.Resolve(uri);

        Assert.False(result.IsAvailable);
        Assert.Equal(ArcAvailability.Unreachable, result.Availability);
    }

    // ── Mock ────────────────────────────────────────────────────────

    private sealed class MockLocator : IArcLocator
    {
        public string Authority { get; }
        public bool WasCalled { get; private set; }

        public MockLocator(string authority) => Authority = authority;

        public ArcOpenResult TryOpen(ArcUri uri, ArcOpenOptions? options = null)
        {
            WasCalled = true;
            return ArcOpenResult.Failure(ArcAvailability.Unreachable, "mock");
        }
    }
}
