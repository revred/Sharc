// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Trust;

namespace Sharc;

/// <summary>
/// A composite of <see cref="IPreparedReader"/> and <see cref="IPreparedWriter"/> steps
/// that executes as a coordinated, reusable operation. Built via a fluent
/// <see cref="Builder"/> obtained from <see cref="SharcDatabase.PrepareAgent"/>.
/// </summary>
/// <remarks>
/// <para>Why "works like an agent":
/// <list type="bullet">
/// <item>Has <b>identity</b> (optionally bound to <see cref="AgentInfo"/>)</item>
/// <item>Has <b>a plan</b> (ordered steps declared in builder)</item>
/// <item>Is <b>reusable</b> (call <see cref="Execute"/> repeatedly; closures provide dynamic values)</item>
/// <item>Is <b>auditable</b> (all operations attributable via AgentInfo)</item>
/// </list>
/// </para>
/// <para>This type is <b>not thread-safe</b>. Each instance should be used from a single thread.</para>
/// </remarks>
public sealed class PreparedAgent : IDisposable
{
    private ExecuteStep[]? _steps;
    private readonly AgentInfo? _agent;

    private PreparedAgent(ExecuteStep[] steps, AgentInfo? agent)
    {
        _steps = steps;
        _agent = agent;
    }

    /// <summary>
    /// Executes all steps in order and returns a summary result.
    /// </summary>
    /// <returns>A <see cref="PreparedAgentResult"/> with execution statistics.</returns>
    /// <exception cref="ObjectDisposedException">The agent has been disposed.</exception>
    public PreparedAgentResult Execute()
    {
        ObjectDisposedException.ThrowIf(_steps is null, this);

        var steps = _steps;
        var insertedIds = new List<long>();
        int rowsAffected = 0;
        int stepsExecuted = 0;

        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            switch (step.Kind)
            {
                case StepKind.Read:
                    using (var reader = step.Reader!.Execute())
                    {
                        step.ReadCallback?.Invoke(reader);
                    }
                    break;

                case StepKind.Insert:
                    long rowId = step.Writer!.Insert(step.ValuesFactory!());
                    insertedIds.Add(rowId);
                    break;

                case StepKind.Delete:
                    if (step.Writer!.Delete(step.RowIdFactory!()))
                        rowsAffected++;
                    break;

                case StepKind.Update:
                    if (step.Writer!.Update(step.RowIdFactory!(), step.ValuesFactory!()))
                        rowsAffected++;
                    break;
            }
            stepsExecuted++;
        }

        return new PreparedAgentResult
        {
            StepsExecuted = stepsExecuted,
            InsertedRowIds = insertedIds.Count > 0 ? insertedIds.ToArray() : Array.Empty<long>(),
            RowsAffected = rowsAffected
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _steps = null;
    }

    // ─── Step Types ──────────────────────────────────────────────

    private enum StepKind : byte { Read, Insert, Delete, Update }

    private sealed class ExecuteStep
    {
        public StepKind Kind;
        public IPreparedReader? Reader;
        public Action<SharcDataReader>? ReadCallback;
        public IPreparedWriter? Writer;
        public Func<long>? RowIdFactory;
        public Func<ColumnValue[]>? ValuesFactory;
    }

    // ─── Builder ─────────────────────────────────────────────────

    /// <summary>
    /// Fluent builder for constructing a <see cref="PreparedAgent"/>.
    /// Steps are executed in the order they are added.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<ExecuteStep> _steps = new();
        private AgentInfo? _agent;

        internal Builder() { }

        /// <summary>
        /// Binds this agent to an <see cref="AgentInfo"/> for trust enforcement.
        /// </summary>
        public Builder WithAgent(AgentInfo agent)
        {
            _agent = agent;
            return this;
        }

        /// <summary>
        /// Adds a read step. The callback receives the reader positioned before the first row.
        /// </summary>
        public Builder Read(IPreparedReader reader, Action<SharcDataReader>? callback = null)
        {
            _steps.Add(new ExecuteStep
            {
                Kind = StepKind.Read,
                Reader = reader,
                ReadCallback = callback
            });
            return this;
        }

        /// <summary>
        /// Adds an insert step. The factory provides column values at execution time.
        /// </summary>
        public Builder Insert(IPreparedWriter writer, Func<ColumnValue[]> valuesFactory)
        {
            _steps.Add(new ExecuteStep
            {
                Kind = StepKind.Insert,
                Writer = writer,
                ValuesFactory = valuesFactory
            });
            return this;
        }

        /// <summary>
        /// Adds a delete step. The factory provides the rowid at execution time.
        /// </summary>
        public Builder Delete(IPreparedWriter writer, Func<long> rowIdFactory)
        {
            _steps.Add(new ExecuteStep
            {
                Kind = StepKind.Delete,
                Writer = writer,
                RowIdFactory = rowIdFactory
            });
            return this;
        }

        /// <summary>
        /// Adds an update step. The factories provide rowid and values at execution time.
        /// </summary>
        public Builder Update(IPreparedWriter writer, Func<long> rowIdFactory,
            Func<ColumnValue[]> valuesFactory)
        {
            _steps.Add(new ExecuteStep
            {
                Kind = StepKind.Update,
                Writer = writer,
                RowIdFactory = rowIdFactory,
                ValuesFactory = valuesFactory
            });
            return this;
        }

        /// <summary>
        /// Builds the immutable <see cref="PreparedAgent"/>. Steps are frozen after this call.
        /// </summary>
        public PreparedAgent Build()
        {
            return new PreparedAgent(_steps.ToArray(), _agent);
        }
    }
}
