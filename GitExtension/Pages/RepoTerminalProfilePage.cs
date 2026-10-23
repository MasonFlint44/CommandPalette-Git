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
/// Lets the user pick the Windows Terminal profile a single repository opens with.
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

    private readonly GitRepoInfo _repo;
    private readonly GitRepoSettingsManager _settingsManager;

    public RepoTerminalProfileForm(GitRepoInfo repo, GitRepoSettingsManager settingsManager)
    {
        _repo = repo;
        _settingsManager = settingsManager;

        var profiles = TerminalProfiles.Load();
        var fallback = TerminalProfiles.Resolve(repo, settingsManager, profiles, includeRepoChoice: false);
        var current = settingsManager.GetRepoTerminalProfile(repo.FullPath);

        var choices = new JsonArray(new JsonObject
        {
            ["title"] = $"Use default ({fallback?.Name ?? "Windows Terminal default"})",
            ["value"] = UseDefault,
        });
        foreach (var profile in profiles)
        {
            choices.Add((JsonNode)new JsonObject { ["title"] = profile.Name, ["value"] = profile.Id });
        }

        // Built as JSON nodes so profile names are escaped properly
        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.6",
            ["body"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = $"Choose the Windows Terminal profile {repo.Name} opens with.",
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "Input.ChoiceSet",
                    ["id"] = "profile",
                    ["label"] = "Terminal profile",
                    ["style"] = "compact",
                    ["value"] = profiles.Any(p => p.Id == current) ? current : UseDefault,
                    ["choices"] = choices,
                }),
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

        var saved = _settingsManager.SetRepoTerminalProfile(_repo.FullPath, profile == UseDefault ? null : profile);
        return saved
            ? CommandResult.GoBack()
            : CommandResult.ShowToast("Couldn't save the terminal profile");
    }
}
