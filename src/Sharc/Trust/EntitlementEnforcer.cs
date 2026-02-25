// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;

namespace Sharc.Trust;

/// <summary>
/// Validates that an agent's ReadScope or WriteScope permits the requested table and columns.
/// </summary>
internal static class EntitlementEnforcer
{
    /// <summary>
    /// Optional hook for pluggable identity verification (e.g., verifying agent signatures
    /// against a registry or public key). If set, this is called before entitlement checks.
    /// </summary>
    public static Action<AgentInfo>? IdentityValidator { get; set; }

    /// <summary>
    /// Validates that the agent's ReadScope permits the requested table and columns.
    /// Throws <see cref="UnauthorizedAccessException"/> if access is denied.
    /// </summary>
    internal static void Enforce(AgentInfo agent, string tableName, string[]? columns)
    {
        ValidateAgent(agent);
        var scope = ScopeDescriptor.Parse(agent.ReadScope);
        EnforceScope(scope, agent.AgentId, "read", tableName, columns);
    }

    /// <summary>
    /// Validates that the agent's WriteScope permits the requested table and columns.
    /// Throws <see cref="UnauthorizedAccessException"/> if access is denied.
    /// </summary>
    internal static void EnforceWrite(AgentInfo agent, string tableName, string[]? columns)
    {
        ValidateAgent(agent);
        var scope = ScopeDescriptor.Parse(agent.WriteScope);
        EnforceScope(scope, agent.AgentId, "write", tableName, columns);
    }

    /// <summary>
    /// Validates that the agent's ReadScope permits access to ALL tables in a compound/Cote query.
    /// Throws <see cref="UnauthorizedAccessException"/> if access to any table is denied.
    /// </summary>
    internal static void EnforceAll(AgentInfo agent, List<Query.TableReference> targets)
    {
        ValidateAgent(agent);
        var scope = ScopeDescriptor.Parse(agent.ReadScope);
        foreach (var (table, columns) in targets)
        {
            EnforceScope(scope, agent.AgentId, "read", table, columns);
        }
    }

    /// <summary>
    /// Validates that the agent has schema administration rights.
    /// Throws <see cref="UnauthorizedAccessException"/> if access is denied.
    /// </summary>
    internal static void EnforceSchemaAdmin(AgentInfo agent)
    {
        ValidateAgent(agent);
        var scope = ScopeDescriptor.Parse(agent.WriteScope);
        if (!scope.IsSchemaAdmin)
             throw new UnauthorizedAccessException($"Agent '{agent.AgentId}' does not have schema administration rights.");
    }

    private static void ValidateAgent(AgentInfo agent)
    {
        IdentityValidator?.Invoke(agent);
        ValidateAgentActive(agent);
    }

    /// <summary>
    /// Validates that the agent's validity window covers the current time.
    /// A value of 0 for ValidityStart or ValidityEnd means no restriction.
    /// </summary>
    private static void ValidateAgentActive(AgentInfo agent)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (agent.ValidityStart > 0 && now < agent.ValidityStart)
            throw new UnauthorizedAccessException(
                $"Agent '{agent.AgentId}' is not yet active (starts at {agent.ValidityStart} ms). Current: {now}");

        if (agent.ValidityEnd > 0 && now > agent.ValidityEnd)
            throw new UnauthorizedAccessException(
                $"Agent '{agent.AgentId}' has expired (ended at {agent.ValidityEnd} ms). Current: {now}");
    }

    private static void EnforceScope(
        ScopeDescriptor scope, string agentId, string accessKind,
        string tableName, string[]? columns)
    {
        if (!scope.CanReadTable(tableName))
            throw new UnauthorizedAccessException(
                $"Agent '{agentId}' does not have {accessKind} access to table '{tableName}'.");

        // null columns = SELECT * (wildcard) — requires table-wide column access.
        // If the scope restricts to specific columns, wildcard must be denied.
        if (columns == null)
        {
            if (!scope.CanReadAllColumns(tableName))
                throw new UnauthorizedAccessException(
                    $"Agent '{agentId}' does not have {accessKind} access to all columns in table '{tableName}'. " +
                    $"SELECT * requires unrestricted column access; specify columns explicitly.");
            return;
        }

        foreach (var column in columns)
        {
            if (!scope.CanReadColumn(tableName, column))
                throw new UnauthorizedAccessException(
                    $"Agent '{agentId}' does not have {accessKind} access to column '{tableName}.{column}'.");
        }
    }
}
