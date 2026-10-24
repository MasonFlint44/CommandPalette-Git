// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Commands;
using GitExtension.Models;
using Microsoft.CommandPalette.Extensions;
using static GitExtension.Tests.Profiles;

namespace GitExtension.Tests;

public class OpenCommandTests : TestEnvironment
{
    private static readonly GitRepoInfo WindowsRepo = new("foo", @"C:\code\my foo");

    private static GitRepoInfo UbuntuRepo => new("foo", @"\\wsl.localhost\Ubuntu\home\me\my foo");

    private readonly RecordingLauncher _launcher = new();

    private string OpenInTerminal(GitRepoInfo repo, Action<Settings.GitRepoSettingsManager>? configure = null)
    {
        var settings = NewSettings();
        configure?.Invoke(settings);
        var result = new OpenInTerminalCommand(repo, settings, _launcher).Invoke();
        Assert.Equal(CommandResultKind.Dismiss, result.Kind);
        return Assert.Single(_launcher.Launches);
    }

    [Fact]
    public void Terminal_without_profiles_opens_Windows_repo_with_Terminals_default()
    {
        Assert.Equal(@"wt -d ""C:\code\my foo""", OpenInTerminal(WindowsRepo));
    }

    [Fact]
    public void Terminal_uses_the_chosen_profile_for_a_Windows_repo()
    {
        SetTerminalProfiles(PowerShell);

        Assert.Equal(
            $@"wt -p ""{PowerShellId}"" -d ""C:\code\my foo""",
            OpenInTerminal(WindowsRepo, s => s.SetRepoTerminalProfile(WindowsRepo.ChoiceKey, PowerShellId)));
    }

    [Fact]
    public void Terminal_runs_wsl_in_the_repo_with_the_distro_apps_look()
    {
        SetTerminalProfiles(PowerShell, UbuntuGenerated, UbuntuApp);

        Assert.Equal(
            $@"wt -p ""{UbuntuAppId}"" wsl.exe -d ""Ubuntu"" --cd ""/home/me/my foo""",
            OpenInTerminal(UbuntuRepo));
    }

    [Fact]
    public void Terminal_runs_wsl_with_the_generated_profile()
    {
        SetTerminalProfiles(UbuntuGenerated);

        Assert.Equal(
            $@"wt -p ""{UbuntuGeneratedId}"" wsl.exe -d ""Ubuntu"" --cd ""/home/me/my foo""",
            OpenInTerminal(UbuntuRepo));
    }

    [Fact]
    public void Terminal_runs_wsl_without_any_profile()
    {
        Assert.Equal(@"wt wsl.exe -d ""Ubuntu"" --cd ""/home/me/my foo""", OpenInTerminal(UbuntuRepo));
    }

    [Fact]
    public void Terminal_keeps_a_custom_command_line_and_starts_in_the_network_path()
    {
        SetTerminalProfiles(Store(UbuntuAppId, "Ubuntu", commandLine: "wsl.exe -d Ubuntu -- tmux"));

        Assert.Equal(
            $@"wt -p ""{UbuntuAppId}"" -d ""\\wsl.localhost\Ubuntu\home\me\my foo""",
            OpenInTerminal(UbuntuRepo));
    }

    [Fact]
    public void Terminal_keeps_a_non_WSL_profile_chosen_for_a_WSL_repo()
    {
        SetTerminalProfiles(UbuntuApp, PowerShell);

        Assert.Equal(
            $@"wt -p ""{PowerShellId}"" -d ""\\wsl.localhost\Ubuntu\home\me\my foo""",
            OpenInTerminal(UbuntuRepo, s => s.SetDistroTerminalProfile("Ubuntu", PowerShellId)));
    }

    [Fact]
    public void VS_Code_opens_a_Windows_repo_in_its_folder()
    {
        new OpenInVSCodeCommand(WindowsRepo, _launcher).Invoke();

        Assert.Equal(@"code ""C:\code\my foo"" (in C:\code\my foo)", Assert.Single(_launcher.Launches));
    }

    [Fact]
    public void VS_Code_opens_a_WSL_repo_through_the_WSL_remote()
    {
        new OpenInVSCodeCommand(UbuntuRepo, _launcher).Invoke();

        Assert.Equal(@"code --remote ""wsl+Ubuntu"" ""/home/me/my foo""", Assert.Single(_launcher.Launches));
    }

    [Fact]
    public void Folder_opens_in_Explorer()
    {
        new OpenFolderCommand(UbuntuRepo, _launcher).Invoke();

        Assert.Equal(@"explorer.exe \\wsl.localhost\Ubuntu\home\me\my foo", Assert.Single(_launcher.Launches));
    }

    [Fact]
    public void Copy_path_names()
    {
        Assert.Equal("Copy path", new CopyRepoPathCommand(UbuntuRepo).Name);
        Assert.Equal("Copy Windows path", new CopyRepoPathCommand(UbuntuRepo, windowsPath: true).Name);
    }
}
