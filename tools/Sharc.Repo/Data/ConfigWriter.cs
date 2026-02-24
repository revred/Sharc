// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;

namespace Sharc.Repo.Data;

/// <summary>
/// Reads and writes config entries in config.arc.
/// </summary>
public sealed class ConfigWriter : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;
    private bool _disposed;

    public ConfigWriter(SharcDatabase db)
    {
        _db = db;
        _writer = SharcWriter.From(db);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Sets a config key. If the key already exists, updates it; otherwise inserts.
    /// Returns the rowid of the affected row.
    /// </summary>
    public long Set(string key, string value)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Scan for existing key
        using var reader = _db.CreateReader("config");
        while (reader.Read())
        {
            var existingKey = reader.GetString(0); // key is ordinal 0 (id is rowid alias)
            if (string.Equals(existingKey, key, StringComparison.Ordinal))
            {
                long rowId = reader.RowId;
                var keyBytes = Encoding.UTF8.GetBytes(key);
                var valBytes = Encoding.UTF8.GetBytes(value);
                _writer.Update("config", rowId,
                    ColumnValue.Text(2 * keyBytes.Length + 13, keyBytes),
                    ColumnValue.Text(2 * valBytes.Length + 13, valBytes),
                    ColumnValue.FromInt64(1, now));
                return rowId;
            }
        }

        // Insert new
        var kBytes = Encoding.UTF8.GetBytes(key);
        var vBytes = Encoding.UTF8.GetBytes(value);
        return _writer.Insert("config",
            ColumnValue.Text(2 * kBytes.Length + 13, kBytes),
            ColumnValue.Text(2 * vBytes.Length + 13, vBytes),
            ColumnValue.FromInt64(1, now));
    }

    /// <summary>Gets a config value by key, or null if not found.</summary>
    public string? Get(string key)
    {
        using var reader = _db.CreateReader("config");
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), key, StringComparison.Ordinal))
                return reader.GetString(1);
        }
        return null;
    }

    /// <summary>Returns all config entries as key-value pairs.</summary>
    public IReadOnlyList<(string Key, string Value)> GetAll()
    {
        var result = new List<(string, string)>();
        using var reader = _db.CreateReader("config");
        while (reader.Read())
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }
        return result;
    }
}
