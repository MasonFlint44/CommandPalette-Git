// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitExtension.Models;
using GitExtension.Settings;

namespace GitExtension.Services;

/// <summary>
/// A Windows Terminal profile. <see cref="Id"/> is its GUID ("{...}"); <see cref="Label"/> is
/// its name, with a suffix when several profiles share that name.
/// </summary>
internal sealed record TerminalProfile(string Id, string Name, string Label, bool Hidden);

/// <summary>
/// Reads profiles from Windows Terminal's settings and picks the one a repository opens with.
/// </summary>
internal static class TerminalProfiles
{
    private const string GeneratedWslSource = "Windows.Terminal.Wsl";

    // Windows Terminal's namespace for the GUIDs of profiles it generates
    private static readonly Guid TerminalProfileNamespace = new("2BDE4A90-D05F-401C-9492-E40884EAD1D8");

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages",
        "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        "LocalState",
        "settings.json");

    /// <summary>
    /// All profiles in Windows Terminal's settings, hidden ones included, in settings order.
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

        var entries = new List<(string Id, string Name, bool Hidden, bool GeneratedWsl)>();
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

            foreach (var profile in profiles.EnumerateArray())
            {
                var name = GetString(profile, "name")?.Trim();
                if (!string.IsNullOrEmpty(name) && Guid.TryParse(GetString(profile, "guid"), out var id))
                {
                    var hidden = profile.TryGetProperty("hidden", out var hiddenElement) && hiddenElement.ValueKind == JsonValueKind.True;
                    entries.Add((id.ToString("B"), name, hidden, GetString(profile, "source") == GeneratedWslSource));
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        // Profiles sharing a name are told apart: the one Terminal generated for a WSL distro
        // becomes "Name (WSL)"; if one other remains it keeps the plain name, otherwise the
        // others are numbered "Name (1)", "Name (2)", ... in settings order.
        var result = new List<TerminalProfile>();
        var numbered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var sameName = entries.Where(e => string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var others = sameName.Count(e => !e.GeneratedWsl);

            string label;
            if (sameName.Count == 1)
            {
                label = entry.Name;
            }
            else if (entry.GeneratedWsl)
            {
                label = $"{entry.Name} (WSL)";
            }
            else if (others == 1)
            {
                label = entry.Name;
            }
            else
            {
                numbered[entry.Name] = numbered.GetValueOrDefault(entry.Name) + 1;
                label = $"{entry.Name} ({numbered[entry.Name]})";
            }

            result.Add(new TerminalProfile(entry.Id, entry.Name, label, entry.Hidden));
        }

        return result;
    }

    /// <summary>
    /// The profile a repository opens with: its own choice, then for WSL repositories the
    /// distro's choice and the profile Terminal generated for the distro, then the global
    /// setting. Null means Windows Terminal's default profile. Choices that no longer match a
    /// profile are skipped.
    /// </summary>
    public static TerminalProfile? Resolve(GitRepoInfo repo, GitRepoSettingsManager settings, List<TerminalProfile> profiles, bool includeRepoChoice = true)
    {
        TerminalProfile? ById(string? id) => profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        return (includeRepoChoice ? ById(settings.GetRepoTerminalProfile(repo.FullPath)) : null)
            ?? (repo.IsWsl ? ById(settings.GetDistroTerminalProfile(repo.Distro!)) ?? ById(GeneratedWslProfileId(repo.Distro!)) : null)
            ?? ById(settings.TerminalProfile);
    }

    /// <summary>
    /// The GUID Windows Terminal gives the profile it generates for a WSL distro: a version 5
    /// UUID of the distro name (UTF-16LE) in Terminal's profile namespace.
    /// </summary>
    public static string GeneratedWslProfileId(string distro)
    {
        var input = TerminalProfileNamespace.ToByteArray(bigEndian: true).Concat(Encoding.Unicode.GetBytes(distro)).ToArray();
#pragma warning disable CA5350 // SHA-1 is what the UUID v5 algorithm specifies; this isn't a security use
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true).ToString("B");
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
