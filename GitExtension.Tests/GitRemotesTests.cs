// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Services;

namespace GitExtension.Tests;

public class GitRemotesTests
{
    [Fact]
    public void Prefers_origin()
    {
        const string config = """
            [core]
                bare = false
            [remote "upstream"]
                url = https://github.com/someone/repo.git
            [remote "origin"]
                url = git@github.com:me/repo.git
                fetch = +refs/heads/*:refs/remotes/origin/*
            """;

        Assert.Equal("git@github.com:me/repo.git", GitRemotes.PreferredRemoteUrl(config));
    }

    [Fact]
    public void Falls_back_to_the_first_remote()
    {
        const string config = """
            [remote "upstream"]
                url = https://github.com/someone/repo.git
            [remote "mine"]
                url = https://github.com/me/repo.git
            """;

        Assert.Equal("https://github.com/someone/repo.git", GitRemotes.PreferredRemoteUrl(config));
    }

    [Fact]
    public void No_remotes_means_no_url()
    {
        Assert.Null(GitRemotes.PreferredRemoteUrl("[core]\n\tbare = false\n"));
        Assert.Null(GitRemotes.PreferredRemoteUrl(string.Empty));
    }

    [Fact]
    public void Reads_quoted_values_comments_and_odd_spacing()
    {
        const string config = """
            # a comment
            [Remote "origin"]
            ; another
            	URL="https://github.com/me/my repo.git" # trailing comment
            	url = https://github.com/me/second-url.git
            [branch "main"]
                url = https://example.com/not-a-remote.git
            """;

        Assert.Equal([("origin", "https://github.com/me/my repo.git")], GitRemotes.RemoteUrls(config));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo", "GitHub")]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo", "GitHub")]
    [InlineData("https://github.com/owner/repo/", "https://github.com/owner/repo", "GitHub")]
    [InlineData("git@github.com:owner/repo.git", "https://github.com/owner/repo", "GitHub")]
    [InlineData("github.com:owner/repo", "https://github.com/owner/repo", "GitHub")]
    [InlineData("ssh://git@github.com/owner/repo.git", "https://github.com/owner/repo", "GitHub")]
    [InlineData("ssh://git@gitlab.example.com:2222/group/sub/repo.git", "https://gitlab.example.com/group/sub/repo", null)]
    [InlineData("git://github.com/owner/repo.git", "https://github.com/owner/repo", "GitHub")]
    [InlineData("https://GitHub.com/Owner/Repo.git", "https://github.com/Owner/Repo", "GitHub")]
    [InlineData("git@gitlab.com:group/sub/repo.git", "https://gitlab.com/group/sub/repo", "GitLab")]
    [InlineData("git@bitbucket.org:team/repo.git", "https://bitbucket.org/team/repo", "Bitbucket")]
    [InlineData("http://git.local:8080/me/repo.git", "http://git.local:8080/me/repo", null)]
    [InlineData("git@ssh.dev.azure.com:v3/org/project/repo", "https://dev.azure.com/org/project/_git/repo", "Azure DevOps")]
    [InlineData("https://org@dev.azure.com/org/project/_git/repo", "https://dev.azure.com/org/project/_git/repo", "Azure DevOps")]
    [InlineData("org@vs-ssh.visualstudio.com:v3/org/project/repo", "https://org.visualstudio.com/project/_git/repo", "Azure DevOps")]
    [InlineData("https://org.visualstudio.com/project/_git/repo", "https://org.visualstudio.com/project/_git/repo", "Azure DevOps")]
    public void Turns_remotes_into_web_pages(string remote, string url, string? hostName)
    {
        var page = GitRemotes.WebPage(remote);

        Assert.NotNull(page);
        Assert.Equal(url, page.Url);
        Assert.Equal(hostName, page.HostName);
    }

    [Theory]
    [InlineData("https://me:ghp_secret@github.com/owner/repo.git")]
    [InlineData("https://ghp_secret@github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo.git?token=ghp_secret")]
    public void Never_keeps_credentials(string remote)
    {
        var page = GitRemotes.WebPage(remote);

        Assert.Equal("https://github.com/owner/repo", page?.Url);
    }

    [Theory]
    [InlineData(@"C:\repos\other")]
    [InlineData("C:/repos/other")]
    [InlineData("/home/me/other.git")]
    [InlineData("../other")]
    [InlineData("file:///C:/repos/other.git")]
    [InlineData("https://github.com/")]
    [InlineData("not a url")]
    public void Local_and_unreadable_remotes_have_no_page(string remote)
    {
        Assert.Null(GitRemotes.WebPage(remote));
    }
}
