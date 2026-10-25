// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitExtension.Services;
using GitExtension.Settings;

namespace GitExtension.Tests;

/// <summary>
/// Gives each test a temporary folder, its own Windows Terminal settings file and settings
/// files, and a fixed list of registered distros. Restores the real ones afterwards.
/// </summary>
public abstract class TestEnvironment : IDisposable
{
    private readonly string _realTerminalSettingsPath = TerminalProfiles.SettingsPath;

    protected TestEnvironment()
    {
        Directory = Path.Combine(Path.GetTempPath(), "GitExtension.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        TerminalProfiles.SettingsPath = Path.Combine(Directory, "terminal-settings.json");
        SetDistros("Ubuntu");
    }

    protected string Directory { get; }

    /// <summary>Writes Windows Terminal's settings.json with the given profiles list.</summary>
    protected static void SetTerminalProfiles(params string[] profiles) =>
        File.WriteAllText(TerminalProfiles.SettingsPath, $$"""{ "profiles": { "list": [ {{string.Join(",\n", profiles)}} ] } }""");

    protected static void SetDistros(params string[] names) => WslDistros.SetSource(() => [.. names]);

    /// <summary>A settings manager whose files live in this test's folder.</summary>
    internal GitRepoSettingsManager NewSettings() => new(Path.Combine(Directory, "settings"));

    public void Dispose()
    {
        TerminalProfiles.SettingsPath = _realTerminalSettingsPath;
        WslDistros.SetSource(null);
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>Records what the open commands would launch.</summary>
internal sealed class RecordingLauncher : Launcher
{
    public List<string> Launches { get; } = [];

    public override void Terminal(string arguments) => Launches.Add($"wt {arguments}");

    public override void Shell(string program, string arguments, string? workingDirectory = null) =>
        Launches.Add(workingDirectory is null ? $"{program} {arguments}" : $"{program} {arguments} (in {workingDirectory})");

    public override void Browser(string url) => Launches.Add($"browser {url}");
}

/// <summary>Windows Terminal profile entries for settings.json.</summary>
internal static class Profiles
{
    public const string UbuntuAppId = "{11111111-1111-1111-1111-111111111111}";
    public const string UbuntuGeneratedId = "{2c4de342-38b7-51cf-b940-2309a097f518}";
    public const string PowerShellId = "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}";
    public const string CmdId = "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}";

    public const string UbuntuStore = "CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc";

    public static string UbuntuApp => Store(UbuntuAppId, "Ubuntu");

    public static string UbuntuGenerated => Generated(UbuntuGeneratedId, "Ubuntu", hidden: true);

    public static string PowerShell => Other(PowerShellId, "Windows PowerShell");

    public static string Cmd => Other(CmdId, "Command Prompt");

    public static string Store(string id, string name, bool hidden = false, string? commandLine = null) =>
        Entry(id, name, UbuntuStore, hidden, commandLine);

    public static string Generated(string id, string name, bool hidden = false) =>
        Entry(id, name, "Windows.Terminal.Wsl", hidden, null);

    public static string Other(string id, string name, bool hidden = false, string? commandLine = null) =>
        Entry(id, name, null, hidden, commandLine);

    private static string Entry(string id, string name, string? source, bool hidden, string? commandLine) =>
        "{" + string.Join(", ", new[]
        {
            $"\"guid\": \"{id}\"",
            $"\"name\": \"{name}\"",
            source is null ? null : $"\"source\": \"{source}\"",
            hidden ? "\"hidden\": true" : null,
            commandLine is null ? null : $"\"commandline\": \"{commandLine.Replace("\\", "\\\\")}\"",
        }.Where(p => p is not null)) + "}";
}
