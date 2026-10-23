// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Opens Windows Terminal at the repository path.
/// WSL repositories open a shell inside their distro instead.
/// </summary>
internal sealed partial class OpenInTerminalCommand : InvokableCommand
{
    private readonly GitRepoInfo _repo;

    public OpenInTerminalCommand(GitRepoInfo repo)
    {
        _repo = repo;
        Name = "Open in terminal";
        Icon = new IconInfo(""); // CommandPrompt icon
    }

    public override CommandResult Invoke()
    {
        if (_repo.IsWsl)
        {
            TerminalLauncher.Launch($"wsl.exe -d \"{_repo.Distro}\" --cd \"{_repo.LinuxPath}\"");
        }
        else
        {
            TerminalLauncher.Launch($"-d \"{_repo.FullPath}\"");
        }

        return CommandResult.Dismiss();
    }
}
