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
        var profileArgument = profile is null ? string.Empty : $"-p \"{profile.Id}\" ";

        if (_repo.IsWsl && (profile is null || profile.IsDistroLauncher))
        {
            // Profiles that only launch a distro (Terminal's generated ones, distro apps' ones) get
            // wsl.exe in place of their command line: a distro app's launcher such as ubuntu.exe
            // opens interactive shells in the home folder whatever the starting directory. The
            // profile still provides the look (colours, icon, font, tab title).
            TerminalLauncher.Launch($"{profileArgument}wsl.exe -d \"{_repo.Distro}\" --cd \"{_repo.LinuxPath}\"");
        }
        else
        {
            // The profile keeps its own command line, startup commands included. wsl.exe-based
            // command lines start in the \\wsl.localhost folder's matching Linux path.
            TerminalLauncher.Launch($"{profileArgument}-d \"{_repo.FullPath}\"");
        }

        return CommandResult.Dismiss();
    }
}
