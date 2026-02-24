// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Security;

/// <summary>
/// Collection of known-trusted agent public keys.
/// Used to validate signers in remote or untrusted arcs.
/// Mirrors X.509 trust store semantics: bring your own root of trust.
/// </summary>
public sealed class TrustAnchorSet
{
    private readonly Dictionary<string, byte[]> _anchors;

    /// <summary>Creates a trust anchor set from agent ID / public key pairs.</summary>
    public TrustAnchorSet(IEnumerable<(string AgentId, byte[] PublicKey)> anchors)
    {
        _anchors = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (id, key) in anchors)
            _anchors[id] = key;
    }

    private TrustAnchorSet()
    {
        _anchors = new Dictionary<string, byte[]>();
    }

    /// <summary>Whether the given agent ID is a trusted anchor.</summary>
    public bool Contains(string agentId) => _anchors.ContainsKey(agentId);

    /// <summary>Gets the public key for a trusted agent, or null if unknown.</summary>
    public byte[]? GetPublicKey(string agentId) =>
        _anchors.TryGetValue(agentId, out var key) ? key : null;

    /// <summary>Number of trust anchors in this set.</summary>
    public int Count => _anchors.Count;

    /// <summary>
    /// Creates a trust anchor set from agents registered in a trusted local arc.
    /// Reads the <c>_sharc_agents</c> table directly for agent IDs and public keys.
    /// </summary>
    public static TrustAnchorSet FromArc(ArcHandle trustedArc)
    {
        const string agentsTable = "_sharc_agents";
        var schema = trustedArc.Database.Schema;
        if (schema.GetTable(agentsTable) == null)
            return Empty;

        var anchors = new List<(string, byte[])>();
        using var reader = trustedArc.Database.CreateReader(agentsTable);
        while (reader.Read())
        {
            string agentId = reader.GetString(0);    // AgentId column
            byte[] publicKey = reader.GetBlob(2).ToArray(); // PublicKey column
            anchors.Add((agentId, publicKey));
        }
        return new TrustAnchorSet(anchors);
    }

    /// <summary>Empty set — no agents trusted.</summary>
    public static TrustAnchorSet Empty { get; } = new();
}
