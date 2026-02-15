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
    /// Validates that the agent's ReadScope permits the requested table and columns.
    /// Throws <see cref="UnauthorizedAccessException"/> if access is denied.
    /// </summary>
    internal static void Enforce(AgentInfo agent, string tableName, string[]? columns)
    {
        ValidateAgentActive(agent);
        var scope = ScopeDescriptor.Parse(agent.ReadScope);
        EnforceScope(scope, agent.AgentId, "read", tableName, columns);
    }

    /// <summary>
    /// Validates that the agent's WriteScope permits the requested table and columns.
    /// Throws <see cref="UnauthorizedAccessException"/> if access is denied.
    /// </summary>
    internal static void EnforceWrite(AgentInfo agent, string tableName, string[]? columns)
    {
        ValidateAgentActive(agent);
        var scope = ScopeDescriptor.Parse(agent.WriteScope);
        EnforceScope(scope, agent.AgentId, "write", tableName, columns);
    }

    /// <summary>
    /// Validates that the agent's ReadScope permits access to ALL tables in a compound/CTE query.
    /// Throws <see cref="UnauthorizedAccessException"/> if access to any table is denied.
    /// </summary>
    internal static void EnforceAll(AgentInfo agent, List<(string table, string[]? columns)> targets)
    {
        ValidateAgentActive(agent);
        var scope = ScopeDescriptor.Parse(agent.ReadScope);
        foreach (var (table, columns) in targets)
        {
            EnforceScope(scope, agent.AgentId, "read", table, columns);
        }
    }

    /// <summary>
    /// Validates that the agent's validity window covers the current time.
    /// A value of 0 for ValidityStart or ValidityEnd means no restriction.
    /// </summary>
    private static void ValidateAgentActive(AgentInfo agent)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (agent.ValidityStart > 0 && now < agent.ValidityStart)
            throw new UnauthorizedAccessException(
                $"Agent '{agent.AgentId}' is not yet active (starts at {agent.ValidityStart}).");

        if (agent.ValidityEnd > 0 && now > agent.ValidityEnd)
            throw new UnauthorizedAccessException(
                $"Agent '{agent.AgentId}' has expired (ended at {agent.ValidityEnd}).");
    }

    private static void EnforceScope(
        ScopeDescriptor scope, string agentId, string accessKind,
        string tableName, string[]? columns)
    {
        if (!scope.CanReadTable(tableName))
            throw new UnauthorizedAccessException(
                $"Agent '{agentId}' does not have {accessKind} access to table '{tableName}'.");

        if (columns == null) return;

        foreach (var column in columns)
        {
            if (!scope.CanReadColumn(tableName, column))
                throw new UnauthorizedAccessException(
                    $"Agent '{agentId}' does not have {accessKind} access to column '{tableName}.{column}'.");
        }
    }
}
