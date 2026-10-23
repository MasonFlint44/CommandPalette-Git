// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using GitExtension.Settings;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Opens Windows Terminal at the repository path, using the repository's terminal profile
/// (see <see cref="TerminalProfiles.Resolve"/>).
/// </summary>
internal sealed partial class OpenInTerminalCommand : InvokableCommand
{
    private readonly GitRepoInfo _repo;
    private readonly GitRepoSettingsManager _settingsManager;

    public OpenInTerminalCommand(GitRepoInfo repo, GitRepoSettingsManager settingsManager)
    {
        _repo = repo;
        _settingsManager = settingsManager;
        Name = "Open in terminal";
        Icon = new IconInfo(""); // CommandPrompt icon
    }

    public override CommandResult Invoke()
    {
        var profile = TerminalProfiles.Resolve(_repo, _settingsManager, TerminalProfiles.Load());

        if (profile is not null)
        {
            // The profile keeps its own command line. For WSL profiles, Windows Terminal turns a
            // \\wsl.localhost starting directory into the matching Linux folder.
            TerminalLauncher.Launch($"-p \"{profile.Id}\" -d \"{_repo.FullPath}\"");
        }
        else if (_repo.IsWsl)
        {
            // No profile for this distro: run wsl.exe in Windows Terminal's default profile
            TerminalLauncher.Launch($"wsl.exe -d \"{_repo.Distro}\" --cd \"{_repo.LinuxPath}\"");
        }
        else
        {
            TerminalLauncher.Launch($"-d \"{_repo.FullPath}\"");
        }

        return CommandResult.Dismiss();
    }
}
