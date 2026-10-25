// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GitExtension.Models;

namespace GitExtension.Services;

/// <summary>
/// Reads a repository's remotes from its git config and turns a remote URL into the
/// repository's web page, without running git.
/// </summary>
internal static partial class GitRemotes
{
    /// <summary>The origin remote's URL, or the first remote's when there's no origin.</summary>
    public static string? PreferredRemoteUrl(string config)
    {
        var remotes = RemoteUrls(config);
        return remotes.FirstOrDefault(r => r.Name == "origin").Url ?? remotes.FirstOrDefault().Url;
    }

    /// <summary>Each remote's name and URL, in the order the config lists them.</summary>
    internal static List<(string Name, string Url)> RemoteUrls(string config)
    {
        var remotes = new List<(string Name, string Url)>();
        string? remote = null;

        foreach (var rawLine in config.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var header = RemoteHeaderRegex().Match(line);
                remote = header.Success ? Unescape(header.Groups["name"].Value) : null;
                continue;
            }

            var equals = line.IndexOf('=');
            if (remote is null || equals < 0 ||
                !line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase) ||
                remotes.Any(r => r.Name == remote))
            {
                continue;
            }

            var url = Value(line[(equals + 1)..]);
            if (url.Length > 0)
            {
                remotes.Add((remote, url));
            }
        }

        return remotes;
    }

    /// <summary>
    /// The web page for a remote URL such as https://github.com/owner/repo.git or
    /// git@github.com:owner/repo.git, without any user name or token. Null for local remotes and
    /// URLs it can't read.
    /// </summary>
    public static RemotePage? WebPage(string remote)
    {
        remote = remote.Trim();
        string scheme = "https", host, path;
        var port = string.Empty;

        if (remote.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            {
                return null;
            }

            switch (uri.Scheme)
            {
                case "https" or "http":
                    scheme = uri.Scheme;
                    port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
                    break;
                case "ssh" or "git" or "git+ssh" or "ssh+git":
                    // The web server isn't on the SSH or git port
                    break;
                default:
                    return null;
            }

            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else
        {
            // scp-like syntax, [user@]host:path. A one-letter host is a Windows drive (C:\repo).
            var match = ScpLikeRegex().Match(remote);
            if (!match.Success || match.Groups["host"].Length == 1)
            {
                return null;
            }

            host = match.Groups["host"].Value;
            path = "/" + match.Groups["path"].Value.TrimStart('/');
        }

        path = path.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4].TrimEnd('/');
        }

        if (path.Length == 0)
        {
            return null;
        }

        host = host.ToLowerInvariant();
        return AzureDevOpsPage(host, path) ?? new RemotePage($"{scheme}://{host}{port}{path}", HostName(host));
    }

    // Azure DevOps's SSH remotes look nothing like its web pages:
    // git@ssh.dev.azure.com:v3/org/project/repo -> https://dev.azure.com/org/project/_git/repo
    // org@vs-ssh.visualstudio.com:v3/org/project/repo -> https://org.visualstudio.com/project/_git/repo
    private static RemotePage? AzureDevOpsPage(string host, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "v3")
        {
            return null;
        }

        var (organization, project, repo) = (parts[1], parts[2], parts[3]);
        return host switch
        {
            "ssh.dev.azure.com" => new RemotePage($"https://dev.azure.com/{organization}/{project}/_git/{repo}", "Azure DevOps"),
            "vs-ssh.visualstudio.com" => new RemotePage($"https://{organization}.visualstudio.com/{project}/_git/{repo}", "Azure DevOps"),
            _ => null,
        };
    }

    private static string? HostName(string host) => host switch
    {
        "github.com" => "GitHub",
        "gitlab.com" => "GitLab",
        "bitbucket.org" => "Bitbucket",
        "dev.azure.com" => "Azure DevOps",
        _ when host.EndsWith(".visualstudio.com", StringComparison.Ordinal) => "Azure DevOps",
        _ => null,
    };

    // A config value: quoted, or up to an inline # or ; comment
    private static string Value(string raw)
    {
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == '\\' && i + 1 < raw.Length)
            {
                value.Append(raw[++i]);
            }
            else if (!quoted && c is '#' or ';')
            {
                break;
            }
            else
            {
                value.Append(c);
            }
        }

        return value.ToString().Trim();
    }

    private static string Unescape(string name) => Regex.Replace(name, @"\\(.)", "$1");

    // [remote "origin"]; section names are case-insensitive, subsection names aren't
    [GeneratedRegex(@"^\[\s*remote\s+""(?<name>(?:[^""\\]|\\.)*)""\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteHeaderRegex();

    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]+):(?<path>.+)$")]
    private static partial Regex ScpLikeRegex();
}
