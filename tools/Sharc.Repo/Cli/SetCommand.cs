// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;
using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Upserts a key-value context entry in workspace.arc.
/// </summary>
public static class SetCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help") { PrintHelp(); return 0; }

        string? key = null;
        string? value = null;
        string? author = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--author" when i + 1 < args.Length: author = args[++i]; break;
                default:
                    if (!args[i].StartsWith("--"))
                    {
                        if (key == null) key = args[i];
                        else if (value == null) value = args[i];
                    }
                    break;
            }
        }

        if (key == null || value == null)
        {
            Console.Error.WriteLine("Usage: sharc set <key> <value> [--author a]");
            return 1;
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        if (!ChannelGuard.IsEnabled("context"))
            return 1;

        var wsPath = Path.Combine(sharcDir, RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Scan-then-update pattern for context table
        long? existingRowId = null;
        using (var reader = db.CreateReader("context"))
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(0), key, StringComparison.Ordinal))
                {
                    existingRowId = reader.RowId;
                    break;
                }
            }
        }

        if (existingRowId.HasValue)
        {
            // Update existing entry
            var kBytes = Encoding.UTF8.GetBytes(key);
            var vBytes = Encoding.UTF8.GetBytes(value);
            using var sw = SharcWriter.From(db);
            sw.Update("context", existingRowId.Value,
                ColumnValue.Text(2 * kBytes.Length + 13, kBytes),
                ColumnValue.Text(2 * vBytes.Length + 13, vBytes),
                author != null ? WorkspaceWriter.NullableTextVal(author) : ColumnValue.Null(),
                ColumnValue.FromInt64(1, now),  // created_at (unchanged in spirit but we overwrite)
                ColumnValue.FromInt64(1, now));  // updated_at
        }
        else
        {
            using var writer = new WorkspaceWriter(db);
            writer.WriteContext(new ContextEntry(key, value, author, now, now));
        }

        Console.WriteLine($"Set {key} = {value}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc set <key> <value> [--author a]");
    }
}
