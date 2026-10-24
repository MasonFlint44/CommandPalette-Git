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
}
