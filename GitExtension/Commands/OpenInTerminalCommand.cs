// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text;
using GitExtension.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Opens Windows Terminal (or cmd.exe as fallback) at the repository path.
/// WSL repositories open a shell inside their distro instead.
/// </summary>
internal sealed partial class OpenInTerminalCommand : InvokableCommand
{
    private readonly GitRepoInfo _repo;
    private readonly string _startupCommand;

    public OpenInTerminalCommand(GitRepoInfo repo, string startupCommand = "")
    {
        _repo = repo;
        _startupCommand = startupCommand;
        Name = "Open in terminal";
        Icon = new IconInfo(""); // CommandPrompt icon
    }

    public override CommandResult Invoke()
    {
        if (_repo.IsWsl)
        {
            ShellHelpers.OpenInShell("wt.exe", WslArguments());
        }
        else if (!string.IsNullOrWhiteSpace(_startupCommand))
        {
            // Open terminal in the repo directory and run the startup command
            ShellHelpers.OpenInShell("wt.exe", $"-d \"{_repo.FullPath}\" cmd /k \"{_startupCommand}\"", _repo.FullPath);
        }
        else
        {
            ShellHelpers.OpenInShell("wt.exe", $"-d \"{_repo.FullPath}\"", _repo.FullPath);
        }

        return CommandResult.Dismiss();
    }

    private string WslArguments()
    {
        var wsl = $"wsl.exe -d \"{_repo.Distro}\" --cd \"{_repo.LinuxPath}\"";
        if (string.IsNullOrWhiteSpace(_startupCommand))
        {
            return wsl;
        }

        // The command is base64-encoded so its quotes and semicolons survive wt.exe's
        // argument handling (wt treats ';' as a tab separator). It is sourced in an
        // interactive shell so .bashrc/.zshrc aliases and PATH changes apply, then a
        // login shell replaces it so the tab stays open whether the command succeeds or not.
        // "\;" is wt's escape for a literal ';'.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(_startupCommand));
        return $"{wsl} -- exec $SHELL -ic '. <(echo {encoded}|base64 -d)\\; exec $SHELL -l'";
    }
}
