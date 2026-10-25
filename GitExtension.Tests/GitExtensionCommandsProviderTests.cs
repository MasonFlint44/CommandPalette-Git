// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Pages;
using GitExtension.Settings;
using Microsoft.CommandPalette.Extensions;

namespace GitExtension.Tests;

public class GitExtensionCommandsProviderTests : TestEnvironment
{
    private readonly GitRepoSettingsManager _settings;
    private readonly string _root;

    public GitExtensionCommandsProviderTests()
    {
        _root = Path.Combine(Directory, "repos");
        System.IO.Directory.CreateDirectory(Path.Combine(_root, "alpha", ".git"));
        _settings = NewSettings();
    }

    [Fact]
    public void Without_scan_paths_the_top_level_command_is_setup()
    {
        var command = Assert.Single(new GitExtensionCommandsProvider(_settings).TopLevelCommands());

        Assert.IsType<SetupPage>(command.Command);
        Assert.Equal("Browse git repositories", command.Title);
    }

    [Fact]
    public void With_scan_paths_the_top_level_command_browses_repos()
    {
        _settings.UpdateScanPaths(_root);

        var command = Assert.Single(new GitExtensionCommandsProvider(_settings).TopLevelCommands());

        Assert.IsType<GitReposPage>(command.Command);
    }

    [Fact]
    public void Saving_scan_paths_switches_from_setup_to_browsing()
    {
        var provider = new GitExtensionCommandsProvider(_settings);
        var raised = false;
        provider.ItemsChanged += (s, e) => raised = true;

        _settings.UpdateScanPaths(_root);

        Assert.IsType<GitReposPage>(Assert.Single(provider.TopLevelCommands()).Command);
        Assert.True(raised);
    }

    [Fact]
    public void Resolves_pinned_repos_by_id()
    {
        _settings.UpdateScanPaths(_root);
        var provider = new GitExtensionCommandsProvider(_settings);

        Assert.Equal("alpha", provider.GetCommandItem($"repo.{Path.Combine(_root, "alpha")}")?.Title);
        Assert.Null(provider.GetCommandItem($"repo.{Path.Combine(_root, "gone")}"));
    }
}
