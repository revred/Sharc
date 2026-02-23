// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Views;

/// <summary>
/// A named, row-producing source with an explicit materialization strategy.
/// This is the minimum contract for any data layer — a View IS a Layer.
/// </summary>
/// <remarks>
/// <para>Consumers that need to iterate rows depend on <see cref="Open"/>.
/// Consumers that need strategy information read <see cref="Strategy"/>.
/// No consumer is forced to depend on methods it doesn't use (ISP).</para>
/// <para>See <see cref="SharcView"/> for the primary implementation.</para>
/// </remarks>
public interface ILayer
{
    /// <summary>Human-readable name for this layer.</summary>
    string Name { get; }

    /// <summary>Controls how rows are produced during cursor iteration.</summary>
    MaterializationStrategy Strategy { get; }

    /// <summary>
    /// Opens a forward-only cursor over the layer's projected rows.
    /// </summary>
    /// <param name="db">The database to read from.</param>
    /// <returns>A forward-only cursor over the projected rows.</returns>
    IViewCursor Open(SharcDatabase db);
}
