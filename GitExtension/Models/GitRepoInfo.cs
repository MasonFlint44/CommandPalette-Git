// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text.RegularExpressions;
using GitExtension.Services;

namespace GitExtension.Models;

/// <summary>
/// Represents a discovered git repository on the local filesystem.
/// </summary>
public sealed partial class GitRepoInfo
{
    public GitRepoInfo(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;

        var match = WslPathRegex().Match(fullPath);
        if (match.Success)
        {
            Distro = WslDistros.Canonicalize(match.Groups["distro"].Value);
            LinuxPath = match.Groups["path"].Success ? match.Groups["path"].Value.Replace('\\', '/') : "/";
        }
    }

    /// <summary>The display name of the repository (folder name).</summary>
    public string Name { get; }

    /// <summary>The full absolute path to the repository root directory.</summary>
    public string FullPath { get; }

    /// <summary>The WSL distro the repository lives in, or null for a Windows repository.</summary>
    public string? Distro { get; }

    /// <summary>The repository's path inside its WSL distro, or null for a Windows repository.</summary>
    public string? LinuxPath { get; }

    public bool IsWsl => Distro is not null;

    /// <summary>
    /// When git last moved the repository's HEAD (commit, checkout, pull, merge, rebase), or null
    /// if unknown. Fetches don't count.
    /// </summary>
    public DateTimeOffset? LastGitActivity { get; init; }

    /// <summary>
    /// Identifies the repository in saved choices: distro and Linux path for WSL, so switching a
    /// scan path between \\wsl$ and \\wsl.localhost (or its case) doesn't lose them.
    /// </summary>
    public string ChoiceKey => IsWsl ? $"wsl:{Distro}:{LinuxPath}" : FullPath;

    /// <summary>The path shown to the user: "Ubuntu: /home/me/repo" for WSL, the Windows path otherwise.</summary>
    public string DisplayPath => IsWsl ? $"{Distro}: {LinuxPath}" : FullPath;

    // Matches \\wsl.localhost\<distro>\... and \\wsl$\<distro>\...
    [GeneratedRegex(@"^\\\\(?:wsl\.localhost|wsl\$)\\(?<distro>[^\\]+)(?<path>\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WslPathRegex();
}
