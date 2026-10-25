// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;

namespace GitExtension.Tests;

public class RepoHistoryTests : TestEnvironment
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private string HistoryPath => Path.Combine(Directory, "history.json");

    private static GitRepoInfo Repo(string name = "foo", DateTimeOffset? activity = null) =>
        new(name, $@"C:\code\{name}") { LastGitActivity = activity };

    private RepoHistory HistoryWithOpens(GitRepoInfo repo, int opens, DateTimeOffset at)
    {
        var history = new RepoHistory(HistoryPath);
        for (var i = 0; i < opens; i++)
        {
            history.RecordOpen(repo, at);
        }

        return history;
    }

    // Command Palette's weights: recency * (10 + 10 * log2(uses + 1)), halving every 3 days, at most 70
    [Theory]
    [InlineData(0, 0.0, 0)]
    [InlineData(1, 0.0, 20)]
    [InlineData(1, 3.0, 10)]
    [InlineData(1, 6.0, 5)]
    [InlineData(7, 0.0, 40)]
    [InlineData(31, 0.0, 60)]
    [InlineData(127, 0.0, 70)]
    [InlineData(31, 30.0, 0)]
    public void Weight_matches_Command_Palettes(int opens, double daysAgo, int expected)
    {
        var repo = Repo();
        var history = HistoryWithOpens(repo, opens, Now.AddDays(-daysAgo));

        Assert.Equal(expected, history.Weight(repo, Now));
    }

    [Fact]
    public void Git_activity_counts_as_recent_use_but_not_as_a_use()
    {
        var history = new RepoHistory(HistoryPath);

        Assert.Equal(10, history.Weight(Repo(activity: Now), Now));
    }

    [Fact]
    public void Git_activity_brings_back_an_old_favourite()
    {
        var opened = Repo();
        var history = HistoryWithOpens(opened, 3, Now.AddDays(-30));

        // Recency from today's commit, frequency from the three opens: 10 + 10 * log2(4)
        Assert.Equal(30, history.Weight(Repo(activity: Now), Now));
    }

    [Fact]
    public void Older_git_activity_doesnt_override_a_newer_open()
    {
        var history = HistoryWithOpens(Repo(), 1, Now);

        Assert.Equal(20, history.Weight(Repo(activity: Now.AddDays(-9)), Now));
    }

    [Fact]
    public void A_future_timestamp_counts_as_now()
    {
        var history = HistoryWithOpens(Repo(), 1, Now.AddDays(1));

        Assert.Equal(20, history.Weight(Repo(), Now));
    }

    [Fact]
    public void History_survives_a_reload()
    {
        HistoryWithOpens(Repo(), 7, Now);

        Assert.Equal(40, new RepoHistory(HistoryPath).Weight(Repo(), Now));
    }

    [Fact]
    public void WSL_repos_keep_their_history_across_path_spellings()
    {
        var history = HistoryWithOpens(new GitRepoInfo("foo", @"\\wsl$\Ubuntu\home\me\foo"), 1, Now);

        Assert.Equal(20, history.Weight(new GitRepoInfo("foo", @"\\wsl.localhost\Ubuntu\home\me\foo"), Now));
    }

    [Fact]
    public void Keeps_the_most_recently_opened_repos()
    {
        var history = new RepoHistory(HistoryPath);
        for (var i = 0; i <= RepoHistory.MaxEntries; i++)
        {
            history.RecordOpen(Repo($"repo{i}"), Now.AddMinutes(i));
        }

        var reloaded = new RepoHistory(HistoryPath);
        Assert.Equal(0, reloaded.Weight(Repo("repo0"), Now));
        Assert.Equal(20, reloaded.Weight(Repo("repo1"), Now));
    }

    [Fact]
    public void An_unreadable_file_starts_empty()
    {
        File.WriteAllText(HistoryPath, "{ not json");

        Assert.Equal(0, new RepoHistory(HistoryPath).Weight(Repo(), Now));
    }

    [Fact]
    public void Recording_an_open_raises_Changed()
    {
        var history = new RepoHistory(HistoryPath);
        var raised = false;
        history.Changed += (s, e) => raised = true;

        history.RecordOpen(Repo(), Now);

        Assert.True(raised);
    }
}
