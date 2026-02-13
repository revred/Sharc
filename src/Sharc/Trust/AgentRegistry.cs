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
    /// <param name="transaction">Optional active transaction. If null, a new one is created and committed.</param>
    public void RegisterAgent(AgentInfo agent, Transaction? transaction = null)
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

        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();

        try
        {
            var source = tx.GetShadowSource();
            var mutator = new BTreeMutator(source, _db.Header.UsablePageSize);
            
            mutator.Insert((uint)table.RootPage, rowId, recordBuffer);
            
            if (ownsTransaction) 
            {
                tx.Commit();
                // Update cache only on separate commit
                _cache![agent.AgentId] = agent;
            }
            else
            {
                // If external transaction, we don't know if it will commit.
                // Invalidate cache so subsequent reads force a reload from DB
                // (which will see the transaction's view or the original state).
                _cache = null;
            }
        }
        finally
        {
            if (ownsTransaction) tx.Dispose();
        }
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
}
