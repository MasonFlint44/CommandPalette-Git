// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Copies the repository path to the clipboard. For WSL repositories this is the
/// Linux path, unless <c>windowsPath</c> is set.
/// </summary>
internal sealed partial class CopyRepoPathCommand : InvokableCommand
{
    private readonly string _path;

    public CopyRepoPathCommand(GitRepoInfo repo, bool windowsPath = false)
    {
        _path = repo.IsWsl && !windowsPath ? repo.LinuxPath! : repo.FullPath;
        Name = windowsPath ? "Copy Windows path" : "Copy path";
        Icon = new IconInfo(""); // Copy icon
    }

    public override CommandResult Invoke()
    {
        ClipboardHelper.SetText(_path);
        return CommandResult.ShowToast($"Copied: {_path}");
    }
}
