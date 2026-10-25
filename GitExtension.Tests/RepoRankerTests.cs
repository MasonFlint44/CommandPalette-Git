// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Tests;

public class RepoRankerTests : TestEnvironment
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly RepoHistory _history;

    public RepoRankerTests()
    {
        _history = new RepoHistory(Path.Combine(Directory, "history.json"));
    }

    private static GitRepoInfo Repo(string name) => new(name, $@"C:\code\{name}");

    private void Open(string name, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            _history.RecordOpen(Repo(name), Now);
        }
    }

    private string[] Rank(string? search, params string[] names) =>
        [.. RepoRanker.Rank(names.Select(Repo), search, _history, r => r.Distro ?? "Windows", Now).Select(r => r.Name)];

    [Fact]
    public void Without_a_search_repos_are_alphabetical_until_used()
    {
        Assert.Equal(["alpha", "Beta", "gamma"], Rank(null, "gamma", "Beta", "alpha"));
    }

    [Fact]
    public void Without_a_search_the_most_used_come_first()
    {
        Open("gamma", 3);
        Open("beta");

        Assert.Equal(["gamma", "beta", "alpha"], Rank("  ", "alpha", "beta", "gamma"));
    }

    [Fact]
    public void Repos_that_dont_match_are_left_out()
    {
        Assert.Equal(["disktray"], Rank("disk", "disktray", "dotfiles"));
    }

    [Fact]
    public void An_exact_name_beats_a_heavily_used_prefix_match()
    {
        Open("git-tools", 100);

        Assert.Equal(["git", "git-tools"], Rank("git", "git-tools", "git"));
    }

    [Fact]
    public void A_prefix_beats_a_heavily_used_word_start()
    {
        Open("CommandPalette-Git", 100);

        Assert.Equal(["gitea", "CommandPalette-Git"], Rank("git", "CommandPalette-Git", "gitea"));
    }

    [Fact]
    public void A_word_start_beats_a_heavily_used_fuzzy_match()
    {
        Open("digit-recognizer", 100);

        Assert.Equal(["CommandPalette-Git", "digit-recognizer"], Rank("git", "digit-recognizer", "CommandPalette-Git"));
    }

    [Fact]
    public void Use_reorders_equally_good_matches()
    {
        Assert.Equal(["lead-queue-api", "lead_queue_worker"], Rank("lead", "lead-queue-api", "lead_queue_worker"));

        Open("lead_queue_worker");

        Assert.Equal(["lead_queue_worker", "lead-queue-api"], Rank("lead", "lead-queue-api", "lead_queue_worker"));
    }

    [Fact]
    public void Use_reorders_close_fuzzy_matches_but_not_clearly_worse_ones()
    {
        // "dt" scores 10 against dotfiles, 6 against claude-terminal and 2 against CommandPalette-Git
        Open("claude-terminal", 31);
        Open("CommandPalette-Git", 31);

        Assert.Equal(
            ["claude-terminal", "dotfiles", "CommandPalette-Git"],
            Rank("dt", "dotfiles", "claude-terminal", "CommandPalette-Git"));
    }

    [Fact]
    public void Matches_the_path_and_location()
    {
        GitRepoInfo[] repos = [new("foo", @"\\wsl.localhost\Ubuntu\home\me\foo"), Repo("bar")];

        Assert.Equal(["foo"], RepoRanker.Rank(repos, "ubuntu", _history, r => r.Distro ?? "Windows", Now).Select(r => r.Name));
        Assert.Equal(["bar"], RepoRanker.Rank(repos, "windows", _history, r => r.Distro ?? "Windows", Now).Select(r => r.Name));
        Assert.Equal(["foo"], RepoRanker.Rank(repos, "home/me", _history, r => r.Distro ?? "Windows", Now).Select(r => r.Name));
    }

    [Theory]
    [InlineData("CommandPalette-Git", "git", true)]
    [InlineData("CommandPalette-Git", "palette", true)]
    [InlineData("CommandPalette-Git", "cpg", true)]
    [InlineData("CommandPalette-Git", "pg", true)]
    [InlineData("lead_queue_worker", "queue", true)]
    [InlineData("lead_queue_worker", "lqw", true)]
    [InlineData("obsidian.vault", "vault", true)]
    [InlineData("digit-recognizer", "git", false)]
    [InlineData("disktray", "tray", false)]
    [InlineData("CommandPalette-Git", "cg", false)]
    public void Word_starts_and_acronyms(string name, string query, bool expected)
    {
        Assert.Equal(expected, RepoRanker.MatchesWordStartOrAcronym(name, query));
    }

    [Fact]
    public void A_lowercase_search_ignores_case()
    {
        // The toolkit gives a point per letter whose case matches: 3 for git-tools, 2 for CommandPalette-Git
        Assert.Equal(FuzzyStringMatcher.ScoreFuzzy("git", "git-tools") - 3, RepoRanker.FuzzyScore("git", "git-tools"));
        Assert.Equal(FuzzyStringMatcher.ScoreFuzzy("git", "CommandPalette-Git") - 2, RepoRanker.FuzzyScore("git", "CommandPalette-Git"));
        Assert.Equal(RepoRanker.FuzzyScore("git", "Git-tools"), RepoRanker.FuzzyScore("git", "git-tools"));
    }

    [Fact]
    public void A_search_with_capitals_keeps_the_case_bonus()
    {
        Assert.Equal(FuzzyStringMatcher.ScoreFuzzy("Git", "Git-tools"), RepoRanker.FuzzyScore("Git", "Git-tools"));
        Assert.True(RepoRanker.FuzzyScore("Git", "Git-tools") > RepoRanker.FuzzyScore("Git", "git-tools"));
    }
}
