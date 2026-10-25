// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Services;

namespace GitExtension.Tests;

public class GitRepoScannerTests : TestEnvironment
{
    private string MakeRepo(string relativePath, bool gitFile = false)
    {
        var path = Path.Combine(Directory, "root", relativePath);
        System.IO.Directory.CreateDirectory(path);
        if (gitFile)
        {
            // Worktrees and submodules have a .git file, not a folder
            File.WriteAllText(Path.Combine(path, ".git"), "gitdir: elsewhere");
        }
        else
        {
            System.IO.Directory.CreateDirectory(Path.Combine(path, ".git"));
        }

        return path;
    }

    private string Root => Path.Combine(Directory, "root");

    private static string[] Names(string scanPaths, int maxDepth = 3) =>
        [.. GitRepoScanner.Scan(scanPaths, maxDepth).Select(r => r.Name)];

    [Fact]
    public void Finds_repos_sorted_by_name()
    {
        MakeRepo("zeta");
        MakeRepo(@"group\Alpha");
        MakeRepo("worktree", gitFile: true);

        Assert.Equal(["Alpha", "worktree", "zeta"], Names(Root));
    }

    [Fact]
    public void Doesnt_look_inside_repos()
    {
        MakeRepo("outer");
        MakeRepo(@"outer\inner");

        Assert.Equal(["outer"], Names(Root));
    }

    [Fact]
    public void Skips_hidden_and_package_folders()
    {
        MakeRepo("visible");
        MakeRepo(@".cache\hidden");
        MakeRepo(@"$Recycle\deleted");
        MakeRepo(@"node_modules\package");

        Assert.Equal(["visible"], Names(Root));
    }

    [Fact]
    public void Respects_max_depth()
    {
        MakeRepo(@"a\b\deep");

        Assert.Empty(Names(Root, maxDepth: 2));
        Assert.Equal(["deep"], Names(Root, maxDepth: 3));
    }

    [Fact]
    public void Scans_several_paths_and_ignores_missing_ones()
    {
        var first = MakeRepo(@"one\first");
        var second = MakeRepo(@"two\second");
        var missing = Path.Combine(Directory, "missing");

        Assert.Equal(["first", "second"], Names($" {Path.GetDirectoryName(first)} ;{missing};; {Path.GetDirectoryName(second)}"));
    }

    [Fact]
    public void Expands_environment_variables()
    {
        MakeRepo("repo");
        Environment.SetEnvironmentVariable("GITEXTENSION_TEST_ROOT", Root);
        try
        {
            Assert.Equal(["repo"], Names("%GITEXTENSION_TEST_ROOT%"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITEXTENSION_TEST_ROOT", null);
        }
    }

    private static void WriteReflog(string gitFolder, DateTime writtenUtc)
    {
        var reflog = Path.Combine(gitFolder, "logs", "HEAD");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(reflog)!);
        File.WriteAllText(reflog, "0000 1111 Me <me@example.com> 0 +0000\tcommit: x\n");
        File.SetLastWriteTimeUtc(reflog, writtenUtc);
    }

    [Fact]
    public void Reads_git_activity_from_the_HEAD_reflog()
    {
        var written = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);
        WriteReflog(Path.Combine(MakeRepo("active"), ".git"), written);
        MakeRepo("fresh");

        var repos = GitRepoScanner.Scan(Root, 3);

        Assert.Equal(new DateTimeOffset(written), repos.Single(r => r.Name == "active").LastGitActivity);
        Assert.Null(repos.Single(r => r.Name == "fresh").LastGitActivity);
    }

    [Fact]
    public void Reads_a_worktrees_activity_from_its_own_git_folder()
    {
        var written = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
        var worktreeGit = Path.Combine(Directory, "main", ".git", "worktrees", "feature");
        WriteReflog(worktreeGit, written);
        var worktree = Path.Combine(Root, "feature");
        System.IO.Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {worktreeGit}\n");

        Assert.Equal(new DateTimeOffset(written), GitRepoScanner.Scan(Root, 3).Single().LastGitActivity);
    }
}
