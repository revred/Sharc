using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Core.Trust;

namespace Sharc.Trust;

/// <summary>
/// Manages the local agent registry for cryptographic trust.
/// </summary>
public sealed class AgentRegistry
{
    private const string AgentsTableName = "_sharc_agents";
    private readonly SharcDatabase _db;
    private Dictionary<string, AgentInfo>? _cache;
    private long _maxRowId;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRegistry"/> class.
    /// </summary>
    /// <param name="db">The database instance.</param>
    public AgentRegistry(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Registers an agent in the local registry.
    /// </summary>
    /// <param name="agent">The agent information to register.</param>
    public void RegisterAgent(AgentInfo agent)
    {
        var table = GetTable(AgentsTableName);
        if (table == null) throw new InvalidOperationException($"System table {AgentsTableName} not found.");
        
        var columns = new[]
        {
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes(agent.AgentId)),
            ColumnValue.Blob(0, agent.PublicKey),
            ColumnValue.FromInt64(0, agent.ValidityStart),
            ColumnValue.FromInt64(0, agent.ValidityEnd),
            ColumnValue.Blob(0, agent.Signature)
        };

        int recordSize = RecordEncoder.ComputeEncodedSize(columns);
        byte[] recordBuffer = new byte[recordSize];
        RecordEncoder.EncodeRecord(columns, recordBuffer);

        EnsureCacheLoaded();
        long rowId = ++_maxRowId;

        using var tx = _db.BeginTransaction();
        var source = tx.GetShadowSource();
        
        AppendToTableBTree(source, table.RootPage, rowId, recordBuffer);
        tx.Commit();

        // Update cache
        _cache![agent.AgentId] = agent;
    }

    /// <summary>
    /// Retrieves an agent's information from the registry.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>The agent information, or null if not found.</returns>
    public AgentInfo? GetAgent(string agentId)
    {
        if (GetTable(AgentsTableName) == null) return null;

        EnsureCacheLoaded();
        return _cache!.TryGetValue(agentId, out var agent) ? agent : null;
    }

    private void EnsureCacheLoaded()
    {
        if (_cache != null) return;

        _cache = new Dictionary<string, AgentInfo>(StringComparer.Ordinal);
        _maxRowId = 0;

        if (GetTable(AgentsTableName) == null) return;

        using var reader = _db.CreateReader(AgentsTableName);
        while (reader.Read())
        {
            _maxRowId++;
            var info = new AgentInfo(
                reader.GetString(0),
                reader.GetBlob(1).ToArray(),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetBlob(4).ToArray()
            );
            _cache[info.AgentId] = info;
        }
    }

    private TableInfo? GetTable(string name)
    {
        foreach (var t in _db.Schema.Tables)
        {
            if (t.Name == name) return t;
        }
        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Generic IWritablePageSource is required for flexibility across sync providers.")]
    private void AppendToTableBTree(IWritablePageSource source, int rootPage, long rowId, byte[] payload)
    {
        uint pageNum = (uint)rootPage;
        byte[] pageData = new byte[source.PageSize];
        source.ReadPage(pageNum, pageData);

        var header = BTreePageHeader.Parse(pageData.AsSpan(pageNum == 1 ? 100 : 0));
        int headerOffset = (pageNum == 1) ? SQLiteLayout.DatabaseHeaderSize : 0;
        
        int cellSize = CellBuilder.ComputeTableLeafCellSize(rowId, payload.Length, _db.Header.UsablePageSize);
        ushort newContentOffset = (ushort)(header.CellContentOffset - cellSize);
        
        CellBuilder.BuildTableLeafCell(rowId, payload, pageData.AsSpan(newContentOffset), _db.Header.UsablePageSize);

        int cellPointerOffset = headerOffset + header.HeaderSize;
        int newPointerPos = cellPointerOffset + (header.CellCount * 2);
        BinaryPrimitives.WriteUInt16BigEndian(pageData.AsSpan(newPointerPos), newContentOffset);

        var updatedHeader = new BTreePageHeader(
            header.PageType,
            header.FirstFreeblockOffset,
            (ushort)(header.CellCount + 1),
            newContentOffset,
            header.FragmentedFreeBytes,
            header.RightChildPage);

        BTreePageHeader.Write(pageData.AsSpan(headerOffset), updatedHeader);
        source.WritePage(pageNum, pageData);
    }
}
