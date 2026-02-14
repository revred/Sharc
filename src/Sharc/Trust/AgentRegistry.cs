using System.Buffers.Binary;
using System.Text;
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
    /// Occurs when a security-relevant event happens (registration success/failure).
    /// </summary>
    public event EventHandler<SecurityEventArgs>? SecurityAudit;

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
        byte[] verificationData = GetVerificationBuffer(agent);

            if (!SharcSigner.Verify(verificationData, agent.Signature, agent.PublicKey))
        {
             SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.RegistrationFailed, agent.AgentId, "Invalid self-attestation signature."));
             throw new InvalidOperationException("Agent registration failed: Invalid self-attestation signature.");
        }

        var table = GetTable(AgentsTableName);
        if (table == null) throw new InvalidOperationException($"System table {AgentsTableName} not found.");
        
        var columns = new[]
        {
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.AgentId)),
            ColumnValue.FromInt64(0, (long)agent.Class),            // 1: Class
            ColumnValue.Blob(0, agent.PublicKey),                   // 2: PublicKey
            ColumnValue.FromInt64(0, (long)agent.AuthorityCeiling), // 3: AuthorityCeiling
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.WriteScope)), // 4: WriteScope
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.ReadScope)),  // 5: ReadScope
            ColumnValue.FromInt64(0, agent.ValidityStart),          // 6: ValidityStart
            ColumnValue.FromInt64(0, agent.ValidityEnd),            // 7: ValidityEnd
            ColumnValue.Text(0, Encoding.UTF8.GetBytes(agent.ParentAgent)), // 8: ParentAgent
            ColumnValue.FromInt64(0, agent.CoSignRequired ? 1 : 0), // 9: CoSignRequired
            ColumnValue.Blob(0, agent.Signature)                    // 10: Signature
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
            
            SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.RegistrationSuccess, agent.AgentId, "Agent registered successfully."));
        }
        catch (Exception ex)
        {
             // If we caught an exception here (e.g. duplicate key from BTree), log it as failed
             SecurityAudit?.Invoke(this, new SecurityEventArgs(SecurityEventType.RegistrationFailed, agent.AgentId, $"Registration failed: {ex.Message}"));
             throw;
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
