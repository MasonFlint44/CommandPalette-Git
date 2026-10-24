// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Settings;

namespace GitExtension.Tests;

public class GitRepoSettingsManagerTests : TestEnvironment
{
    [Fact]
    public void Defaults()
    {
        var settings = NewSettings();

        Assert.Equal(string.Empty, settings.ScanPaths);
        Assert.Equal(3, settings.MaxDepth);
        Assert.Equal(GitRepoSettingsManager.OpenFolderAction, settings.DefaultAction);
        Assert.Equal(GitRepoSettingsManager.DefaultTerminalProfile, settings.TerminalProfile);
    }

    [Fact]
    public void Profile_choices_are_saved_and_read_back()
    {
        var settings = NewSettings();
        Assert.True(settings.SetRepoTerminalProfile("wsl:Ubuntu:/home/me/foo", "{a}"));
        Assert.True(settings.SetDistroTerminalProfile("Ubuntu", "{b}"));

        var reloaded = NewSettings();

        Assert.Equal("{a}", reloaded.GetRepoTerminalProfile("wsl:Ubuntu:/home/me/foo"));
        Assert.Equal("{b}", reloaded.GetDistroTerminalProfile("Ubuntu"));
        Assert.Null(reloaded.GetRepoTerminalProfile("wsl:Ubuntu:/home/me/other"));
    }

    [Fact]
    public void Clearing_a_choice_removes_it()
    {
        var settings = NewSettings();
        settings.SetDistroTerminalProfile("Ubuntu", "{b}");
        settings.SetDistroTerminalProfile("Ubuntu", null);

        Assert.Null(NewSettings().GetDistroTerminalProfile("Ubuntu"));
    }

    [Fact]
    public void Choice_keys_ignore_case()
    {
        var settings = NewSettings();
        settings.SetRepoTerminalProfile(@"C:\Code\Foo", "{a}");

        Assert.Equal("{a}", settings.GetRepoTerminalProfile(@"c:\code\foo"));
    }

    [Fact]
    public void Broken_choices_file_means_no_choices()
    {
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "settings"));
        File.WriteAllText(Path.Combine(Directory, "settings", "GitExtension.terminalProfiles.json"), """{ "repos": [1, 2] , "distros": { "Ubuntu": 5, "Debian": "{d}" } }""");

        var settings = NewSettings();

        Assert.Null(settings.GetDistroTerminalProfile("Ubuntu"));
        Assert.Equal("{d}", settings.GetDistroTerminalProfile("Debian"));
    }

    [Fact]
    public void Failed_save_is_reported()
    {
        var settings = NewSettings();
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "settings", "GitExtension.terminalProfiles.json"));

        Assert.False(settings.SetRepoTerminalProfile("x", "{a}"));
    }

    [Fact]
    public void Scan_paths_with_quotes_and_backslashes_round_trip()
    {
        var settings = NewSettings();
        var raised = false;
        settings.ScanPathsUpdated += (_, _) => raised = true;

        settings.UpdateScanPaths(@"\\wsl.localhost\Ubuntu\home\me;C:\a ""b""");

        Assert.True(raised);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\me;C:\a ""b""", NewSettings().ScanPaths);
    }
}
