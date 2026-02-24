// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Reads and writes config.arc entries.
/// </summary>
public static class ConfigCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help")
        {
            PrintHelp();
            return 0;
        }

        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null)
        {
            Console.Error.WriteLine("Not initialized. Run 'sharc init' first.");
            return 1;
        }

        var configPath = Path.Combine(sharcDir, RepoLocator.ConfigFileName);
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("Config file not found.");
            return 1;
        }

        // No args: list all
        if (args.Length == 0)
        {
            using var db = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = false });
            using var cw = new ConfigWriter(db);
            var entries = cw.GetAll();
            foreach (var (key, value) in entries)
                Console.WriteLine($"{key} = {value}");
            return 0;
        }

        string configKey = args[0];

        // One arg: get value
        if (args.Length == 1)
        {
            using var db = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = false });
            using var cw = new ConfigWriter(db);
            var val = cw.Get(configKey);
            if (val == null)
            {
                Console.Error.WriteLine($"Key not found: {configKey}");
                return 1;
            }
            Console.WriteLine(val);
            return 0;
        }

        // Two args: set value
        string configValue = args[1];
        using (var db = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = true }))
        {
            using var cw = new ConfigWriter(db);
            cw.Set(configKey, configValue);
            Console.WriteLine($"Set {configKey} = {configValue}");
        }
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: sharc config [<key> [<value>]]");
        Console.WriteLine();
        Console.WriteLine("  No arguments:    List all config entries");
        Console.WriteLine("  sharc config <key>          Get a config value");
        Console.WriteLine("  sharc config <key> <value>  Set a config value");
    }
}
