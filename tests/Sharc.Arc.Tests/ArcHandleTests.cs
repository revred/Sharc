// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Xunit;

namespace Sharc.Arc.Tests;

public class ArcHandleTests : IDisposable
{
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

    private string TempPath()
    {
        var p = Path.Combine(Path.GetTempPath(), $"sharc_arc_{Guid.NewGuid()}.arc");
        _tempFiles.Add(p);
        return p;
    }

    [Fact]
    public void CreateInMemory_ReturnsValidHandle()
    {
        using var handle = ArcHandle.CreateInMemory("test.arc");
        Assert.Equal("test.arc", handle.Name);
        Assert.NotNull(handle.Database);
        Assert.NotNull(handle.Ledger);
        Assert.NotNull(handle.Registry);
    }

    [Fact]
    public void OpenLocal_ExistingDatabase_OpensSuccessfully()
    {
        // Create a real database file
        var path = TempPath();
        var db = SharcDatabase.Create(path);
        db.Dispose();

        using var handle = ArcHandle.OpenLocal(path);
        Assert.Equal(Path.GetFileName(path), handle.Name);
        Assert.NotNull(handle.Uri);
        Assert.Equal("local", handle.Uri!.Value.Authority);
    }

    [Fact]
    public void FromMemory_ByteArray_RestoresCorrectly()
    {
        // Create in-memory, export, re-import
        using var original = ArcHandle.CreateInMemory("test.arc");
        var bytes = original.ExportBytes();

        using var restored = ArcHandle.FromMemory("test.arc", bytes);
        Assert.Equal("test.arc", restored.Name);
    }

    [Fact]
    public void VerifyIntegrity_EmptyLedger_ReturnsTrue()
    {
        using var handle = ArcHandle.CreateInMemory("test.arc");
        Assert.True(handle.VerifyIntegrity());
    }

    [Fact]
    public void ExportBytes_ReturnsNonEmptyArray()
    {
        using var handle = ArcHandle.CreateInMemory("test.arc");
        var bytes = handle.ExportBytes();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var handle = ArcHandle.CreateInMemory("test.arc");
        handle.Dispose();
        handle.Dispose(); // should not throw
    }
}
