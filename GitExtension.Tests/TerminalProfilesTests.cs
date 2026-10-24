// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Models;
using GitExtension.Services;
using static GitExtension.Tests.Profiles;

namespace GitExtension.Tests;

public class TerminalProfileLoadTests : TestEnvironment
{
    [Theory]
    [InlineData("Ubuntu", "{2c4de342-38b7-51cf-b940-2309a097f518}")]
    [InlineData("wsl-vpnkit", "{e636fd95-8db6-56fc-969f-5647553fa92b}")]
    public void Generated_profile_ID_matches_Windows_Terminal(string distro, string id)
    {
        Assert.Equal(id, TerminalProfiles.GeneratedWslProfileId(distro));
    }

    [Fact]
    public void Missing_or_broken_settings_give_no_profiles()
    {
        Assert.Empty(TerminalProfiles.Load());

        File.WriteAllText(TerminalProfiles.SettingsPath, "{ not json");
        Assert.Empty(TerminalProfiles.Load());

        File.WriteAllText(TerminalProfiles.SettingsPath, """{ "profiles": "nope" }""");
        Assert.Empty(TerminalProfiles.Load());
    }

    [Fact]
    public void Reads_settings_with_comments_trailing_commas_and_the_old_array_form()
    {
        File.WriteAllText(TerminalProfiles.SettingsPath, $$"""
            // Windows Terminal settings
            {
              "profiles": [
                {{PowerShell}}, /* the default */
              ],
            }
            """);

        Assert.Equal(["Windows PowerShell"], TerminalProfiles.Load().Select(p => p.Name));
    }

    [Fact]
    public void Reads_kind_hidden_and_command_line()
    {
        SetTerminalProfiles(
            UbuntuApp,
            UbuntuGenerated,
            PowerShell,
            Store("{22222222-2222-2222-2222-222222222222}", "Custom Store", commandLine: "custom.exe"),
            Other("{33333333-3333-3333-3333-333333333333}", "Blank command", commandLine: "  "));

        var profiles = TerminalProfiles.Load();

        Assert.Equal(
            [
                (UbuntuAppId, TerminalProfileKind.StoreApp, false, false, true),
                (UbuntuGeneratedId, TerminalProfileKind.GeneratedWsl, true, false, true),
                (PowerShellId, TerminalProfileKind.Other, false, false, false),
                ("{22222222-2222-2222-2222-222222222222}", TerminalProfileKind.StoreApp, false, true, false),
                ("{33333333-3333-3333-3333-333333333333}", TerminalProfileKind.Other, false, false, false),
            ],
            profiles.Select(p => (p.Id, p.Kind, p.Hidden, p.HasCommandLine, p.IsDistroLauncher)));
    }

    [Fact]
    public void Skips_profiles_without_a_name_or_GUID()
    {
        SetTerminalProfiles(
            """{ "name": "No GUID" }""",
            """{ "guid": "{44444444-4444-4444-4444-444444444444}" }""",
            """{ "guid": "not a guid", "name": "Bad GUID" }""",
            PowerShell);

        Assert.Equal(["Windows PowerShell"], TerminalProfiles.Load().Select(p => p.Name));
    }

    [Fact]
    public void Normalises_GUID_case_and_format()
    {
        SetTerminalProfiles("""{ "guid": "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "name": "Upper" }""");

        Assert.Equal("{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}", TerminalProfiles.Load().Single().Id);
    }

    [Fact]
    public void Unique_names_are_left_alone()
    {
        SetTerminalProfiles(PowerShell, Cmd);

        Assert.Equal(["Windows PowerShell", "Command Prompt"], TerminalProfiles.Load().Select(p => p.Label));
    }

    [Fact]
    public void Generated_profile_sharing_a_name_gets_WSL_suffix()
    {
        // Counts hidden profiles too, so labels don't change when one is unhidden
        SetTerminalProfiles(UbuntuApp, UbuntuGenerated);

        Assert.Equal(["Ubuntu", "Ubuntu (WSL)"], TerminalProfiles.Load().Select(p => p.Label));
    }

    [Fact]
    public void Several_others_sharing_a_name_are_numbered()
    {
        SetTerminalProfiles(
            UbuntuApp,
            UbuntuGenerated,
            Other("{55555555-5555-5555-5555-555555555555}", "ubuntu"));

        Assert.Equal(["Ubuntu (1)", "Ubuntu (WSL)", "ubuntu (2)"], TerminalProfiles.Load().Select(p => p.Label));
    }

    [Fact]
    public void Two_others_sharing_a_name_without_a_generated_one_are_numbered()
    {
        SetTerminalProfiles(
            Other("{55555555-5555-5555-5555-555555555555}", "Dev"),
            Other("{66666666-6666-6666-6666-666666666666}", "Dev"));

        Assert.Equal(["Dev (1)", "Dev (2)"], TerminalProfiles.Load().Select(p => p.Label));
    }
}

public class TerminalProfileResolveTests : TestEnvironment
{
    private static readonly GitRepoInfo WindowsRepo = new("foo", @"C:\code\foo");

    private static GitRepoInfo UbuntuRepo => new("foo", @"\\wsl.localhost\Ubuntu\home\me\foo");

    private string? Resolve(GitRepoInfo repo, Settings.GitRepoSettingsManager? settings = null) =>
        TerminalProfiles.Resolve(repo, settings ?? NewSettings(), TerminalProfiles.Load())?.Id;

    private static void SetGlobal(Settings.GitRepoSettingsManager settings, string id) =>
        settings.Settings.Update($$"""{ "gitRepos.terminalProfile": "{{id}}" }""");

    [Fact]
    public void Windows_repo_uses_its_choice_then_the_global_setting_then_Terminals_default()
    {
        SetTerminalProfiles(PowerShell, Cmd);
        var settings = NewSettings();

        Assert.Null(Resolve(WindowsRepo, settings));

        SetGlobal(settings, CmdId);
        Assert.Equal(CmdId, Resolve(WindowsRepo, settings));

        settings.SetRepoTerminalProfile(WindowsRepo.ChoiceKey, PowerShellId);
        Assert.Equal(PowerShellId, Resolve(WindowsRepo, settings));
    }

    [Fact]
    public void Choices_for_profiles_that_no_longer_exist_are_skipped()
    {
        SetTerminalProfiles(PowerShell);
        var settings = NewSettings();
        SetGlobal(settings, PowerShellId);
        settings.SetRepoTerminalProfile(WindowsRepo.ChoiceKey, "{99999999-9999-9999-9999-999999999999}");

        Assert.Equal(PowerShellId, Resolve(WindowsRepo, settings));
    }

    [Fact]
    public void WSL_repo_order_is_repo_then_distro_then_app_then_generated_then_global()
    {
        var settings = NewSettings();
        SetGlobal(settings, PowerShellId);

        SetTerminalProfiles(PowerShell);
        Assert.Equal(PowerShellId, Resolve(UbuntuRepo, settings));

        SetTerminalProfiles(PowerShell, UbuntuGenerated);
        Assert.Equal(UbuntuGeneratedId, Resolve(UbuntuRepo, settings));

        SetTerminalProfiles(PowerShell, UbuntuGenerated, UbuntuApp, Cmd);
        Assert.Equal(UbuntuAppId, Resolve(UbuntuRepo, settings));

        settings.SetDistroTerminalProfile("Ubuntu", CmdId);
        Assert.Equal(CmdId, Resolve(UbuntuRepo, settings));

        settings.SetRepoTerminalProfile(UbuntuRepo.ChoiceKey, UbuntuGeneratedId);
        Assert.Equal(UbuntuGeneratedId, Resolve(UbuntuRepo, settings));
    }

    [Fact]
    public void Distro_choice_matches_the_distro_case_insensitively()
    {
        SetTerminalProfiles(PowerShell, UbuntuApp);
        var settings = NewSettings();
        settings.SetDistroTerminalProfile("ubuntu", PowerShellId);

        Assert.Equal(PowerShellId, Resolve(UbuntuRepo, settings));
    }

    [Fact]
    public void Global_setting_doesnt_override_a_WSL_distros_own_profile()
    {
        SetTerminalProfiles(PowerShell, UbuntuGenerated);
        var settings = NewSettings();
        SetGlobal(settings, PowerShellId);

        Assert.Equal(UbuntuGeneratedId, Resolve(UbuntuRepo, settings));
    }

    [Fact]
    public void Hidden_app_profile_is_skipped_but_hidden_generated_profile_is_used()
    {
        SetTerminalProfiles(Store(UbuntuAppId, "Ubuntu", hidden: true), UbuntuGenerated);

        Assert.Equal(UbuntuGeneratedId, Resolve(UbuntuRepo));
    }
}

public class DistroAppProfileTests : TestEnvironment
{
    private string? AppProfileFor(string distro)
    {
        var profiles = TerminalProfiles.Load();
        var profile = TerminalProfiles.DistroDefault(distro, NewSettings(), profiles);
        return profile?.Kind == TerminalProfileKind.StoreApp ? profile.Name : null;
    }

    [Fact]
    public void Exact_name_matches()
    {
        SetTerminalProfiles(UbuntuApp);

        Assert.Equal("Ubuntu", AppProfileFor("Ubuntu"));
    }

    [Fact]
    public void Profile_name_may_go_on_after_the_distro_name()
    {
        SetDistros("Ubuntu-22.04", "openSUSE-Tumbleweed");
        SetTerminalProfiles(
            Store("{a1000000-0000-0000-0000-000000000000}", "Ubuntu 22.04.5 LTS"),
            Store("{a2000000-0000-0000-0000-000000000000}", "openSUSE Tumbleweed Edition"));

        Assert.Equal("Ubuntu 22.04.5 LTS", AppProfileFor("Ubuntu-22.04"));
        Assert.Equal("openSUSE Tumbleweed Edition", AppProfileFor("openSUSE-Tumbleweed"));
    }

    [Fact]
    public void Plain_distro_doesnt_take_a_versioned_app()
    {
        SetTerminalProfiles(Store("{a1000000-0000-0000-0000-000000000000}", "Ubuntu 22.04 LTS"));

        Assert.Null(AppProfileFor("Ubuntu"));
    }

    [Fact]
    public void Profile_goes_to_the_registered_distro_with_the_longest_matching_name()
    {
        SetDistros("Ubuntu", "Ubuntu-Preview", "Ubuntu-22.04");
        SetTerminalProfiles(
            Store("{a1000000-0000-0000-0000-000000000000}", "Ubuntu Preview"),
            Store("{a2000000-0000-0000-0000-000000000000}", "Ubuntu 22.04.5 LTS"),
            UbuntuApp);

        Assert.Equal("Ubuntu", AppProfileFor("Ubuntu"));
        Assert.Equal("Ubuntu Preview", AppProfileFor("Ubuntu-Preview"));
        Assert.Equal("Ubuntu 22.04.5 LTS", AppProfileFor("Ubuntu-22.04"));
    }

    [Fact]
    public void Distro_doesnt_take_a_profile_belonging_to_a_longer_registered_distro()
    {
        SetDistros("Ubuntu", "Ubuntu-Preview");
        SetTerminalProfiles(Store("{a1000000-0000-0000-0000-000000000000}", "Ubuntu Preview"));

        Assert.Null(AppProfileFor("Ubuntu"));
        Assert.Equal("Ubuntu Preview", AppProfileFor("Ubuntu-Preview"));
    }

    [Fact]
    public void Exact_match_wins_over_an_earlier_longer_name()
    {
        SetDistros("Debian");
        SetTerminalProfiles(
            Store("{a1000000-0000-0000-0000-000000000000}", "Debian Testing"),
            Store("{a2000000-0000-0000-0000-000000000000}", "Debian"));

        Assert.Equal("Debian", AppProfileFor("Debian"));
    }

    [Fact]
    public void Known_limitation_unregistered_preview_app_lends_its_look()
    {
        SetDistros("Ubuntu");
        SetTerminalProfiles(Store("{a1000000-0000-0000-0000-000000000000}", "Ubuntu Preview"));

        Assert.Equal("Ubuntu Preview", AppProfileFor("Ubuntu"));
    }

    [Fact]
    public void Only_visible_Store_profiles_are_matched()
    {
        SetTerminalProfiles(
            Other("{a1000000-0000-0000-0000-000000000000}", "Ubuntu"),
            Store("{a2000000-0000-0000-0000-000000000000}", "Ubuntu", hidden: true));

        Assert.Null(AppProfileFor("Ubuntu"));
    }

    [Fact]
    public void Store_profile_with_a_custom_command_line_is_still_matched()
    {
        SetTerminalProfiles(Store(UbuntuAppId, "Ubuntu", commandLine: "wsl.exe -d Ubuntu -- tmux"));

        Assert.Equal("Ubuntu", AppProfileFor("Ubuntu"));
    }

    [Fact]
    public void Distro_name_without_letters_or_digits_matches_nothing()
    {
        SetDistros("---");
        SetTerminalProfiles(UbuntuApp);

        Assert.Null(AppProfileFor("---"));
    }
}
