// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Commands;

/// <summary>
/// Opens the repository's page on its git host (GitHub, GitLab…) in the default browser.
/// It doesn't count as a use for ordering: browsing a repository isn't working in it.
/// </summary>
internal sealed partial class OpenRemoteCommand : InvokableCommand
{
    private readonly RemotePage _page;
    private readonly Launcher _launcher;

    public OpenRemoteCommand(RemotePage page, Launcher? launcher = null)
    {
        _page = page;
        _launcher = launcher ?? Launcher.Default;
        Name = page.HostName is null ? "Open remote in browser" : $"Open on {page.HostName}";
        Icon = new IconInfo(""); // Globe icon
    }

    public override CommandResult Invoke()
    {
        _launcher.Browser(_page.Url);

        return CommandResult.Dismiss();
    }
}
