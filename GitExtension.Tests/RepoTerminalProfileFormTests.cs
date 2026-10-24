// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;
using GitExtension.Models;
using GitExtension.Pages;
using Microsoft.CommandPalette.Extensions;
using static GitExtension.Tests.Profiles;

namespace GitExtension.Tests;

public class RepoTerminalProfileFormTests : TestEnvironment
{
    private static readonly GitRepoInfo UbuntuRepo = new("foo", @"\\wsl.localhost\Ubuntu\home\me\foo");

    public RepoTerminalProfileFormTests()
    {
        SetTerminalProfiles(UbuntuApp, UbuntuGenerated, PowerShell, Cmd);
    }

    private static JsonNode Card(RepoTerminalProfileForm form) => JsonNode.Parse(form.TemplateJson)!;

    private static JsonNode ProfileInput(RepoTerminalProfileForm form) =>
        Card(form)["body"]!.AsArray().Single(n => (string?)n!["id"] == "profile")!;

    private static string[] ChoiceTitles(RepoTerminalProfileForm form) =>
        [.. ProfileInput(form)["choices"]!.AsArray().Select(c => (string)c!["title"]!)];

    private static string SelectedValue(RepoTerminalProfileForm form) => (string)ProfileInput(form)["value"]!;

    private static string[] Texts(RepoTerminalProfileForm form) =>
        [.. Card(form)["body"]!.AsArray().Where(n => (string?)n!["type"] == "TextBlock").Select(n => (string)n!["text"]!)];

    private static string Payload(string value) => new JsonObject { ["profile"] = value }.ToJsonString();

    [Fact]
    public void Lists_default_then_visible_profiles()
    {
        var form = new RepoTerminalProfileForm(UbuntuRepo, NewSettings(), wholeDistro: false);

        Assert.Equal(["Use default (Ubuntu)", "Ubuntu", "Windows PowerShell", "Command Prompt"], ChoiceTitles(form));
        Assert.Equal("default", SelectedValue(form));
    }

    [Fact]
    public void Repo_form_starts_on_the_repo_choice_and_names_the_distro_choice_as_default()
    {
        var settings = NewSettings();
        settings.SetDistroTerminalProfile("Ubuntu", PowerShellId);
        settings.SetRepoTerminalProfile(UbuntuRepo.ChoiceKey, CmdId);

        var form = new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: false);

        Assert.Equal(CmdId, SelectedValue(form));
        Assert.Equal("Use default (Windows PowerShell)", ChoiceTitles(form)[0]);
    }

    [Fact]
    public void Distro_form_starts_on_the_distro_choice_and_names_the_app_profile_as_default()
    {
        var settings = NewSettings();
        settings.SetDistroTerminalProfile("Ubuntu", PowerShellId);
        settings.SetRepoTerminalProfile(UbuntuRepo.ChoiceKey, CmdId);

        var form = new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: true);

        Assert.Equal(PowerShellId, SelectedValue(form));
        Assert.Equal("Use default (Ubuntu)", ChoiceTitles(form)[0]);
        Assert.Contains("foo has its own profile (Command Prompt), so this won't change it.", Texts(form));
    }

    [Fact]
    public void Distro_form_has_no_note_without_a_repo_choice()
    {
        var form = new RepoTerminalProfileForm(UbuntuRepo, NewSettings(), wholeDistro: true);

        Assert.Single(Texts(form));
    }

    [Fact]
    public void Choice_of_a_removed_profile_shows_as_default()
    {
        var settings = NewSettings();
        settings.SetRepoTerminalProfile(UbuntuRepo.ChoiceKey, "{99999999-9999-9999-9999-999999999999}");

        Assert.Equal("default", SelectedValue(new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: false)));
    }

    [Fact]
    public void Default_names_Terminals_default_when_nothing_else_applies()
    {
        SetTerminalProfiles(PowerShell);
        var form = new RepoTerminalProfileForm(new GitRepoInfo("foo", @"C:\foo"), NewSettings(), wholeDistro: false);

        Assert.Equal("Use default (Windows Terminal's default)", ChoiceTitles(form)[0]);
    }

    [Fact]
    public void Profile_names_are_escaped_in_the_card()
    {
        SetTerminalProfiles(Other("{a1000000-0000-0000-0000-000000000000}", """Quote \" and {braces}"""));

        var form = new RepoTerminalProfileForm(UbuntuRepo, NewSettings(), wholeDistro: false);

        Assert.Contains("""Quote " and {braces}""", ChoiceTitles(form));
    }

    [Fact]
    public void Saving_the_repo_form_sets_only_the_repo_choice()
    {
        var settings = NewSettings();
        settings.SetDistroTerminalProfile("Ubuntu", PowerShellId);

        var result = new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: false).SubmitForm(Payload(CmdId));

        Assert.Equal(CommandResultKind.GoBack, result.Kind);
        Assert.Equal(CmdId, settings.GetRepoTerminalProfile(UbuntuRepo.ChoiceKey));
        Assert.Equal(PowerShellId, settings.GetDistroTerminalProfile("Ubuntu"));
    }

    [Fact]
    public void Saving_the_distro_form_sets_only_the_distro_choice()
    {
        var settings = NewSettings();
        settings.SetRepoTerminalProfile(UbuntuRepo.ChoiceKey, CmdId);

        new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: true).SubmitForm(Payload(PowerShellId));

        Assert.Equal(PowerShellId, settings.GetDistroTerminalProfile("Ubuntu"));
        Assert.Equal(CmdId, settings.GetRepoTerminalProfile(UbuntuRepo.ChoiceKey));
    }

    [Fact]
    public void Saving_default_clears_the_choice()
    {
        var settings = NewSettings();
        settings.SetDistroTerminalProfile("Ubuntu", PowerShellId);

        new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: true).SubmitForm(Payload("default"));

        Assert.Null(settings.GetDistroTerminalProfile("Ubuntu"));
    }

    [Fact]
    public void Empty_submission_changes_nothing()
    {
        var settings = NewSettings();
        settings.SetRepoTerminalProfile(UbuntuRepo.ChoiceKey, CmdId);

        var result = new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: false).SubmitForm("{}");

        Assert.Equal(CommandResultKind.GoBack, result.Kind);
        Assert.Equal(CmdId, settings.GetRepoTerminalProfile(UbuntuRepo.ChoiceKey));
    }

    [Fact]
    public void Failed_save_shows_a_toast()
    {
        var settings = NewSettings();
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "settings", "GitExtension.terminalProfiles.json"));

        var result = new RepoTerminalProfileForm(UbuntuRepo, settings, wholeDistro: false).SubmitForm(Payload(CmdId));

        Assert.Equal(CommandResultKind.ShowToast, result.Kind);
    }

    [Fact]
    public void Page_names()
    {
        Assert.Equal("Set terminal profile…", new RepoTerminalProfilePage(UbuntuRepo, NewSettings()).Name);
        Assert.Equal("Set terminal profile for all Ubuntu repos…", new RepoTerminalProfilePage(UbuntuRepo, NewSettings(), wholeDistro: true).Name);
    }
}
