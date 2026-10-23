// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GitExtension.Models;
using GitExtension.Settings;

namespace GitExtension.Services;

/// <summary>A Windows Terminal profile, identified by its GUID ("{...}").</summary>
internal sealed record TerminalProfile(string Id, string Name);

/// <summary>
/// Reads profiles from Windows Terminal's settings and picks the one a repository opens with.
/// </summary>
internal static class TerminalProfiles
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages",
        "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        "LocalState",
        "settings.json");

    /// <summary>
    /// The visible profiles in Windows Terminal's settings, in settings order.
    /// Empty if Terminal isn't installed or its settings can't be read.
    /// </summary>
    public static List<TerminalProfile> Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            // "profiles" is either { "list": [...] } or, in older settings files, the array itself
            var profiles = document.RootElement.TryGetProperty("profiles", out var element) ? element : default;
            if (profiles.ValueKind == JsonValueKind.Object)
            {
                profiles = profiles.TryGetProperty("list", out var list) ? list : default;
            }

            if (profiles.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<TerminalProfile>();
            foreach (var profile in profiles.EnumerateArray())
            {
                var hidden = profile.TryGetProperty("hidden", out var hiddenElement) && hiddenElement.ValueKind == JsonValueKind.True;
                var name = profile.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
                var guid = profile.TryGetProperty("guid", out var guidElement) && guidElement.ValueKind == JsonValueKind.String ? guidElement.GetString() : null;

                if (!hidden && !string.IsNullOrWhiteSpace(name) && Guid.TryParse(guid, out var id))
                {
                    result.Add(new TerminalProfile(id.ToString("B"), name.Trim()));
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The profile a repository opens with: its own choice, then (for WSL) the profile named
    /// after its distro, then the global setting. Null means Windows Terminal's default profile.
    /// Choices that no longer match a profile are skipped.
    /// </summary>
    public static TerminalProfile? Resolve(GitRepoInfo repo, GitRepoSettingsManager settings, List<TerminalProfile> profiles, bool includeRepoChoice = true)
    {
        TerminalProfile? ById(string? id) => profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        return (includeRepoChoice ? ById(settings.GetRepoTerminalProfile(repo.FullPath)) : null)
            ?? (repo.IsWsl ? profiles.FirstOrDefault(p => string.Equals(p.Name, repo.Distro, StringComparison.OrdinalIgnoreCase)) : null)
            ?? ById(settings.TerminalProfile);
    }
}
