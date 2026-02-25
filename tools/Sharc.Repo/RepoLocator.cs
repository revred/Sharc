// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo;

/// <summary>
/// Locates the .sharc/ folder by walking from a starting directory up to the git repo root.
/// </summary>
public static class RepoLocator
{
    public const string SharcDirName = ".sharc";
    public const string WorkspaceFileName = "workspace.arc";
    public const string ConfigFileName = "config.arc";

    /// <summary>
    /// Finds the git repo root (directory containing .git/).
    /// Returns null if not inside a git repo.
    /// </summary>
    public static string? FindGitRoot(string? startDir = null)
    {
        var dir = startDir ?? Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Finds the .sharc/ directory, or returns null if not initialized.
    /// </summary>
    public static string? FindSharcDir(string? startDir = null)
    {
        var gitRoot = FindGitRoot(startDir);
        if (gitRoot == null) return null;

        var sharcDir = Path.Combine(gitRoot, SharcDirName);
        return Directory.Exists(sharcDir) ? sharcDir : null;
    }

    /// <summary>
    /// Returns the path to workspace.arc within the .sharc/ directory.
    /// Throws if .sharc/ not found.
    /// </summary>
    public static string GetWorkspacePath(string? startDir = null)
    {
        var sharcDir = FindSharcDir(startDir)
            ?? throw new InvalidOperationException("Not initialized. Run 'sharc init' first.");
        return Path.Combine(sharcDir, WorkspaceFileName);
    }

    /// <summary>
    /// Returns the path to config.arc within the .sharc/ directory.
    /// Throws if .sharc/ not found.
    /// </summary>
    public static string GetConfigPath(string? startDir = null)
    {
        var sharcDir = FindSharcDir(startDir)
            ?? throw new InvalidOperationException("Not initialized. Run 'sharc init' first.");
        return Path.Combine(sharcDir, ConfigFileName);
    }
}
