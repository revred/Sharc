// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;

namespace Sharc.Repo.Schema;

/// <summary>
/// Creates the config.arc schema and seeds default configuration entries.
/// Idempotent — safe to call on an existing database.
/// </summary>
public static class ConfigSchemaBuilder
{
    private const string ConfigDdl = """
        CREATE TABLE IF NOT EXISTS config (
            id INTEGER PRIMARY KEY,
            key TEXT NOT NULL,
            value TEXT NOT NULL,
            updated_at INTEGER NOT NULL
        )
        """;

    /// <summary>Default configuration entries seeded on first init.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["channel.notes"] = "enabled",
        ["channel.annotations"] = "enabled",
        ["channel.decisions"] = "enabled",
        ["channel.context"] = "enabled",
        ["channel.conversations"] = "disabled",
        ["channel.git_history"] = "enabled",
        ["git.track"] = "false",
        ["encryption.enabled"] = "false",
    };

    /// <summary>
    /// Creates or opens a config database, creates the schema, and seeds defaults
    /// if the config table is empty. Returns the open database.
    /// </summary>
    public static SharcDatabase CreateSchema(string path)
    {
        SharcDatabase db;
        if (File.Exists(path))
            db = SharcDatabase.Open(path, new SharcOpenOptions { Writable = true });
        else
            db = SharcDatabase.Create(path);

        using var tx = db.BeginTransaction();
        tx.Execute(ConfigDdl);
        tx.Commit();

        // Seed defaults only if the table is empty
        if (CountRows(db) == 0)
            SeedDefaults(db);

        return db;
    }

    private static int CountRows(SharcDatabase db)
    {
        int count = 0;
        using var reader = db.CreateReader("config");
        while (reader.Read()) count++;
        return count;
    }

    private static void SeedDefaults(SharcDatabase db)
    {
        using var writer = SharcWriter.From(db);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var (key, value) in Defaults)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var valBytes = Encoding.UTF8.GetBytes(value);
            writer.Insert("config",
                ColumnValue.Text(2 * keyBytes.Length + 13, keyBytes),
                ColumnValue.Text(2 * valBytes.Length + 13, valBytes),
                ColumnValue.FromInt64(1, now));
        }
    }
}
