// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using GitExtension.Models;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Services;

/// <summary>
/// Orders repositories the way Command Palette orders its main list (MainListPage and
/// MainListRanker in PowerToys). Without a search, the most used and most recently used come
/// first. With one, how the name matches sets a tier that always wins, and within a tier the
/// match quality and frecency are added up.
/// </summary>
internal static class RepoRanker
{
    // Command Palette's balance: one point of match quality is worth ten of frecency (0..70), so
    // history reorders matches of about the same quality but can't lift a clearly worse one.
    internal const double LexicalScale = 10.0;

    private enum Tier
    {
        None,
        Fuzzy,
        WordStart,
        Prefix,
        ExactName,
    }

    /// <summary>
    /// The repositories matching <paramref name="search"/>, best first. All of them, by frecency,
    /// when the search is empty.
    /// </summary>
    /// <param name="locationLabel">The repository's location tag (its distro, or Windows), which also matches.</param>
    public static List<GitRepoInfo> Rank(
        IEnumerable<GitRepoInfo> repos,
        string? search,
        RepoHistory history,
        Func<GitRepoInfo, string> locationLabel,
        DateTimeOffset now)
    {
        var query = search?.Trim() ?? string.Empty;

        return repos
            .Select(repo => (Repo: repo, Score: Score(repo, query, history, locationLabel, now)))
            .Where(r => r.Score.Tier != Tier.None)
            .OrderByDescending(r => r.Score.Tier)
            .ThenByDescending(r => r.Score.WithinTier)
            .ThenBy(r => r.Repo.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Repo.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Repo)
            .ToList();
    }

    private static (Tier Tier, double WithinTier) Score(
        GitRepoInfo repo,
        string query,
        RepoHistory history,
        Func<GitRepoInfo, string> locationLabel,
        DateTimeOffset now)
    {
        var frecency = history.Weight(repo, now);
        if (query.Length == 0)
        {
            return (Tier.Fuzzy, frecency);
        }

        // Like Command Palette's title and subtitle: the name counts fully, the path and the
        // location tag less
        var nameScore = FuzzyScore(query, repo.Name);
        var pathScore = FuzzyScore(query, repo.DisplayPath);
        var locationScore = FuzzyScore(query, locationLabel(repo));
        if (nameScore == 0 && pathScore == 0 && locationScore == 0)
        {
            return (Tier.None, 0);
        }

        var lexicalQuality = Math.Max(nameScore, Math.Max((pathScore - 4) / 2.0, (locationScore - 4) / 2.0));
        return (NameTier(repo.Name, query), (lexicalQuality * LexicalScale) + frecency);
    }

    private static Tier NameTier(string name, string query)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return Tier.ExactName;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return Tier.Prefix;
        }

        return MatchesWordStartOrAcronym(name, query) ? Tier.WordStart : Tier.Fuzzy;
    }

    /// <summary>
    /// The toolkit's fuzzy score, except that an all-lowercase query gets no bonus for matching
    /// letters' case, as in Command Palette's main search: typing "git" says nothing about case,
    /// so "Git" shouldn't lose to "git".
    /// </summary>
    internal static int FuzzyScore(string query, string target)
    {
        if (!query.All(c => !char.IsLetter(c) || c is >= 'a' and <= 'z'))
        {
            return FuzzyStringMatcher.ScoreFuzzy(query, target);
        }

        var (score, positions) = FuzzyStringMatcher.ScoreFuzzyWithPositions(query, target, allowNonContiguousMatches: true);
        if (score == 0 || positions.Count != query.Length)
        {
            return score;
        }

        // The toolkit adds a point for each matched character whose case matches exactly
        var sameCase = positions.Where((position, i) => target[position] == query[i]).Count();
        return score - sameCase;
    }

    // From Command Palette's MainListRanker.MatchesWordBoundaryOrAcronym: a word of the name
    // starts with the query, or the query is a run of the words' initials ("cpg" for
    // CommandPalette-Git). Words start after separators (-, _, . and so on) and at camel case.
    internal static bool MatchesWordStartOrAcronym(string name, string query)
    {
        if (query.Length == 0 || name.Length == 0)
        {
            return false;
        }

        var initials = new List<char>();
        for (var i = 0; i < name.Length; i++)
        {
            if (IsWordStart(name, i))
            {
                if (name.AsSpan(i).StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                initials.Add(name[i]);
            }
        }

        // Single characters are left to the word start check above
        return query.Length >= 2 &&
            new string([.. initials]).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWordStart(string s, int i)
    {
        if (i == 0)
        {
            return char.IsLetterOrDigit(s[i]);
        }

        var prev = s[i - 1];
        var cur = s[i];
        return (!char.IsLetterOrDigit(prev) && char.IsLetterOrDigit(cur)) ||
            (char.IsUpper(cur) && (char.IsLower(prev) || char.IsDigit(prev)));
    }
}
