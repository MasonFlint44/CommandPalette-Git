// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace GitExtension.Models;

/// <summary>A repository's page on its git host.</summary>
/// <param name="Url">The page, e.g. https://github.com/owner/repo, without any credentials.</param>
/// <param name="HostName">The host's name for the menu (GitHub, GitLab…), or null for other hosts.</param>
public sealed record RemotePage(string Url, string? HostName);
