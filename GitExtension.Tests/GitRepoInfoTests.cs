// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;

namespace GitExtension.Tests;

public class GitRepoInfoTests : TestEnvironment
{
    [Fact]
    public void Windows_repo_keeps_its_path()
    {
        var repo = new GitRepoInfo("foo", @"C:\code\foo");

        Assert.False(repo.IsWsl);
        Assert.Null(repo.Distro);
        Assert.Null(repo.LinuxPath);
        Assert.Equal(@"C:\code\foo", repo.DisplayPath);
        Assert.Equal(@"C:\code\foo", repo.ChoiceKey);
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\me\foo")]
    [InlineData(@"\\wsl$\Ubuntu\home\me\foo")]
    [InlineData(@"\\WSL.LOCALHOST\Ubuntu\home\me\foo")]
    public void WSL_repo_gets_distro_and_linux_path(string path)
    {
        var repo = new GitRepoInfo("foo", path);

        Assert.True(repo.IsWsl);
        Assert.Equal("Ubuntu", repo.Distro);
        Assert.Equal("/home/me/foo", repo.LinuxPath);
        Assert.Equal("Ubuntu: /home/me/foo", repo.DisplayPath);
        Assert.Equal(path, repo.FullPath);
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu")]
    [InlineData(@"\\wsl.localhost\Ubuntu\")]
    public void Distro_root_is_slash(string path)
    {
        Assert.Equal("/", new GitRepoInfo("Ubuntu", path).LinuxPath);
    }

    [Fact]
    public void Linux_path_keeps_spaces()
    {
        Assert.Equal("/home/me/my repo", new GitRepoInfo("my repo", @"\\wsl.localhost\Ubuntu\home\me\my repo").LinuxPath);
    }

    [Fact]
    public void Distro_name_takes_its_registered_spelling()
    {
        SetDistros("Ubuntu-22.04");

        Assert.Equal("Ubuntu-22.04", new GitRepoInfo("foo", @"\\wsl.localhost\ubuntu-22.04\home\me\foo").Distro);
    }

    [Fact]
    public void Unregistered_distro_keeps_the_typed_spelling()
    {
        SetDistros("Ubuntu");

        Assert.Equal("debian", new GitRepoInfo("foo", @"\\wsl.localhost\debian\home\me\foo").Distro);
    }

    [Fact]
    public void Choice_key_is_the_same_for_every_spelling_of_the_path()
    {
        string[] paths = [@"\\wsl.localhost\Ubuntu\home\me\foo", @"\\wsl$\Ubuntu\home\me\foo", @"\\wsl.localhost\ubuntu\home\me\foo"];

        Assert.Single(paths.Select(p => new GitRepoInfo("foo", p).ChoiceKey).Distinct());
    }

    [Theory]
    [InlineData(@"\\server\share\foo")]
    [InlineData(@"\\wsl.localhost.example.com\Ubuntu\foo")]
    [InlineData(@"D:\wsl.localhost\Ubuntu\foo")]
    public void Other_paths_are_not_WSL(string path)
    {
        Assert.False(new GitRepoInfo("foo", path).IsWsl);
    }
}

public class WslDistrosTests : TestEnvironment
{
    [Fact]
    public void Canonicalize_matches_case_insensitively()
    {
        SetDistros("Ubuntu", "docker-desktop");

        Assert.Equal("Ubuntu", WslDistros.Canonicalize("UBUNTU"));
        Assert.Equal("docker-desktop", WslDistros.Canonicalize("Docker-Desktop"));
        Assert.Equal("Fedora", WslDistros.Canonicalize("Fedora"));
    }

    [Fact]
    public void Unknown_names_dont_reread_the_registry_on_every_call()
    {
        var reads = 0;
        WslDistros.SetSource(() =>
        {
            reads++;
            return ["Ubuntu"];
        });

        for (var i = 0; i < 10; i++)
        {
            WslDistros.Canonicalize("Unknown");
        }

        Assert.Equal(1, reads);
    }
}
