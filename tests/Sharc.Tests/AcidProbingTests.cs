using Sharc.Core;
using Sharc.Core.IO;
using Sharc.Core.Format;
using Xunit;

namespace Sharc.Tests;

public class AcidProbingTests
{
    private static readonly uint[] SinglePageArray = [5u];

    private static byte[] CreateEmptyDatabase(int pageSize = 4096, int pageCount = 1)
    {
        var data = new byte[pageSize * pageCount];
        var header = new DatabaseHeader(
            pageSize: pageSize,
            writeVersion: 1,
            readVersion: 1,
            reservedBytesPerPage: 0,
            changeCounter: 0,
            pageCount: pageCount,
            firstFreelistPage: 0,
            freelistPageCount: 0,
            schemaCookie: 0,
            schemaFormat: 4,
            textEncoding: 1, // UTF-8
            userVersion: 0,
            applicationId: 0,
            sqliteVersionNumber: 3042000
        );
        DatabaseHeader.Write(data, header);
        return data;
    }

    [Fact]
    public void Isolation_CrossInstance_ReaderSeesCommittedOnly()
    {
        var data = CreateEmptyDatabase(pageCount: 10);
        using var db1 = SharcDatabase.OpenMemory(data);
        
        using (var tx = db1.BeginTransaction())
        {
            // Access page 10 instead of 1 to avoid touching the header
            var content10 = new byte[4096];
            content10[0] = 0xAA;
            db1.WritePage(10, content10);

            // Access page 5 as well
            var content5 = new byte[4096];
            content5[0] = 0x55;
            db1.WritePage(5, content5);

            // VERIFY HEADER IN DATA IS STILL OK
            var headerInData = DatabaseHeader.Parse(data);
            Assert.Equal(10, headerInData.PageCount);
            
            // VERIFY PAGE 10 IN DATA IS STILL ZERO
            Assert.Equal(0, data[(10 - 1) * 4096]);

            // Create second instance on the SAME data
            using var db2 = SharcDatabase.OpenMemory(data);
            
            // Should be original data
            Assert.Equal(0, db2.ReadPage(10)[0]);
            Assert.Equal(0, db2.ReadPage(5)[0]);
            
            tx.Commit();
        }

        // After commit, both see it
        using var db3 = SharcDatabase.OpenMemory(data);
        Assert.Equal(0xAA, db3.ReadPage(10)[0]);
        Assert.Equal(0x55, db3.ReadPage(5)[0]);
    }

    [Fact]
    public void BTree_StructuralIntegrity_AcrossTransaction()
    {
        // This test probes if B-Tree structures survive staging.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, CreateEmptyDatabase(pageCount: 10));
            
            using (var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true }))
            {
                using (var tx = db.BeginTransaction())
                {
                    for (uint i = 2; i <= 10; i++)
                    {
                        var page = new byte[4096];
                        page[0] = (byte)i; // Marker
                        db.WritePage(i, page);
                    }
                    tx.Commit();
                }
            }

            // Verify persistence
            var data = File.ReadAllBytes(tempFile);
            for (uint i = 2; i <= 10; i++)
            {
                Assert.Equal((byte)i, data[(i - 1) * 4096]);
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Atomicity_RollbackRestoresOriginalState()
    {
        var data = CreateEmptyDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        
        using (var tx = db.BeginTransaction())
        {
            var content1 = new byte[4096];
            DatabaseHeader.MagicBytes.CopyTo(content1);
            content1[100] = 0xEE;
            db.WritePage(1, content1);
            
            tx.Rollback();
        }

        var readSpan = db.ReadPage(1);
        Assert.Equal(0, readSpan[100]); // Should be back to original
    }

    [Fact]
    public void Durability_FileWritePersistence()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, CreateEmptyDatabase());
            
            using (var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true }))
            {
                using var tx = db.BeginTransaction();
                var content = new byte[4096];
                DatabaseHeader.MagicBytes.CopyTo(content);
                content[500] = 0x55;
                db.WritePage(1, content);
                tx.Commit();
            }

            // Read back via raw IO
            var bytes = File.ReadAllBytes(tempFile);
            Assert.Equal(0x55, bytes[500]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Atomicity_CrashRecovery_RestoresConsistency()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, CreateEmptyDatabase(pageCount: 10));
            
            // 1. Start a transaction and write something
            using (var db = SharcDatabase.Open(tempFile, new SharcOpenOptions { Writable = true }))
            {
                // 1a. Create the journal FIRST, capturing the ORIGINAL clean page 5
                var journalPath = tempFile + ".journal";
                RollbackJournal.CreateJournal(journalPath, db.PageSource, SinglePageArray);

                // 1b. Now dirty the page (simulating an in-flight transaction)
                using var tx = db.BeginTransaction();
                var content = new byte[4096];
                content[0] = 0x99;
                db.WritePage(5, content);
                
                // We DON'T commit here. We simulate a crash by leaving the journal behind.
            } // Close DB before we manually corrupt

            // Now "partially" write to the database file (the corruption)
            using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.Seek(4 * 4096, SeekOrigin.Begin); // Page 5
                fs.WriteByte(0x99); 
                fs.Flush();
            }

            // Verify it IS corrupted right now
            var corruptedBytes = File.ReadAllBytes(tempFile);
            Assert.Equal(0x99, corruptedBytes[4 * 4096]);

            // 2. OPEN the database. It should see the .journal and recovery.
            using (var db2 = SharcDatabase.Open(tempFile))
            {
                var recoveredContent = db2.ReadPage(5);
                Assert.Equal(0, recoveredContent[0]); // Should be restored to 0!
            }

            // Verify journal is gone
            Assert.False(File.Exists(tempFile + ".journal"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempFile + ".journal")) File.Delete(tempFile + ".journal");
        }
    }

    [Fact]
    public void Probing_PartialCommit_CorruptionVerification()
    {
        var data = CreateEmptyDatabase(pageCount: 2);
        
        var baseSource = new MemoryPageSource(data);
        var failingSource = new FailingPageSource(baseSource, failAtWriteCount: 2);
        
        var dirtyPages = new Dictionary<uint, byte[]>();
        var p1 = new byte[4096]; p1[500] = 0x11;
        var p2 = new byte[4096]; p2[500] = 0x22;
        dirtyPages[1] = p1;
        dirtyPages[2] = p2;

        try
        {
            foreach (var (pageNumber, content) in dirtyPages)
            {
                failingSource.WritePage(pageNumber, content);
            }
        }
        catch (IOException) { }

        // Verification: Page 1 was written, but Page 2 was NOT.
        var page1 = baseSource.GetPage(1);
        Assert.Equal(0x11, page1[500]);
        
        var page2 = baseSource.GetPage(2);
        Assert.Equal(0, page2[500]);
    }

    private sealed class FailingPageSource : IWritablePageSource
    {
        private readonly IWritablePageSource _inner;
        private readonly int _failAtWriteCount;
        private int _writeCount;

        public FailingPageSource(IWritablePageSource inner, int failAtWriteCount)
        {
            _inner = inner;
            _failAtWriteCount = failAtWriteCount;
        }

        public int PageSize => _inner.PageSize;
        public int PageCount => _inner.PageCount;

        public ReadOnlySpan<byte> GetPage(uint pageNumber) => _inner.GetPage(pageNumber);
        public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber) => _inner.GetPageMemory(pageNumber);
        public int ReadPage(uint pageNumber, Span<byte> destination) => _inner.ReadPage(pageNumber, destination);

        public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
        {
            _writeCount++;
            if (_writeCount >= _failAtWriteCount)
                throw new IOException("Simulated I/O failure during commit.");
            _inner.WritePage(pageNumber, source);
        }

        public void Flush() => _inner.Flush();
        public void Invalidate(uint pageNumber) => _inner.Invalidate(pageNumber);
        public void Dispose() => _inner.Dispose();
    }
}
