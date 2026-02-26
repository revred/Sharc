// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core;

/// <summary>
/// Base interface for all Sharc extensions.
/// Extensions can register custom functions, virtual tables, or specialized storage handlers.
/// </summary>
public interface ISharcExtension
{
    /// <summary>
    /// Gets the unique name of the extension.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Called when the extension is registered.
    /// </summary>
    void OnRegister(object context);
}
