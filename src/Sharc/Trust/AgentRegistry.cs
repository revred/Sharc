using System.Buffers.Binary;
using System.Text;
using Sharc.Core;
using Sharc.Core.Schema;
using Sharc.Core.Trust;
using Sharc.Core.Storage;

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
    /// Occurs when a security-relevant event happens (registration success/failure).
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<SecurityEventArgs>? SecurityAudit;
#pragma warning restore CS0067

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRegistry"/> class.
    /// </summary>
    /// <param name="db">The database instance.</param>
    public AgentRegistry(SharcDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Registers a new agent in the system registry.
    /// </summary>
    /// <param name="agent">The agent information to register.</param>
    /// <param name="transaction">Optional active transaction.</param>
    public void RegisterAgent(AgentInfo agent, Transaction? transaction = null)
    {
        if (!SharcSigner.Verify(GetVerificationBuffer(agent), agent.Signature, agent.PublicKey))
             throw new InvalidOperationException("Invalid signature.");

        var rootPage = SystemStore.GetRootPage(_db.Schema, AgentsTableName);
        var cols = new[]
        {
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.AgentId)),
            ColumnValue.FromInt64(0, (long)agent.Class),
            ColumnValue.Blob(0, agent.PublicKey),
            ColumnValue.FromInt64(0, (long)agent.AuthorityCeiling),
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.WriteScope)),
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.ReadScope)),
            ColumnValue.FromInt64(0, agent.ValidityStart),
            ColumnValue.FromInt64(0, agent.ValidityEnd),
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.ParentAgent)),
            ColumnValue.FromInt64(0, agent.CoSignRequired ? 1 : 0),
            ColumnValue.Blob(0, agent.Signature)
        };

        EnsureCacheLoaded();
        long rowId = ++_maxRowId;
        bool ownsTransaction = transaction == null;
        var tx = transaction ?? _db.BeginTransaction();

        try
        {
            SystemStore.InsertRecord(tx.GetShadowSource(), _db.Header.UsablePageSize, rootPage, rowId, cols);
            if (ownsTransaction) { tx.Commit(); _cache![agent.AgentId] = agent; }
            else _cache = null;
        }
        finally { if (ownsTransaction) tx.Dispose(); }
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
            // Reader index must match column order in RegisterAgent
            var info = new AgentInfo(
                reader.GetString(0), // AgentId
                (AgentClass)reader.GetInt64(1), // Class
                reader.GetBlob(2).ToArray(), // PublicKey
                (ulong)reader.GetInt64(3), // AuthorityCeiling
                reader.GetString(4), // WriteScope
                reader.GetString(5), // ReadScope
                reader.GetInt64(6), // ValidityStart
                reader.GetInt64(7), // ValidityEnd
                reader.GetString(8), // ParentAgent
                reader.GetInt64(9) != 0, // CoSignRequired
                reader.GetBlob(10).ToArray() // Signature
            );
            _cache[info.AgentId] = info;
        }
    }

    /// <summary>
    /// Constructs the verification buffer for an agent registration.
    /// </summary>
    public static byte[] GetVerificationBuffer(AgentInfo agent)
    {
        int bufferSize = Encoding.UTF8.GetByteCount(agent.AgentId) + 
                         1 + // Class (byte)
                         agent.PublicKey.Length + 
                         8 + // AuthorityCeiling
                         Encoding.UTF8.GetByteCount(agent.WriteScope) + 
                         Encoding.UTF8.GetByteCount(agent.ReadScope) + 
                         8 + // ValidityStart
                         8 + // ValidityEnd
                         Encoding.UTF8.GetByteCount(agent.ParentAgent) +
                         1;  // CoSignRequired

        byte[] verificationData = new byte[bufferSize];
        int offset = 0;
        
        offset += Encoding.UTF8.GetBytes(agent.AgentId, verificationData.AsSpan(offset));
        verificationData[offset++] = (byte)agent.Class;
        agent.PublicKey.CopyTo(verificationData.AsSpan(offset));
        offset += agent.PublicKey.Length;
        BinaryPrimitives.WriteUInt64BigEndian(verificationData.AsSpan(offset), agent.AuthorityCeiling);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(agent.WriteScope, verificationData.AsSpan(offset));
        offset += Encoding.UTF8.GetBytes(agent.ReadScope, verificationData.AsSpan(offset));
        BinaryPrimitives.WriteInt64BigEndian(verificationData.AsSpan(offset), agent.ValidityStart);
        offset += 8;
        BinaryPrimitives.WriteInt64BigEndian(verificationData.AsSpan(offset), agent.ValidityEnd);
        offset += 8;
        offset += Encoding.UTF8.GetBytes(agent.ParentAgent, verificationData.AsSpan(offset));
        verificationData[offset++] = agent.CoSignRequired ? (byte)1 : (byte)0;

        return verificationData;
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
