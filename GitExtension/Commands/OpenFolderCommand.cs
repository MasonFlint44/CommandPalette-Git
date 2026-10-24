// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Opens the repository folder in Windows Explorer.
/// </summary>
internal sealed partial class OpenFolderCommand : InvokableCommand
{
    private readonly GitRepoInfo _repo;
    private readonly Launcher _launcher;

    public OpenFolderCommand(GitRepoInfo repo, Launcher? launcher = null)
    {
        _repo = repo;
        _launcher = launcher ?? Launcher.Default;
        Name = "Open folder";
        Icon = new IconInfo("\uE838"); // FolderOpen icon
    }

    public override CommandResult Invoke()
    {
        _launcher.Shell("explorer.exe", _repo.FullPath);

        return CommandResult.Dismiss();
    }
}
