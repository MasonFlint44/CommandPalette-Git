// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GitExtension.Models;

namespace GitExtension.Services;

/// <summary>
/// Remembers how often and how recently each repository was opened, and turns that into the
/// frecency weight <see cref="RepoRanker"/> sorts by. The weight follows Command Palette's own
/// (RecentCommandsManager.GetCommandHistoryWeight), with one addition: a repository's last git
/// activity counts towards how recently it was used, though not towards how often.
/// </summary>
internal sealed class RepoHistory
{
    // Command Palette's values: recency halves every three days, frequency adds log2(uses + 1)
    // on top of the base, and the weight stays within 0..70.
    internal const double HalfLifeDays = 3.0;
    internal const double BaseWeight = 10.0;
    internal const double FrequencyWeight = 10.0;
    internal const double MaxWeight = 70.0;
    internal const int MaxEntries = 500;

    private readonly string _path;
    private readonly Lock _lock = new();

    // GitRepoInfo.ChoiceKey -> uses and last open, like the terminal profile choices
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public RepoHistory(string path)
    {
        _path = path;
        Load();
    }

    /// <summary>Raised after a repository's open is recorded.</summary>
    public event EventHandler? Changed;

    /// <summary>Records that the repository was opened (in VS Code, a terminal or Explorer).</summary>
    public void RecordOpen(GitRepoInfo repo, DateTimeOffset? now = null)
    {
        lock (_lock)
        {
            var uses = _entries.TryGetValue(repo.ChoiceKey, out var entry) ? entry.Uses : 0;
            _entries[repo.ChoiceKey] = new Entry(uses + 1, now ?? DateTimeOffset.UtcNow);

            if (_entries.Count > MaxEntries)
            {
                foreach (var key in _entries.OrderByDescending(e => e.Value.LastOpened).Skip(MaxEntries).Select(e => e.Key).ToList())
                {
                    _entries.Remove(key);
                }
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The repository's frecency weight at <paramref name="now"/>, from 0 (never used) to 70.
    /// </summary>
    public int Weight(GitRepoInfo repo, DateTimeOffset now)
    {
        Entry? entry;
        lock (_lock)
        {
            entry = _entries.GetValueOrDefault(repo.ChoiceKey);
        }

        DateTimeOffset? lastUsed = entry?.LastOpened;
        if (repo.LastGitActivity is { } activity && (lastUsed is null || activity > lastUsed))
        {
            lastUsed = activity;
        }

        if (lastUsed is null)
        {
            return 0;
        }

        // Clamp the age at zero so a timestamp slightly in the future (clock skew) can't amplify
        var ageDays = Math.Max(0.0, (now - lastUsed.Value).TotalDays);
        var recency = Math.Pow(2.0, -ageDays / HalfLifeDays);
        var frequency = Math.Log2((entry?.Uses ?? 0) + 1);
        var weight = recency * (BaseWeight + (FrequencyWeight * frequency));

        return (int)Math.Round(Math.Clamp(weight, 0.0, MaxWeight));
    }

    private void Save()
    {
        try
        {
            using var stream = File.Create(_path);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            foreach (var (key, entry) in _entries)
            {
                writer.WriteStartObject(key);
                writer.WriteNumber("uses", entry.Uses);
                writer.WriteString("lastOpened", entry.LastOpened);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ranking still works for this session from the in-memory history
        }
    }

    private void Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            foreach (var repo in document.RootElement.EnumerateObject())
            {
                if (repo.Value.ValueKind == JsonValueKind.Object &&
                    repo.Value.TryGetProperty("uses", out var uses) && uses.TryGetInt32(out var useCount) &&
                    repo.Value.TryGetProperty("lastOpened", out var lastOpened) && lastOpened.TryGetDateTimeOffset(out var lastOpenedTime))
                {
                    _entries[repo.Name] = new Entry(useCount, lastOpenedTime);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // No history yet, or the file is unreadable: repositories rank by git activity alone
        }
    }

    private sealed record Entry(int Uses, DateTimeOffset LastOpened);
}
