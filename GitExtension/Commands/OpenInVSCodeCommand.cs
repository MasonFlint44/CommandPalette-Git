// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Opens the repository in Visual Studio Code.
/// </summary>
internal sealed partial class OpenInVSCodeCommand : InvokableCommand
{
    private readonly GitRepoInfo _repo;
    private readonly RepoHistory? _history;
    private readonly Launcher _launcher;

    public OpenInVSCodeCommand(GitRepoInfo repo, RepoHistory? history = null, Launcher? launcher = null)
    {
        _repo = repo;
        _history = history;
        _launcher = launcher ?? Launcher.Default;
        Name = "Open in VS Code";
        Icon = new IconInfo(""); // Code icon
    }

    public override CommandResult Invoke()
    {
        _history?.RecordOpen(_repo);

        if (_repo.IsWsl)
        {
            // Open through the WSL remote so VS Code runs its server inside the distro.
            // No working directory: code is a .cmd and cmd.exe rejects UNC working directories.
            _launcher.Shell("code", $"--remote \"wsl+{_repo.Distro}\" \"{_repo.LinuxPath}\"");
        }
        else
        {
            _launcher.Shell("code", $"\"{_repo.FullPath}\"", _repo.FullPath);
        }

        return CommandResult.Dismiss();
    }
}
