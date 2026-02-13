using System.Collections.Concurrent;

namespace Sharc.Core;

/// <summary>
/// Manages the registration and discovery of Sharc extensions.
/// </summary>
public sealed class SharcExtensionRegistry
{
    private readonly ConcurrentDictionary<string, ISharcExtension> _extensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a new extension.
    /// </summary>
    public void Register(ISharcExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (!_extensions.TryAdd(extension.Name, extension))
        {
            throw new InvalidOperationException($"Extension '{extension.Name}' is already registered.");
        }
    }

    /// <summary>
    /// Attempts to get an extension by name.
    /// </summary>
    public bool TryGetExtension(string name, out ISharcExtension? extension)
    {
        return _extensions.TryGetValue(name, out extension);
    }

    /// <summary>
    /// Gets all registered extensions.
    /// </summary>
    public IEnumerable<ISharcExtension> GetAll() => _extensions.Values;
}
