// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace GitExtension.Services;

/// <summary>
/// Looks up the WSL distros registered for the current user.
/// </summary>
internal static class WslDistros
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    // A name that matches no registered distro triggers a re-read (it may have just been
    // registered), but at most this often, since every repo under such a path asks.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private static readonly Lock CacheLock = new();
    private static List<string>? _names;
    private static DateTime _readAt;

    /// <summary>The registered distro names.</summary>
    public static IReadOnlyList<string> RegisteredNames()
    {
        lock (CacheLock)
        {
            return Names(refresh: false);
        }
    }

    /// <summary>
    /// The registered spelling of a distro name, e.g. "Ubuntu" for "ubuntu". Scan paths keep
    /// whatever case the user typed, but Windows Terminal's generated profile IDs depend on the
    /// exact name. Returns the name unchanged if no registered distro matches.
    /// </summary>
    public static string Canonicalize(string distro)
    {
        lock (CacheLock)
        {
            return Find(Names(refresh: false), distro) ?? Find(Names(refresh: true), distro) ?? distro;
        }

        static string? Find(List<string> names, string distro) =>
            names.FirstOrDefault(n => string.Equals(n, distro, StringComparison.OrdinalIgnoreCase));
    }

    // Call with CacheLock held
    private static List<string> Names(bool refresh)
    {
        if (_names is null || (refresh && DateTime.UtcNow - _readAt > RefreshInterval))
        {
            _names = ReadRegisteredNames();
            _readAt = DateTime.UtcNow;
        }

        return _names;
    }

    private static List<string> ReadRegisteredNames()
    {
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
            if (lxss is null)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var id in lxss.GetSubKeyNames())
            {
                using var distro = lxss.OpenSubKey(id);
                if (distro?.GetValue("DistributionName") is string name && !string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return [];
        }
    }
}
