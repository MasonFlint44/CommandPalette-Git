// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GitExtension.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Settings;

public sealed class GitRepoSettingsManager : JsonSettingsManager
{
    private static readonly string Namespace = "gitRepos";

    private static string Namespaced(string propertyName) => $"{Namespace}.{propertyName}";

    private readonly TextSetting _scanPaths = new(
        Namespaced("scanPaths"),
        "Scan Paths",
        "Semicolon-separated list of root directories to scan for git repositories (e.g. D:\\Projects;C:\\Users\\me\\repos). For WSL, use the distro's network path (e.g. \\\\wsl.localhost\\Ubuntu\\home\\me\\git)",
        string.Empty);

    private static readonly List<ChoiceSetSetting.Choice> DepthChoices =
    [
        new("2", "2"),
        new("3", "3"),
        new("4", "4"),
        new("5", "5"),
        new("6", "6"),
    ];

    private readonly ChoiceSetSetting _maxDepth = new(
        Namespaced("maxDepth"),
        "Max Scan Depth",
        "Maximum directory depth to search for .git folders",
        DepthChoices)
    {
        Value = "3",
    };

    public const string OpenFolderAction = "open_folder";
    public const string OpenTerminalAction = "open_terminal";
    public const string OpenVSCodeAction = "open_vscode";

    private static readonly List<ChoiceSetSetting.Choice> DefaultActionChoices =
    [
        new("Open folder", OpenFolderAction),
        new("Open in terminal", OpenTerminalAction),
        new("Open in VS Code", OpenVSCodeAction),
    ];

    private readonly ChoiceSetSetting _defaultAction = new(
        Namespaced("defaultAction"),
        "Default Action",
        "Action performed when pressing Enter on a repository",
        DefaultActionChoices)
    {
        Value = OpenFolderAction,
    };

    public const string DefaultTerminalProfile = "default";

    // Choices are read from Windows Terminal's settings when the extension starts
    private readonly ChoiceSetSetting _terminalProfile = new(
        Namespaced("terminalProfile"),
        "Terminal Profile",
        "Windows Terminal profile to open repositories with. WSL repositories use the profile named after their distro when there is one, and each repository can choose its own from its context menu",
        [
            new("Windows Terminal default", DefaultTerminalProfile),
            .. TerminalProfiles.Load().Select(p => new ChoiceSetSetting.Choice(p.Name, p.Id)),
        ])
    {
        Value = DefaultTerminalProfile,
    };

    // Per-repository profile choices (repo path -> profile GUID), kept in their own file
    // because the settings file only holds the settings declared above.
    private readonly Dictionary<string, string> _repoTerminalProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _repoTerminalProfilesLock = new();

    public string ScanPaths => _scanPaths.Value ?? string.Empty;

    public int MaxDepth => int.TryParse(_maxDepth.Value, out var depth) ? depth : 3;

    public string DefaultAction => _defaultAction.Value ?? OpenFolderAction;

    public string TerminalProfile => _terminalProfile.Value ?? DefaultTerminalProfile;

    public string? GetRepoTerminalProfile(string repoPath)
    {
        lock (_repoTerminalProfilesLock)
        {
            return _repoTerminalProfiles.GetValueOrDefault(repoPath);
        }
    }

    /// <summary>
    /// Sets the profile a repository opens with, or clears it when <paramref name="profileId"/> is null.
    /// Returns false if the choice couldn't be saved.
    /// </summary>
    public bool SetRepoTerminalProfile(string repoPath, string? profileId)
    {
        lock (_repoTerminalProfilesLock)
        {
            if (profileId is null)
            {
                _repoTerminalProfiles.Remove(repoPath);
            }
            else
            {
                _repoTerminalProfiles[repoPath] = profileId;
            }

            try
            {
                using var stream = File.Create(RepoProfilesJsonPath());
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                foreach (var (path, id) in _repoTerminalProfiles)
                {
                    writer.WriteString(path, id);
                }

                writer.WriteEndObject();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private void LoadRepoTerminalProfiles()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(RepoProfilesJsonPath()));
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String)
                {
                    _repoTerminalProfiles[entry.Name] = entry.Value.GetString()!;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // No choices saved yet, or the file is unreadable: every repo uses the default
        }
    }

    /// <summary>
    /// Programmatically updates the scan paths setting and persists to disk.
    /// We can't use Settings.RaiseSettingsChanged() (it's internal to the toolkit),
    /// so we save directly and fire our own event for listeners.
    /// </summary>
    public event EventHandler? ScanPathsUpdated;

    public void UpdateScanPaths(string paths)
    {
        var key = Namespaced("scanPaths");
        var json = $"{{\"{key}\":\"{paths.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}";
        Settings.Update(json);
        SaveSettings();
        ScanPathsUpdated?.Invoke(this, EventArgs.Empty);
    }

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "GitExtension.settings.json");
    }

    private static string RepoProfilesJsonPath() =>
        Path.Combine(Path.GetDirectoryName(SettingsJsonPath())!, "GitExtension.repoProfiles.json");

    public GitRepoSettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_scanPaths);
        Settings.Add(_maxDepth);
        Settings.Add(_defaultAction);
        Settings.Add(_terminalProfile);

        LoadSettings();
        LoadRepoTerminalProfiles();

        Settings.SettingsChanged += (s, a) => SaveSettings();
    }
}
