// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using GitExtension.Commands;
using GitExtension.Models;
using GitExtension.Services;
using GitExtension.Settings;

namespace GitExtension.Tests;

/// <summary>
/// Launches the real programs against this machine's Windows Terminal settings, registry and
/// Ubuntu distro, and checks where they opened. They open and close windows, so they only run
/// when GITEXTENSION_LAUNCH_TESTS=1. Each test works in its own scratch folders (one in
/// Ubuntu's /tmp, one in Windows' temp folder) and closes only the windows it opened.
/// </summary>
public sealed class LaunchTests : IDisposable
{
    private const string Skip = "Opens real windows; set GITEXTENSION_LAUNCH_TESTS=1 to run";
    private const string Distro = "Ubuntu";
    private const string TerminalWindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);

    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private readonly string _linuxRoot;
    private readonly string _windowsRoot;
    private readonly GitRepoSettingsManager _settings;
    private readonly List<IntPtr> _windowsToClose = [];
    private readonly List<string> _explorerFoldersToClose = [];

    public LaunchTests()
    {
        _linuxRoot = $"/tmp/gitext-test-{_id}";
        _windowsRoot = Path.Combine(Path.GetTempPath(), $"gitext-test-{_id}");
        _settings = new GitRepoSettingsManager(Path.Combine(_windowsRoot, "settings"));
    }

    public static bool Enabled => Environment.GetEnvironmentVariable("GITEXTENSION_LAUNCH_TESTS") == "1";

    // A repo folder with a space and a unique name, so windows can be found by title
    private string RepoName => $"gitext repo {_id}";

    private GitRepoInfo MakeWslRepo()
    {
        var linuxPath = $"{_linuxRoot}/{RepoName}";
        Wsl($"mkdir -p \"$1/.git\"", linuxPath);
        return new GitRepoInfo(RepoName, $@"\\wsl.localhost\{Distro}{linuxPath.Replace('/', '\\')}");
    }

    private GitRepoInfo MakeWindowsRepo()
    {
        var path = Path.Combine(_windowsRoot, RepoName);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return new GitRepoInfo(RepoName, path);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Registry_gives_the_registered_distro_spelling()
    {
        Assert.Equal(Distro, WslDistros.Canonicalize(Distro.ToLowerInvariant()));
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Scanning_a_WSL_path_finds_its_repos()
    {
        MakeWslRepo();

        var repo = Assert.Single(GitRepoScanner.Scan($@"\\wsl.localhost\{Distro.ToLowerInvariant()}{_linuxRoot.Replace('/', '\\')}", 2));

        Assert.Equal(Distro, repo.Distro);
        Assert.Equal($"{_linuxRoot}/{RepoName}", repo.LinuxPath);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Ubuntu_repos_default_to_the_Ubuntu_apps_profile()
    {
        var profile = TerminalProfiles.Resolve(MakeWslRepo(), _settings, TerminalProfiles.Load());

        Assert.NotNull(profile);
        Assert.Equal(TerminalProfileKind.StoreApp, profile.Kind);
        Assert.True(profile.IsDistroLauncher);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Terminal_opens_a_WSL_repo_in_its_folder_with_the_default_profile()
    {
        var repo = MakeWslRepo();

        OpenInTerminal(repo);

        Assert.NotEmpty(WaitForShellsIn(repo.LinuxPath!));
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Terminal_opens_a_WSL_repo_in_its_folder_with_the_generated_profile()
    {
        var repo = MakeWslRepo();
        _settings.SetRepoTerminalProfile(repo.ChoiceKey, TerminalProfiles.GeneratedWslProfileId(Distro));

        OpenInTerminal(repo);

        Assert.NotEmpty(WaitForShellsIn(repo.LinuxPath!));
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Terminal_opens_a_Windows_repo_under_mnt_with_the_generated_WSL_profile()
    {
        var repo = MakeWindowsRepo();
        _settings.SetRepoTerminalProfile(repo.ChoiceKey, TerminalProfiles.GeneratedWslProfileId(Distro));

        OpenInTerminal(repo);

        var drive = char.ToLowerInvariant(repo.FullPath[0]);
        Assert.NotEmpty(WaitForShellsIn($"/mnt/{drive}{repo.FullPath[2..].Replace('\\', '/')}"));
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void VS_Code_opens_a_WSL_repo_through_the_WSL_remote()
    {
        new OpenInVSCodeCommand(MakeWslRepo()).Invoke();

        var title = WaitForWindow(title => title.Contains(RepoName, StringComparison.Ordinal), TimeSpan.FromSeconds(90));

        Assert.Contains($"[WSL: {Distro}]", title, StringComparison.Ordinal);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void VS_Code_opens_a_Windows_repo()
    {
        new OpenInVSCodeCommand(MakeWindowsRepo()).Invoke();

        var title = WaitForWindow(title => title.Contains(RepoName, StringComparison.Ordinal), TimeSpan.FromSeconds(60));

        Assert.DoesNotContain("[WSL", title, StringComparison.Ordinal);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Explorer_opens_a_WSL_repo()
    {
        var repo = MakeWslRepo();

        new OpenFolderCommand(repo).Invoke();

        Assert.True(WaitForExplorerAt(repo.FullPath), $"No Explorer window at {repo.FullPath}");
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public void Explorer_opens_a_Windows_repo()
    {
        var repo = MakeWindowsRepo();

        new OpenFolderCommand(repo).Invoke();

        Assert.True(WaitForExplorerAt(repo.FullPath), $"No Explorer window at {repo.FullPath}");
    }

    /// <summary>
    /// Runs the command as Command Palette would, but in a new Terminal window, so that closing
    /// it afterwards can't close tabs the user has open.
    /// </summary>
    private void OpenInTerminal(GitRepoInfo repo)
    {
        var before = TopLevelWindows(TerminalWindowClass);
        new OpenInTerminalCommand(repo, _settings, new NewWindowLauncher()).Invoke();

        List<IntPtr> opened = [];
        var found = WaitUntil(() => (opened = [.. TopLevelWindows(TerminalWindowClass).Except(before)]).Count > 0, LaunchTimeout);
        _windowsToClose.AddRange(opened);
        Assert.True(found, "No new Windows Terminal window opened");
    }

    private sealed class NewWindowLauncher : Launcher
    {
        public override void Terminal(string arguments) => base.Terminal($"-w new {arguments}");
    }

    /// <summary>The names of the processes in the distro whose working folder is <paramref name="linuxPath"/>.</summary>
    private static List<string> WaitForShellsIn(string linuxPath)
    {
        List<string> names = [];
        WaitUntil(
            () => (names = [.. Wsl("for p in /proc/[0-9]*; do [ \"$(readlink \"$p/cwd\" 2>/dev/null)\" = \"$1\" ] && cat \"$p/comm\"; done; true", linuxPath)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]).Count > 0,
            LaunchTimeout);
        return names;
    }

    private string WaitForWindow(Func<string, bool> matches, TimeSpan timeout)
    {
        var window = IntPtr.Zero;
        Assert.True(WaitUntil(() => (window = TopLevelWindows(null).FirstOrDefault(w => matches(WindowTitle(w)))) != IntPtr.Zero, timeout), "No matching window opened");
        _windowsToClose.Add(window);
        return WindowTitle(window);
    }

    private bool WaitForExplorerAt(string path)
    {
        _explorerFoldersToClose.Add(path);
        return WaitUntil(() => ExplorerWindowsAt(path).Count > 0, LaunchTimeout);
    }

    private static List<dynamic> ExplorerWindowsAt(string path)
    {
        var shell = (dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        var found = new List<dynamic>();
        foreach (var window in shell.Windows())
        {
            string url = window.LocationURL;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile
                && string.Equals(uri.LocalPath.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                found.Add(window);
            }
        }

        return found;
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            Thread.Sleep(250);
        }

        return true;
    }

    /// <summary>Runs a sh script in the distro with the given arguments ($1, ...) and returns its output.</summary>
    private static string Wsl(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in (string[])["-d", Distro, "-e", "sh", "-c", script, "sh", .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static List<IntPtr> TopLevelWindows(string? className)
    {
        var windows = new List<IntPtr>();
        EnumWindows(
            (window, _) =>
            {
                if (IsWindowVisible(window) && (className is null || WindowClass(window) == className))
                {
                    windows.Add(window);
                }

                return true;
            },
            IntPtr.Zero);
        return windows;
    }

    private static string WindowTitle(IntPtr window)
    {
        var buffer = new char[512];
        return new string(buffer, 0, GetWindowTextW(window, buffer, buffer.Length));
    }

    private static string WindowClass(IntPtr window)
    {
        var buffer = new char[256];
        return new string(buffer, 0, GetClassNameW(window, buffer, buffer.Length));
    }

    public void Dispose()
    {
        foreach (var window in _windowsToClose)
        {
            PostMessageW(window, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        foreach (var path in _explorerFoldersToClose)
        {
            foreach (var window in ExplorerWindowsAt(path))
            {
                window.Quit();
            }
        }

        Wsl("rm -rf \"$1\"", _linuxRoot);
        try
        {
            Directory.Delete(_windowsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    // DllImport rather than LibraryImport: the generator can't marshal the callback or buffers
    // without unsafe code, and these tests don't need to be trimmed or AOT compiled
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
#pragma warning restore SYSLIB1054
}
