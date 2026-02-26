// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

/// <summary>
/// Thread-safe registry for transaction commit observers.
/// Keeps observer bookkeeping out of <see cref="SharcDatabase"/>.
/// </summary>
internal sealed class TransactionCommitObserverRegistry
{
    private readonly object _gate = new();
    private readonly List<ITransactionCommitObserver> _observers = new();

    internal void Register(ITransactionCommitObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            if (!_observers.Contains(observer))
                _observers.Add(observer);
        }
    }

    internal void Unregister(ITransactionCommitObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    internal void NotifyCommitted(SharcDatabase db, IReadOnlyList<TransactionRowMutation> mutations)
    {
        if (mutations.Count == 0)
            return;

        ITransactionCommitObserver[] observers;
        lock (_gate)
        {
            if (_observers.Count == 0)
                return;
            observers = _observers.ToArray();
        }

        for (int i = 0; i < observers.Length; i++)
        {
            try
            {
                observers[i].OnTransactionCommitted(db, mutations);
            }
            catch
            {
                // Best-effort observer notifications must not break committed transactions.
            }
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _observers.Clear();
        }
    }
}
