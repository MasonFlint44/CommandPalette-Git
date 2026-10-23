// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace GitExtension.Services;

/// <summary>
/// Looks up the WSL distros registered for the current user.
/// </summary>
internal static class WslDistros
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    private static readonly object CacheLock = new();
    private static List<string> _names = [];

    /// <summary>
    /// The registered spelling of a distro name, e.g. "Ubuntu" for "ubuntu". Scan paths keep
    /// whatever case the user typed, but Windows Terminal's generated profile IDs depend on the
    /// exact name. Returns the name unchanged if no registered distro matches.
    /// </summary>
    public static string Canonicalize(string distro)
    {
        lock (CacheLock)
        {
            var match = Find(distro);
            if (match is null)
            {
                // The distro may have been registered since the last lookup
                _names = ReadRegisteredNames();
                match = Find(distro);
            }

            return match ?? distro;
        }

        static string? Find(string distro) => _names.FirstOrDefault(n => string.Equals(n, distro, StringComparison.OrdinalIgnoreCase));
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
