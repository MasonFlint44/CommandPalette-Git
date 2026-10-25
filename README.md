# Git Repos — Command Palette Extension

A [Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) extension that discovers your local git repositories and gives you quick access to open them in Explorer, VS Code, or Terminal.

![Screenshot](https://img.shields.io/badge/Platform-Windows-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Scan & discover** — Recursively finds git repositories under your configured root directories
- **Open in VS Code** — `Enter` opens the repository in VS Code
- **Open in Terminal** — `Ctrl+Enter` opens Windows Terminal at the repo directory
- **Open in Explorer** — Opens the repo folder in Windows Explorer, from the context menu
- **Terminal profiles** — Pick the Windows Terminal profile repos open with: globally, per WSL distro, or per repo
- **Copy path** — Copy the full repository path to your clipboard
- **WSL repositories** — Repos under `\\wsl.localhost\<distro>\` open in VS Code's WSL mode and in a WSL shell, and show their Linux path
- **Location labels** — Each repo is tagged "Windows" or with its WSL distro name
- **Search & ranking** — Fuzzy search by name, path or location, with the repos you use most at the top (see [How repos are ordered](#how-repos-are-ordered))
- **Dock pinning** — Pin your favorite repos to the Command Palette Dock for instant access
- **Theme-aware icons** — Git icons that adapt to light and dark themes
- **Inline setup** — Configure scan paths directly from the extension, no need to dig into settings

## Getting started

This is a fork of [niels9001/CommandPalette-Git](https://github.com/niels9001/CommandPalette-Git) with its own package name, so it installs alongside the original. If you have both, disable one in Command Palette's extension settings so repos aren't listed twice. The fork keeps its own settings, so scan paths need setting up again.

### Build and install from source

**Prerequisites:**
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- [PowerToys](https://github.com/microsoft/PowerToys) with Command Palette enabled
- Windows Developer Mode (Settings > System > For developers), to install a build that isn't signed

**Build & install:**

```powershell
git clone https://github.com/MasonFlint44/CommandPalette-Git.git
cd CommandPalette-Git
.\scripts\Install-Dev.ps1
```

This builds the extension and installs it for your user from the build output. Run it again after making changes; `-Uninstall` removes it. You can also deploy from Visual Studio 2022 (17.13+).

## Configuration

When you first open the extension, you'll be prompted to configure your scan paths.

| Setting | Description | Default |
|---------|-------------|---------|
| **Scan paths** | Semicolon-separated root directories (e.g. `D:\Projects;C:\repos`). For WSL, use the distro's network path (e.g. `\\wsl.localhost\Ubuntu\home\me\git`) | *(empty)* |
| **Max scan depth** | How deep to recurse into directories (2–6) | 3 |
| **Terminal profile** | Windows Terminal profile to open repos with. Order: the repo's own choice, then for WSL repos the distro's choice, the distro app's profile (e.g. the Ubuntu app's) and the profile Terminal generated for the distro, then this setting, then Windows Terminal's default. Repo and distro choices are made from a repo's context menu | Windows Terminal default |
| **Default action** | What `Enter` does on a repository: open in VS Code, open in terminal, or open folder. `Ctrl+Enter` runs the next of those in that order (terminal, or VS Code if terminal is on `Enter`), and the third is in the context menu | Open in VS Code |

You can change these anytime via the "Change scan paths" item at the bottom of the repo list, or through the extension's settings page.

## Actions per repository

| Action | Trigger | Description |
|--------|---------|-------------|
| Open in VS Code | `Enter` (default; see **Default action**) | Runs `code <path>`, or `code --remote wsl+<distro> <linux path>` for WSL repos |
| Open in terminal | `Ctrl+Enter` (default) | Opens Windows Terminal with the repo's profile (`-p <profile> -d <path>`), so the profile's command line, including any startup command, runs in the repo folder. For WSL repos using a profile that only launches the distro (Terminal's generated profile or a distro app's), runs `wsl.exe -d <distro> --cd <linux path>` with that profile's look instead, since launchers like `ubuntu.exe` start in the home folder. Falls back to `wt.exe` if Terminal isn't a Store install |
| Open folder | Context menu | Opens in Windows Explorer |
| Set terminal profile… | Context menu | Chooses the Windows Terminal profile this repo opens with |
| Set terminal profile for all &lt;distro&gt; repos… | Context menu (WSL repos only) | Chooses the profile every repo in that distro opens with; repos with their own choice keep it |
| Copy path | Context menu | Copies full path to clipboard (the Linux path for WSL repos) |
| Copy Windows path | Context menu (WSL repos only) | Copies the `\\wsl.localhost\...` path |

## How repos are ordered

The list follows Command Palette's own ranking, so repos you use rise to the top:

- **Without a search**, the most used and most recently used repos come first, then the rest alphabetically. Opening a repo in VS Code, a terminal or Explorer counts as a use; copying its path doesn't. A repo's last git activity (commit, checkout, pull, merge or rebase, from `.git/logs/HEAD`) counts towards how recently it was used, but not towards how often, so repos you work on outside the palette rise too.
- **With a search**, how the name matches comes first: an exact name, then names starting with the search, then a word in the name starting with it or its initials (`cpg` for `CommandPalette-Git`), then any fuzzy match of the name, path or location. Within each of those, better matches and more used repos come first.

Usage is kept in `GitExtension.history.json` next to the extension's settings.

## Running the tests

The tests need Windows and the .NET 10 SDK (they target .NET 9, which the .NET 10 SDK can build):

```
dotnet test --project GitExtension.Tests\GitExtension.Tests.csproj -p:Platform=x64
```

They don't touch your real Windows Terminal settings, extension settings or WSL distros: each test uses its own temporary files and a fixed list of distros, and records command lines instead of launching anything.

`LaunchTests` go further: they open real Windows Terminal, VS Code and Explorer windows against your own Terminal settings and Ubuntu distro, check where each one opened, then close them. They're skipped unless you opt in:

```
set GITEXTENSION_LAUNCH_TESTS=1
dotnet test --project GitExtension.Tests\GitExtension.Tests.csproj -p:Platform=x64 --filter-class GitExtension.Tests.LaunchTests
```

They need an `Ubuntu` WSL distro and VS Code with the WSL extension. Each test works in its own scratch folders and closes only the windows it opened (Terminal is told to use a new window, so your existing tabs aren't affected).

## Known limitations

- **Distro app profiles on Windows repos.** A distro app's profile (e.g. the Ubuntu app's) runs a launcher such as `ubuntu.exe`, which ignores the starting folder, so a Windows repo opened with it lands in the Linux home folder. Choose Terminal's generated profile for the distro (e.g. "Ubuntu (WSL)") instead; it runs `wsl.exe`, which opens `C:\...` folders as `/mnt/c/...`.
- **Command Prompt profiles on WSL repos.** cmd.exe can't use a `\\wsl.localhost` folder as its starting directory and starts in `C:\Windows` instead.
- **Windows opening behind the palette.** Command Palette versions bundled before PowerToys 0.101 only let an extension bring windows to the front when you first open its page, and typing a search cancels that, so VS Code and other apps can open behind other windows. Update PowerToys to fix it.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Privacy

This extension operates entirely offline and does not collect any data. See [PRIVACY.md](PRIVACY.md) for details.
