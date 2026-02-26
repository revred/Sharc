// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

/// <summary>
/// A committed row-level mutation emitted after a successful transaction commit.
/// </summary>
internal readonly record struct TransactionRowMutation(string TableName, long RowId);

/// <summary>
/// Observer contract for receiving committed row-mutation notifications.
/// </summary>
internal interface ITransactionCommitObserver
{
    /// <summary>
    /// Called after a transaction has been durably committed.
    /// </summary>
    void OnTransactionCommitted(SharcDatabase db, IReadOnlyList<TransactionRowMutation> mutations);
}
