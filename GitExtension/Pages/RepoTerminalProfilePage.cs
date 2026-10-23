// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Text.Json.Nodes;
using GitExtension.Models;
using GitExtension.Services;
using GitExtension.Settings;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Pages;

/// <summary>
/// Lets the user pick the Windows Terminal profile a repository opens with, or for a WSL
/// repository, the profile every repository in its distro opens with.
/// </summary>
internal sealed partial class RepoTerminalProfilePage : ContentPage
{
    private readonly GitRepoInfo _repo;
    private readonly GitRepoSettingsManager _settingsManager;

    public RepoTerminalProfilePage(GitRepoInfo repo, GitRepoSettingsManager settingsManager)
    {
        _repo = repo;
        _settingsManager = settingsManager;
        Name = "Set terminal profile…";
        Title = $"Terminal profile for {repo.Name}";
        Icon = new IconInfo(""); // CommandPrompt icon
    }

    // Built on each visit so the profile list and current choice are up to date
    public override IContent[] GetContent() => [new RepoTerminalProfileForm(_repo, _settingsManager)];
}

internal sealed partial class RepoTerminalProfileForm : FormContent
{
    private const string UseDefault = "default";
    private const string ThisRepo = "repo";
    private const string WholeDistro = "distro";

    private readonly GitRepoInfo _repo;
    private readonly GitRepoSettingsManager _settingsManager;

    public RepoTerminalProfileForm(GitRepoInfo repo, GitRepoSettingsManager settingsManager)
    {
        _repo = repo;
        _settingsManager = settingsManager;

        var profiles = TerminalProfiles.Load();
        var current = TerminalProfiles.Resolve(repo, settingsManager, profiles);
        var repoChoice = settingsManager.GetRepoTerminalProfile(repo.ChoiceKey);

        var choices = new JsonArray(new JsonObject { ["title"] = "Use default", ["value"] = UseDefault });
        foreach (var profile in profiles.Where(p => !p.Hidden))
        {
            choices.Add((JsonNode)new JsonObject { ["title"] = profile.Label, ["value"] = profile.Id });
        }

        // Built as JSON nodes so profile names are escaped properly
        var body = new JsonArray(
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = $"{repo.Name} currently opens with: {current?.Label ?? "Windows Terminal's default profile"}",
                ["wrap"] = true,
            },
            new JsonObject
            {
                ["type"] = "Input.ChoiceSet",
                ["id"] = "profile",
                ["label"] = "Terminal profile",
                ["style"] = "compact",
                ["value"] = profiles.Any(p => p.Id == repoChoice) ? repoChoice : UseDefault,
                ["choices"] = choices,
            });

        if (repo.IsWsl)
        {
            body.Add((JsonNode)new JsonObject
            {
                ["type"] = "Input.ChoiceSet",
                ["id"] = "scope",
                ["label"] = "Apply to",
                ["style"] = "expanded",
                ["value"] = ThisRepo,
                ["choices"] = new JsonArray(
                    new JsonObject { ["title"] = "This repository", ["value"] = ThisRepo },
                    new JsonObject { ["title"] = $"All {repo.Distro} repositories", ["value"] = WholeDistro }),
            });
        }

        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.6",
            ["body"] = body,
            ["actions"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "Action.Submit",
                    ["title"] = "Save",
                    ["data"] = new JsonObject(),
                }),
        };
        TemplateJson = card.ToJsonString();
    }

    public override CommandResult SubmitForm(string payload)
    {
        var inputs = JsonNode.Parse(payload);
        var profile = inputs?["profile"]?.ToString();
        if (string.IsNullOrEmpty(profile))
        {
            return CommandResult.GoBack();
        }

        var profileId = profile == UseDefault ? null : profile;
        bool saved;
        if (_repo.IsWsl && inputs?["scope"]?.ToString() == WholeDistro)
        {
            // Also drop this repository's own choice, so the distro-wide one takes effect here
            saved = _settingsManager.SetDistroTerminalProfile(_repo.Distro!, profileId)
                && _settingsManager.SetRepoTerminalProfile(_repo.ChoiceKey, null);
        }
        else
        {
            saved = _settingsManager.SetRepoTerminalProfile(_repo.ChoiceKey, profileId);
        }

        return saved
            ? CommandResult.GoBack()
            : CommandResult.ShowToast("Couldn't save the terminal profile");
    }
}
