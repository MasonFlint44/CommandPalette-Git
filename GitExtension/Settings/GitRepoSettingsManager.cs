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
        new("Open in VS Code", OpenVSCodeAction),
        new("Open in terminal", OpenTerminalAction),
        new("Open folder", OpenFolderAction),
    ];

    private readonly ChoiceSetSetting _defaultAction = new(
        Namespaced("defaultAction"),
        "Default Action",
        "Action performed when pressing Enter on a repository. Ctrl+Enter runs the next of VS Code, terminal and folder; the rest are in the context menu (Ctrl+K)",
        DefaultActionChoices)
    {
        Value = OpenVSCodeAction,
    };

    public const string DefaultTerminalProfile = "default";

    // Choices are read from Windows Terminal's settings when the extension starts
    private readonly ChoiceSetSetting _terminalProfile = new(
        Namespaced("terminalProfile"),
        "Terminal Profile",
        "Windows Terminal profile to open Windows repositories with. WSL repositories use their distro app's profile (e.g. Ubuntu's) or the one Terminal generated for the distro, falling back to this only if neither exists. Each repository or distro can choose its own from a repository's context menu",
        [
            new("Windows Terminal default", DefaultTerminalProfile),
            .. TerminalProfiles.Load().Where(p => !p.Hidden).Select(p => new ChoiceSetSetting.Choice(p.Label, p.Id)),
        ])
    {
        Value = DefaultTerminalProfile,
    };

    // Per-repository (GitRepoInfo.ChoiceKey -> profile GUID) and per-distro (distro -> profile GUID)
    // profile choices, kept in their own file because the settings file only holds the
    // settings declared above.
    private readonly Dictionary<string, string> _repoTerminalProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _distroTerminalProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _terminalProfileChoicesLock = new();

    public string ScanPaths => _scanPaths.Value ?? string.Empty;

    public int MaxDepth => int.TryParse(_maxDepth.Value, out var depth) ? depth : 3;

    public string DefaultAction => _defaultAction.Value ?? OpenVSCodeAction;

    public string TerminalProfile => _terminalProfile.Value ?? DefaultTerminalProfile;

    /// <summary>How often and how recently each repository was opened, for ordering the list.</summary>
    internal RepoHistory History { get; }

    public string? GetRepoTerminalProfile(string repoKey)
    {
        lock (_terminalProfileChoicesLock)
        {
            return _repoTerminalProfiles.GetValueOrDefault(repoKey);
        }
    }

    public string? GetDistroTerminalProfile(string distro)
    {
        lock (_terminalProfileChoicesLock)
        {
            return _distroTerminalProfiles.GetValueOrDefault(distro);
        }
    }

    /// <summary>
    /// Sets the profile a repository opens with, or clears it when <paramref name="profileId"/> is null.
    /// Returns false if the choice couldn't be saved.
    /// </summary>
    public bool SetRepoTerminalProfile(string repoKey, string? profileId) =>
        SetTerminalProfileChoice(_repoTerminalProfiles, repoKey, profileId);

    /// <summary>
    /// Sets the profile a WSL distro's repositories open with, or clears it when
    /// <paramref name="profileId"/> is null. Returns false if the choice couldn't be saved.
    /// </summary>
    public bool SetDistroTerminalProfile(string distro, string? profileId) =>
        SetTerminalProfileChoice(_distroTerminalProfiles, distro, profileId);

    private bool SetTerminalProfileChoice(Dictionary<string, string> choices, string key, string? profileId)
    {
        lock (_terminalProfileChoicesLock)
        {
            if (profileId is null)
            {
                choices.Remove(key);
            }
            else
            {
                choices[key] = profileId;
            }

            try
            {
                using var stream = File.Create(_terminalProfileChoicesPath);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                WriteChoices(writer, "repos", _repoTerminalProfiles);
                WriteChoices(writer, "distros", _distroTerminalProfiles);
                writer.WriteEndObject();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        static void WriteChoices(Utf8JsonWriter writer, string name, Dictionary<string, string> choices)
        {
            writer.WriteStartObject(name);
            foreach (var (key, id) in choices)
            {
                writer.WriteString(key, id);
            }

            writer.WriteEndObject();
        }
    }

    private void LoadTerminalProfileChoices()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_terminalProfileChoicesPath));
            ReadChoices(document.RootElement, "repos", _repoTerminalProfiles);
            ReadChoices(document.RootElement, "distros", _distroTerminalProfiles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // No choices saved yet, or the file is unreadable: everything uses the defaults
        }

        static void ReadChoices(JsonElement root, string name, Dictionary<string, string> choices)
        {
            if (root.TryGetProperty(name, out var section) && section.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in section.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                    {
                        choices[entry.Name] = entry.Value.GetString()!;
                    }
                }
            }
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

    private readonly string _terminalProfileChoicesPath;

    public GitRepoSettingsManager()
        : this(Utilities.BaseSettingsPath("Microsoft.CmdPal"))
    {
    }

    /// <summary>Keeps the settings files in <paramref name="directory"/>; tests use a temporary one.</summary>
    internal GitRepoSettingsManager(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "GitExtension.settings.json");
        _terminalProfileChoicesPath = Path.Combine(directory, "GitExtension.terminalProfiles.json");
        History = new RepoHistory(Path.Combine(directory, "GitExtension.history.json"));

        Settings.Add(_scanPaths);
        Settings.Add(_maxDepth);
        Settings.Add(_defaultAction);
        Settings.Add(_terminalProfile);

        LoadSettings();
        LoadTerminalProfileChoices();

        Settings.SettingsChanged += (s, a) => SaveSettings();
    }
}
