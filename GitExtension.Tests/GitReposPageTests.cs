// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Pages;
using GitExtension.Settings;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Tests;

public class GitReposPageTests : TestEnvironment
{
    private readonly GitRepoSettingsManager _settings;
    private readonly string _alpha;

    public GitReposPageTests()
    {
        var root = Path.Combine(Directory, "repos");
        _alpha = Path.Combine(root, "alpha");
        System.IO.Directory.CreateDirectory(Path.Combine(_alpha, ".git"));
        System.IO.Directory.CreateDirectory(Path.Combine(root, "beta", ".git"));

        _settings = NewSettings();
        _settings.UpdateScanPaths(root);
    }

    // The first scan runs in the background; until it finishes the page shows no items
    private static IListItem[] Items(GitReposPage page)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var items = page.GetItems();
            if (items.Length > 0 || DateTime.UtcNow > deadline)
            {
                return items;
            }

            Thread.Sleep(50);
        }
    }

    private static string[] MenuNames(IListItem item) =>
        [.. item.MoreCommands.OfType<CommandContextItem>().Select(c => string.IsNullOrEmpty(c.Title) ? c.Command!.Name : c.Title)];

    [Fact]
    public void Lists_repos_with_a_location_tag()
    {
        var items = Items(new GitReposPage(_settings));

        Assert.Equal(["alpha", "beta"], items.Select(i => i.Title));
        Assert.Equal(_alpha, items[0].Subtitle);
        Assert.Equal("Windows", Assert.Single(items[0].Tags).Text);
    }

    // The first menu item is what Ctrl+Enter runs
    [Theory]
    [InlineData(null, "Open in VS Code", "Open in terminal", "Open folder")]
    [InlineData(GitRepoSettingsManager.OpenVSCodeAction, "Open in VS Code", "Open in terminal", "Open folder")]
    [InlineData(GitRepoSettingsManager.OpenTerminalAction, "Open in terminal", "Open in VS Code", "Open folder")]
    [InlineData(GitRepoSettingsManager.OpenFolderAction, "Open folder", "Open in VS Code", "Open in terminal")]
    public void Enter_runs_the_default_action_and_Ctrl_Enter_the_next(string? defaultAction, string enter, string ctrlEnter, string inMenu)
    {
        if (defaultAction is not null)
        {
            _settings.Settings.Update($$"""{ "gitRepos.defaultAction": "{{defaultAction}}" }""");
        }

        var item = Items(new GitReposPage(_settings))[0];

        Assert.Equal(enter, item.Command.Name);
        Assert.Equal([ctrlEnter, inMenu, "Copy path", "Set terminal profile…", "Settings"], MenuNames(item));
    }

    [Fact]
    public void Search_matches_name_and_location()
    {
        var page = new GitReposPage(_settings);
        Items(page);

        page.SearchText = "bet";
        Assert.Equal(["beta"], page.GetItems().Select(i => i.Title));

        page.SearchText = "windows";
        Assert.Equal(2, page.GetItems().Length);

        page.SearchText = "Ubuntu";
        Assert.Empty(page.GetItems());
    }

    [Fact]
    public void Pinned_repo_resolves_before_the_page_is_opened()
    {
        var page = new GitReposPage(_settings);

        var item = page.GetCommandItemById($"repo.{_alpha}");

        Assert.NotNull(item);
        Assert.Equal("alpha", item.Title);
        Assert.NotEmpty(((IListItem)item).MoreCommands);
    }

    [Fact]
    public void No_repos_offers_to_change_scan_paths()
    {
        _settings.UpdateScanPaths(Path.Combine(Directory, "empty"));
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "empty"));

        var items = Items(new GitReposPage(_settings));

        Assert.Equal(["No git repositories found", "Change scan paths"], items.Select(i => i.Title));
    }

    [Fact]
    public void Opened_repos_move_to_the_top()
    {
        var page = new GitReposPage(_settings);
        Assert.Equal(["alpha", "beta"], Items(page).Select(i => i.Title));

        _settings.History.RecordOpen(new GitRepoInfo("beta", Path.Combine(Path.GetDirectoryName(_alpha)!, "beta")));

        Assert.Equal(["beta", "alpha"], page.GetItems().Select(i => i.Title));
    }

    [Fact]
    public void Repos_with_a_remote_can_open_it_after_the_open_actions()
    {
        File.WriteAllText(Path.Combine(_alpha, ".git", "config"), "[remote \"origin\"]\n\turl = https://github.com/me/alpha.git\n");

        var items = Items(new GitReposPage(_settings));

        Assert.Equal(["Open in terminal", "Open folder", "Open on GitHub", "Copy path"], MenuNames(items[0]).Take(4));
        Assert.DoesNotContain("Open on GitHub", MenuNames(items[1]));
    }
}
