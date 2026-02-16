using Xunit;

namespace Sharc.Tests;

public class TransactionTests
{
    private static byte[] CreateEmptyDatabase()
    {
        var data = new byte[4096];
        // SQLite Magic
        "SQLite format 3\0"u8.CopyTo(data);
        // Page Size 4096 (offset 16, big endian)
        data[16] = 0x10;
        data[17] = 0x00;
        // Page Count 1 (offset 28)
        data[28] = 0;
        data[29] = 0;
        data[30] = 0;
        data[31] = 1;
        return data;
    }

    [Fact]
    public void Commit_PersistsChangesToUnderlyingSource()
    {
        var data = CreateEmptyDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        
        using (var tx = db.BeginTransaction())
        {
            var content = new byte[4096];
            content[0] = 0xFF;
            db.WritePage(1, content);
            tx.Commit();
        }

        Assert.Equal(0xFF, data[0]);
    }

    [Fact]
    public void Rollback_DiscardsChanges()
    {
        var data = CreateEmptyDatabase();
        var originalMagic = data[0];
        using var db = SharcDatabase.OpenMemory(data);
        
        using (var tx = db.BeginTransaction())
        {
            var content = new byte[4096];
            content[0] = 0xEE;
            db.WritePage(1, content);
            tx.Rollback();
        }

        Assert.Equal(originalMagic, data[0]);
    }

    [Fact]
    public void Reads_SeeBufferedWritesBeforeCommit()
    {
        var data = CreateEmptyDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        
        using (var tx = db.BeginTransaction())
        {
            var content = new byte[4096];
            content[100] = 0xAA;
            db.WritePage(1, content);
            
            var readSpan = db.ReadPage(1);
            Assert.Equal(0xAA, readSpan[100]);
        }
    }

    [Fact]
    public void Dispose_WithoutCommit_RollbacksChanges()
    {
        var data = CreateEmptyDatabase();
        var originalMagic = data[0];
        using var db = SharcDatabase.OpenMemory(data);
        
        using (var tx = db.BeginTransaction())
        {
            var content = new byte[4096];
            content[0] = 0xDD;
            db.WritePage(1, content);
        } // Implicit rollback

        Assert.Equal(originalMagic, data[0]);
    }

    [Fact]
    public void MultipleUpdates_ToSamePage_AreBufferedCorrectly()
    {
        var data = CreateEmptyDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        
        using (var tx = db.BeginTransaction())
        {
            var content1 = new byte[4096];
            content1[50] = 0x11;
            db.WritePage(1, content1);
            
            var content2 = new byte[4096];
            content2[50] = 0x22;
            db.WritePage(1, content2);
            
            tx.Commit();
        }

        Assert.Equal(0x22, data[50]);
    }
}
