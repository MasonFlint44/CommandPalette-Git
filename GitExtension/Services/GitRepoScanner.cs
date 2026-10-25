// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GitExtension.Models;

namespace GitExtension.Services;

/// <summary>
/// Scans configured directories for git repositories.
/// </summary>
public static class GitRepoScanner
{
    /// <summary>
    /// Scans the given semicolon-separated root paths for git repositories
    /// up to the specified maximum depth.
    /// </summary>
    public static List<GitRepoInfo> Scan(string scanPaths, int maxDepth)
    {
        var roots = ParseScanPaths(scanPaths);
        var repos = new List<GitRepoInfo>();

        foreach (var root in roots)
        {
            ScanDirectory(root, maxDepth, 0, repos);
        }

        repos.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return repos;
    }

    private static string[] ParseScanPaths(string scanPaths)
    {
        return scanPaths
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ResolvePath)
            .Where(Directory.Exists)
            .ToArray();
    }

    private static string ResolvePath(string path)
    {
        // Expand ~ to user profile directory
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path[1..].TrimStart('\\', '/'));
        }

        // Expand environment variables like %USERPROFILE%
        path = Environment.ExpandEnvironmentVariables(path);

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// The repository's git folder: <paramref name="dotGit"/> itself, or for a worktree the
    /// folder its .git file points at. Null if that can't be read.
    /// </summary>
    private static string? GitFolder(string dotGit)
    {
        try
        {
            if (!File.Exists(dotGit))
            {
                return dotGit;
            }

            var line = File.ReadLines(dotGit).FirstOrDefault() ?? string.Empty;
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return null;
            }

            var target = line["gitdir:".Length..].Trim();
            if (target.StartsWith('/') && dotGit.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // A WSL worktree's gitdir is a Linux path: put it under the distro's \\wsl.localhost\<distro>
                var share = dotGit.Split('\\', 5);
                target = $@"\\{share[2]}\{share[3]}{target.Replace('/', '\\')}";
            }

            return Path.GetFullPath(target, Path.GetDirectoryName(dotGit)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// When HEAD's reflog (logs/HEAD in the git folder) last changed. Git appends to it whenever
    /// HEAD moves, which background fetches don't do.
    /// </summary>
    private static DateTimeOffset? LastHeadChange(string gitFolder)
    {
        try
        {
            var reflog = new FileInfo(Path.Combine(gitFolder, "logs", "HEAD"));
            return reflog.Exists ? new DateTimeOffset(reflog.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The web page of the repository's origin remote (or its first remote), from its config.
    /// A worktree's config is in the main repository's git folder, named in its commondir file.
    /// </summary>
    private static RemotePage? ReadRemotePage(string gitFolder)
    {
        try
        {
            var commonDir = Path.Combine(gitFolder, "commondir");
            var configFolder = File.Exists(commonDir)
                ? Path.GetFullPath(File.ReadLines(commonDir).First().Trim(), gitFolder)
                : gitFolder;

            var remote = GitRemotes.PreferredRemoteUrl(File.ReadAllText(Path.Combine(configFolder, "config")));
            return remote is null ? null : GitRemotes.WebPage(remote);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void ScanDirectory(string directory, int maxDepth, int currentDepth, List<GitRepoInfo> results)
    {
        if (currentDepth > maxDepth)
        {
            return;
        }

        try
        {
            var gitDir = Path.Combine(directory, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir))
            {
                var name = Path.GetFileName(directory) ?? directory;
                var gitFolder = GitFolder(gitDir);
                results.Add(new GitRepoInfo(name, directory)
                {
                    LastGitActivity = gitFolder is null ? null : LastHeadChange(gitFolder),
                    Remote = gitFolder is null ? null : ReadRemotePage(gitFolder),
                });
                // Don't recurse into a repo's subdirectories (avoids submodules/nested repos)
                return;
            }

            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                // Skip hidden, system, and special directories for performance
                var dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.') ||
                    dirName.StartsWith('$') ||
                    dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ScanDirectory(subDir, maxDepth, currentDepth + 1, results);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (IOException)
        {
            // Skip directories with I/O errors (e.g., broken symlinks)
        }
    }
}
