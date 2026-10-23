// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using GitExtension.Models;
using GitExtension.Services;
using GitExtension.Settings;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Pages;

/// <summary>
/// Lets the user pick the Windows Terminal profile a repository opens with or, with
/// <c>wholeDistro</c>, the profile every repository in a WSL repository's distro opens with.
/// </summary>
internal sealed partial class RepoTerminalProfilePage : ContentPage
{
    private readonly GitRepoInfo _repo;
    private readonly GitRepoSettingsManager _settingsManager;
    private readonly bool _wholeDistro;

    public RepoTerminalProfilePage(GitRepoInfo repo, GitRepoSettingsManager settingsManager, bool wholeDistro = false)
    {
        _repo = repo;
        _settingsManager = settingsManager;
        _wholeDistro = wholeDistro;
        Name = wholeDistro ? $"Set terminal profile for all {repo.Distro} repos…" : "Set terminal profile…";
        Title = wholeDistro ? $"Terminal profile for {repo.Distro} repositories" : $"Terminal profile for {repo.Name}";
        Icon = new IconInfo(""); // CommandPrompt icon
    }

    // Built on each visit so the profile list and current choice are up to date
    public override IContent[] GetContent() => [new RepoTerminalProfileForm(_repo, _settingsManager, _wholeDistro)];
}

internal sealed partial class RepoTerminalProfileForm : FormContent
{
    private const string UseDefault = "default";

    private readonly GitRepoInfo _repo;
    private readonly GitRepoSettingsManager _settingsManager;
    private readonly bool _wholeDistro;

    public RepoTerminalProfileForm(GitRepoInfo repo, GitRepoSettingsManager settingsManager, bool wholeDistro)
    {
        _repo = repo;
        _settingsManager = settingsManager;
        _wholeDistro = wholeDistro;

        var profiles = TerminalProfiles.Load();
        var choice = wholeDistro
            ? settingsManager.GetDistroTerminalProfile(repo.Distro!)
            : settingsManager.GetRepoTerminalProfile(repo.ChoiceKey);
        var fallback = wholeDistro
            ? TerminalProfiles.DistroDefault(repo.Distro!, settingsManager, profiles)
            : TerminalProfiles.RepoDefault(repo, settingsManager, profiles);

        var choices = new JsonArray(new JsonObject
        {
            ["title"] = $"Use default ({fallback?.Label ?? "Windows Terminal's default"})",
            ["value"] = UseDefault,
        });
        foreach (var profile in profiles.Where(p => !p.Hidden))
        {
            choices.Add((JsonNode)new JsonObject { ["title"] = profile.Label, ["value"] = profile.Id });
        }

        // Built as JSON nodes so profile names are escaped properly
        var body = new List<JsonNode>
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = wholeDistro
                    ? $"Choose the Windows Terminal profile {repo.Distro} repositories open with. Repositories with their own profile keep it."
                    : $"Choose the Windows Terminal profile {repo.Name} opens with.",
                ["wrap"] = true,
            },
        };

        var repoOverride = TerminalProfiles.ById(profiles, settingsManager.GetRepoTerminalProfile(repo.ChoiceKey));
        if (wholeDistro && repoOverride is not null)
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = $"{repo.Name} has its own profile ({repoOverride.Label}), so this won't change it.",
                ["wrap"] = true,
                ["isSubtle"] = true,
            });
        }

        body.Add(new JsonObject
        {
            ["type"] = "Input.ChoiceSet",
            ["id"] = "profile",
            ["label"] = "Terminal profile",
            ["style"] = "compact",
            ["value"] = profiles.Any(p => p.Id == choice) ? choice : UseDefault,
            ["choices"] = choices,
        });

        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.6",
            ["body"] = new JsonArray([.. body]),
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
        var profile = JsonNode.Parse(payload)?["profile"]?.ToString();
        if (string.IsNullOrEmpty(profile))
        {
            return CommandResult.GoBack();
        }

        var profileId = profile == UseDefault ? null : profile;
        var saved = _wholeDistro
            ? _settingsManager.SetDistroTerminalProfile(_repo.Distro!, profileId)
            : _settingsManager.SetRepoTerminalProfile(_repo.ChoiceKey, profileId);

        return saved
            ? CommandResult.GoBack()
            : CommandResult.ShowToast("Couldn't save the terminal profile");
    }
}
