// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Tests.Trust;

public static class TrustTestFixtures
{
    public static byte[] CreateTrustDatabase(int pageSize = 4096)
    {
        var data = new byte[pageSize * 4]; // 4 pages: 1 schema, 1 ledger, 1 agents, 1 scores
        var dbHeader = new DatabaseHeader(
            pageSize: pageSize,
            writeVersion: 1,
            readVersion: 1,
            reservedBytesPerPage: 0,
            changeCounter: 1,
            pageCount: 4,
            firstFreelistPage: 0,
            freelistPageCount: 0,
            schemaCookie: 1,
            schemaFormat: 4,
            textEncoding: 1,
            userVersion: 0,
            applicationId: 0,
            sqliteVersionNumber: 3042000
        );
        DatabaseHeader.Write(data, dbHeader);

        // -- Page 1: Schema Leaf --
        var schemaHeader = new BTreePageHeader(BTreePageType.LeafTable, 0, 3, (ushort)pageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(100), schemaHeader);

        // 1. _sharc_ledger
        var ledgerCols = new[]
        {
            ColumnValue.Text(0, Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.Text(2, Encoding.UTF8.GetBytes("_sharc_ledger")),
            ColumnValue.FromInt64(3, 2),
            ColumnValue.Text(4, Encoding.UTF8.GetBytes("CREATE TABLE _sharc_ledger (SequenceNumber INTEGER PRIMARY KEY, Timestamp INTEGER, AgentId TEXT, Payload BLOB, PayloadHash BLOB, PreviousHash BLOB, Signature BLOB)"))
        };

        // 2. _sharc_agents
        var agentsCols = new[]
        {
            ColumnValue.Text(0, Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, Encoding.UTF8.GetBytes("_sharc_agents")),
            ColumnValue.Text(2, Encoding.UTF8.GetBytes("_sharc_agents")),
            ColumnValue.FromInt64(3, 3),
            ColumnValue.Text(4, Encoding.UTF8.GetBytes("CREATE TABLE _sharc_agents (AgentId TEXT PRIMARY KEY, Class INTEGER, PublicKey BLOB, AuthorityCeiling INTEGER, WriteScope TEXT, ReadScope TEXT, ValidityStart INTEGER, ValidityEnd INTEGER, ParentAgent TEXT, CoSignRequired INTEGER, Signature BLOB)"))
        };

        // 3. _sharc_scores
        var scoresCols = new[]
        {
            ColumnValue.Text(0, Encoding.UTF8.GetBytes("table")),
            ColumnValue.Text(1, Encoding.UTF8.GetBytes("_sharc_scores")),
            ColumnValue.Text(2, Encoding.UTF8.GetBytes("_sharc_scores")),
            ColumnValue.FromInt64(3, 4),
            ColumnValue.Text(4, Encoding.UTF8.GetBytes("CREATE TABLE _sharc_scores (AgentId TEXT PRIMARY KEY, Score REAL, Confidence REAL, LastUpdated INTEGER, RatingCount INTEGER, Alpha REAL, Beta REAL)"))
        };

        int r1Size = RecordEncoder.ComputeEncodedSize(ledgerCols);
        byte[] r1 = new byte[r1Size];
        RecordEncoder.EncodeRecord(ledgerCols, r1);

        int r2Size = RecordEncoder.ComputeEncodedSize(agentsCols);
        byte[] r2 = new byte[r2Size];
        RecordEncoder.EncodeRecord(agentsCols, r2);

        int r3Size = RecordEncoder.ComputeEncodedSize(scoresCols);
        byte[] r3 = new byte[r3Size];
        RecordEncoder.EncodeRecord(scoresCols, r3);

        Span<byte> cell1 = stackalloc byte[r1Size + 10];
        int l1 = CellBuilder.BuildTableLeafCell(1, r1, cell1, pageSize);
        ushort o1 = (ushort)(pageSize - l1);
        cell1[..l1].CopyTo(data.AsSpan(o1));

        Span<byte> cell2 = stackalloc byte[r2Size + 10];
        int l2 = CellBuilder.BuildTableLeafCell(2, r2, cell2, pageSize);
        ushort o2 = (ushort)(o1 - l2);
        cell2[..l2].CopyTo(data.AsSpan(o2));

        Span<byte> cell3 = stackalloc byte[r3Size + 10];
        int l3 = CellBuilder.BuildTableLeafCell(3, r3, cell3, pageSize);
        ushort o3 = (ushort)(o2 - l3);
        cell3[..l3].CopyTo(data.AsSpan(o3));

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(108), o1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(110), o2);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(112), o3);

        // -- Page 2+3+4: Empty Leaves --
        BTreePageHeader.Write(data.AsSpan(pageSize), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));
        BTreePageHeader.Write(data.AsSpan(pageSize * 2), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));
        BTreePageHeader.Write(data.AsSpan(pageSize * 3), new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0));

        return data;
    }

    public static AgentInfo CreateValidAgent(ISharcSigner signer, long start = 0, long end = 0, ulong authorityCeiling = ulong.MaxValue)
    {
        var pub = signer.GetPublicKey();
        var wScope = "*";
        var rScope = "*";
        var parent = "";
        var cosign = false;

        int bufferSize = Encoding.UTF8.GetByteCount(signer.AgentId) + 1 + pub.Length + 8 + 
                         Encoding.UTF8.GetByteCount(wScope) + Encoding.UTF8.GetByteCount(rScope) + 
                         8 + 8 + Encoding.UTF8.GetByteCount(parent) + 1;
                         
        byte[] data = new byte[bufferSize];
        int offset = 0;
        offset += Encoding.UTF8.GetBytes(signer.AgentId, data.AsSpan(offset));
        data[offset++] = (byte)AgentClass.User;
        pub.CopyTo(data.AsSpan(offset));
        offset += pub.Length;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset), authorityCeiling);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(wScope, data.AsSpan(offset));
        offset += Encoding.UTF8.GetBytes(rScope, data.AsSpan(offset));
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), start);
        offset += 8;
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(offset), end);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(parent, data.AsSpan(offset));
        data[offset++] = cosign ? (byte)1 : (byte)0;
        
        var sig = signer.Sign(data);
        
        return new AgentInfo(signer.AgentId, AgentClass.User, pub, authorityCeiling, wScope, rScope, start, end, parent, cosign, sig);
    }
}
