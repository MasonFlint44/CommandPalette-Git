// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitExtension.Commands;
using GitExtension.Models;
using GitExtension.Services;
using GitExtension.Settings;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Pages;

internal sealed partial class GitReposPage : DynamicListPage
{
    private static readonly IconInfo GitIcon =
        IconHelpers.FromRelativePaths("Assets\\git.light.svg", "Assets\\git.dark.svg");

    // Windows blue for Windows repos; WSL repos use their distro's brand colour when known.
    private static readonly OptionalColor WindowsTagColor = ColorHelpers.FromRgb(0x00, 0x78, 0xD4);
    private static readonly OptionalColor WslFallbackTagColor = ColorHelpers.FromRgb(0x6B, 0x4F, 0xBB);

    // Matched as name prefixes so versioned distros (Ubuntu-22.04, kali-linux, archlinux) get their colour.
    private static readonly (string Prefix, OptionalColor Color)[] DistroTagColors =
    [
        ("Ubuntu", ColorHelpers.FromRgb(0xE9, 0x54, 0x20)),
        ("Debian", ColorHelpers.FromRgb(0xA8, 0x00, 0x30)),
        ("openSUSE", ColorHelpers.FromRgb(0x73, 0xBA, 0x25)),
        ("Fedora", ColorHelpers.FromRgb(0x51, 0xA2, 0xDA)),
        ("Arch", ColorHelpers.FromRgb(0x17, 0x93, 0xD1)),
        ("kali", ColorHelpers.FromRgb(0x36, 0x7B, 0xF0)),
        ("AlmaLinux", ColorHelpers.FromRgb(0x0F, 0x4E, 0x6E)),
        ("OracleLinux", ColorHelpers.FromRgb(0xC7, 0x46, 0x34)),
    ];

    private readonly GitRepoSettingsManager _settingsManager;
    private readonly SetupPage _setupPage;
    private List<GitRepoInfo> _repos = [];
    private int _isScanning;
    private Task _initialScan = Task.CompletedTask;

    // Cache of command items for dock pinning lookup
    private readonly Dictionary<string, ICommandItem> _commandItemCache = [];

    public GitReposPage(GitRepoSettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _setupPage = new SetupPage(settingsManager);

        Icon = GitIcon;
        Title = "Browse git repositories";
        Name = "Open";
        PlaceholderText = "Search repositories...";

        _settingsManager.Settings.SettingsChanged += OnSettingsChanged;
        _settingsManager.ScanPathsUpdated += (s, e) => OnSettingsChanged(s, _settingsManager.Settings);

        // Scan in the background so a stopped WSL distro (which has to boot when its
        // \\wsl.localhost path is first touched) doesn't hold up loading the extension.
        _initialScan = RefreshReposInBackground();
    }

    private void OnSettingsChanged(object? sender, Microsoft.CommandPalette.Extensions.Toolkit.Settings settingsArgs)
    {
        RefreshRepos();
        RaiseItemsChanged();
    }

    private void RefreshRepos()
    {
        if (string.IsNullOrWhiteSpace(_settingsManager.ScanPaths))
        {
            _repos = [];
            return;
        }

        _repos = GitRepoScanner.Scan(_settingsManager.ScanPaths, _settingsManager.MaxDepth);
    }

    private Task RefreshReposInBackground()
    {
        if (string.IsNullOrWhiteSpace(_settingsManager.ScanPaths))
        {
            return Task.CompletedTask;
        }

        // Skip if a scan is already running
        if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        IsLoading = true;
        return Task.Run(() =>
        {
            try
            {
                var newRepos = GitRepoScanner.Scan(_settingsManager.ScanPaths, _settingsManager.MaxDepth);
                var changed = !_repos.Select(r => r.FullPath).SequenceEqual(newRepos.Select(r => r.FullPath));
                if (changed)
                {
                    _repos = newRepos;
                    RaiseItemsChanged();
                }
            }
            finally
            {
                IsLoading = false;
                Interlocked.Exchange(ref _isScanning, 0);
            }
        });
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged();
    }

    /// <summary>
    /// Resolves a command item by ID for dock pinning support.
    /// Falls back to building the item on-demand from _repos so that
    /// pinned dock items resolve even before the page has been visited.
    /// </summary>
    internal ICommandItem? GetCommandItemById(string id)
    {
        if (_commandItemCache.TryGetValue(id, out var item))
        {
            return item;
        }

        // Dock pins can be resolved before the initial background scan finishes,
        // so give it a few seconds before giving up on the ID.
        if (!_initialScan.IsCompleted)
        {
            _initialScan.Wait(TimeSpan.FromSeconds(5));
        }

        // On-demand resolution: find the repo by ID and build a CommandItem
        var repo = _repos.FirstOrDefault(r => RepoCommandId(r) == id);
        if (repo == null)
        {
            return null;
        }

        var openFolder = new OpenFolderCommand(repo) { Id = id };
        var commandItem = new CommandItem(openFolder)
        {
            Title = repo.Name,
            Subtitle = repo.DisplayPath,
            Icon = GitIcon,
        };
        _commandItemCache[id] = commandItem;
        return commandItem;
    }

    private static string RepoCommandId(GitRepoInfo repo) => $"repo.{repo.FullPath}";

    private static string LocationLabel(GitRepoInfo repo) => repo.Distro ?? "Windows";

    private static Tag LocationTag(GitRepoInfo repo)
    {
        var background = !repo.IsWsl
            ? WindowsTagColor
            : DistroTagColors
                .Where(d => repo.Distro!.StartsWith(d.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Color)
                .DefaultIfEmpty(WslFallbackTagColor)
                .First();

        return new Tag(LocationLabel(repo))
        {
            Foreground = ColorHelpers.FromRgb(0xFF, 0xFF, 0xFF),
            Background = background,
            ToolTip = repo.IsWsl ? $"WSL ({repo.Distro})" : "Windows",
        };
    }

    public override IListItem[] GetItems()
    {
        // Trigger a background rescan each time the page is shown
        RefreshReposInBackground();

        if (_repos.Count == 0)
        {
            // The first scan is still running; show the loading bar rather than "no repositories".
            if (!_initialScan.IsCompleted)
            {
                return [];
            }

            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "No git repositories found",
                    Subtitle = "Check your scan paths and depth in settings",
                },
                new ListItem(_setupPage)
                {
                    Title = "Change scan paths",
                    Icon = new IconInfo("\uE713"),
                },
            ];
        }

        var filtered = FilterRepos();
        _commandItemCache.Clear();

        var items = filtered.Select(repo =>
        {
            var repoId = RepoCommandId(repo);
            var openFolder = new OpenFolderCommand(repo) { Id = repoId };
            var openTerminal = new OpenInTerminalCommand(repo, _settingsManager.TerminalCommand);
            var openVSCode = new OpenInVSCodeCommand(repo);
            var copyPath = new CopyRepoPathCommand(repo);

            List<IContextItem> moreCommands =
            [
                new CommandContextItem(openTerminal),
                new CommandContextItem(openVSCode),
                new CommandContextItem(copyPath),
            ];
            if (repo.IsWsl)
            {
                moreCommands.Add(new CommandContextItem(new CopyRepoPathCommand(repo, windowsPath: true)));
            }

            moreCommands.Add(new CommandContextItem(_settingsManager.Settings.SettingsPage) { Title = "Settings", Icon = new IconInfo("\uE713") });

            var listItem = new ListItem(openFolder)
            {
                Title = repo.Name,
                Subtitle = repo.DisplayPath,
                Icon = GitIcon,
                Tags = [LocationTag(repo)],
                MoreCommands = [.. moreCommands],
            };

            // Register for dock pinning
            var commandItem = new CommandItem(openFolder)
            {
                Title = repo.Name,
                Subtitle = repo.DisplayPath,
                Icon = GitIcon,
            };
            _commandItemCache[repoId] = commandItem;

            return listItem;
        }).ToList();

        return items.ToArray();
    }

    private List<GitRepoInfo> FilterRepos()
    {
        var search = SearchText;
        if (string.IsNullOrWhiteSpace(search))
        {
            return _repos;
        }

        return _repos
            .Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.DisplayPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                LocationLabel(r).Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
