// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Services;

/// <summary>
/// Starts the programs the open commands launch. Tests replace it to record command lines
/// instead of opening windows.
/// </summary>
internal class Launcher
{
    public static Launcher Default { get; } = new();

    /// <summary>Opens Windows Terminal with the given wt.exe command line.</summary>
    public virtual void Terminal(string arguments) => TerminalLauncher.Launch(arguments);

    /// <summary>Starts a program the way the shell would, e.g. code or explorer.exe.</summary>
    public virtual void Shell(string program, string arguments, string? workingDirectory = null) =>
        ShellHelpers.OpenInShell(program, arguments, workingDirectory);

    /// <summary>Opens a web page in the default browser.</summary>
    public virtual void Browser(string url) => ShellHelpers.OpenInShell(url);
}
