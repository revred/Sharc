// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;

namespace Sharc.Repo.Cli;

/// <summary>
/// Checks whether a data channel is enabled in config.arc before writes.
/// </summary>
internal static class ChannelGuard
{
    /// <summary>
    /// Returns true if the channel is enabled, false if disabled.
    /// Prints a message to stderr when disabled.
    /// </summary>
    public static bool IsEnabled(string channelName)
    {
        var sharcDir = RepoLocator.FindSharcDir();
        if (sharcDir == null) return false;

        var configPath = Path.Combine(sharcDir, RepoLocator.ConfigFileName);
        if (!File.Exists(configPath)) return true; // no config = all enabled

        using var db = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = false });
        using var cw = new ConfigWriter(db);
        var val = cw.Get($"channel.{channelName}");

        if (string.Equals(val, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"Channel '{channelName}' is disabled. Use 'sharc config channel.{channelName} enabled'.");
            return false;
        }

        return true;
    }
}
