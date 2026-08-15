# DeepSeek Harness Launcher

[![CI](https://github.com/Lonivxy/deepseek-harness-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/Lonivxy/deepseek-harness-launcher/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10+-512BD4?style=flat&logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-12+-239120?style=flat&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![WinForms](https://img.shields.io/badge/WinForms-Windows%20GUI-0078D6?style=flat&logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/winforms/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A friendly Windows desktop companion for [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness).
No command windows, no browser tabs — just a clean GUI that starts the engine, streams its logs,
checks for updates, and opens the interface in the browser of your choice.

> **Note:** this project manages and launches DeepSeek Harness; it is not affiliated with DeepSeek AI.

## Features

- **Built-in log panel** — live engine output inside the GUI (backend + web UI, which share one process).
- **One-click harness installer** — if DeepSeek Harness is missing, the launcher
  offers to download, install dependencies, and build it automatically, so brand-new
  users never need to touch a terminal. Uses a faster China mirror automatically
  where the official npm registry is slow.
- **Status indicator** — shows whether your DeepSeek Harness installation is up to date.
- **Auto-open** — the DS Harness interface opens by itself the moment the engine
  is ready (toggle in the settings row; a note appears before the engine starts).
- **Main action buttons**:
  1. **Check Update** — compares the installed harness against the latest version
     (works even where GitHub is blocked, via a CDN mirror).
  2. **Restart Backend** — stops and restarts the engine cleanly (no orphaned processes).
  3. **Open DS Harness** — opens the interface in a standalone app-style browser window.
  4. **Install Node.js** — appears automatically when Node.js is missing.
  5. **Install Harness** — appears automatically when the harness is missing.
- **First-run wizard** — choose Chrome or Edge, with a "Remember my choice" checkbox.
- **Prerequisite check** — detects missing Node.js / Git / pnpm at startup. Missing
  Node.js triggers a **one-click auto-install** of nvm-windows + Node.js LTS
  (per-user, no admin rights needed); pnpm is installed automatically via npm.
  After the Node.js environment is set up, the launcher continues straight to
  building and starting the engine.
- **Guaranteed cleanup** — the engine runs inside a Windows Job Object; closing the app kills
  the entire process tree (`cmd → pnpm → node`), so nothing is left running.
- **Close confirmation** — before closing, the launcher asks for confirmation, then stops the
  engine **and** closes the DS Harness interface window it opened — without touching any other
  Chrome / Edge windows you may have open.
- **Self-healing engine start** — the launcher automatically retries if the engine's
  first boot hits a transient Windows file-lock issue.

## Screenshot

*Add a screenshot here once you have one — this makes the README much friendlier.*

## Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (only needed to build from source)
- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) installed locally
  (or let the launcher install it for you on first run)
- Git (the launcher checks for it and points you to the installer if missing)
- Chrome or Edge installed

## Quick start (end users)

1. Download the latest `DeepSeekHarnessLauncher.exe` from the **Releases** page.
2. Run it. On first launch a wizard asks which browser to use.
3. If Node.js is missing, the launcher asks whether to install it automatically —
   it sets up nvm-windows + Node.js LTS in one click (no admin rights needed).
4. If DeepSeek Harness isn't installed, the launcher offers to download and set
   it up automatically — just click **Yes** and wait a few minutes.
5. Click **Open DS Harness** once the engine reports online and enter your API key
   in the harness's own settings.

Closing the app stops the engine. No console windows, no leftover processes.

## Manual setup (alternative to the one-click installer)

If you prefer to install DeepSeek Harness yourself:

```powershell
git clone https://github.com/deepseek-ai/deepseek-harness.git D:\dsh
cd D:\dsh
pnpm install
pnpm run build
```

Then start DeepSeek Harness Launcher. The default engine path is `D:\dsh`; change it in
`%APPDATA%\DeepSeekHarnessLauncher\config.json` if your checkout lives elsewhere.

## Build from source

```powershell
dotnet build src/DshLauncher/DshLauncher.csproj -c Release
```

For a standalone single-file exe (no .NET runtime needed on the target machine):

```powershell
dotnet publish src/DshLauncher/DshLauncher.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Configuration

| Setting | Location | Purpose |
|---|---|---|
| App settings | `%APPDATA%\DeepSeekHarnessLauncher\config.json` | Browser choice, harness path, port |

The API key is entered in the harness itself, so it never passes through this launcher.

## Release workflow

Pushing a version tag builds the standalone exe in GitHub Actions and attaches it to a release:

```powershell
git tag v1.4.5
git push origin v1.4.5
```

See [.github/workflows/release.yml](.github/workflows/release.yml).

Power users can also run the installers headlessly (used by CI):

```powershell
DeepSeekHarnessLauncher.exe --install-harness "D:\dsh"   # clone + install + build the harness
DeepSeekHarnessLauncher.exe --install-node               # install nvm-windows + Node.js LTS
```

## Project structure

```
deepseek-harness-launcher/
├── .github/
│   └── workflows/
│       ├── ci.yml          # builds on every push/PR
│       └── release.yml     # builds + publishes releases on version tags
├── src/
│   └── DshLauncher/
│       ├── DshLauncher.csproj
│       ├── Program.cs              # entry point (+ --install-harness / --install-node CLI modes)
│       ├── app.manifest            # DPI awareness, non-admin
│       ├── Assets/
│       │   └── app.ico             # app icon
│       ├── Models/
│       │   └── AppConfig.cs        # persisted settings
│       ├── Services/
│       │   ├── BackendService.cs   # engine lifecycle + Job Object cleanup
│       │   ├── BrowserService.cs   # Chrome/Edge app-window launcher
│       │   ├── ConfigService.cs    # config.json
│       │   ├── HarnessInstallerService.cs  # one-click clone/install/build
│       │   ├── NodeEnvInstallerService.cs  # one-click nvm-windows + Node.js LTS
│       │   ├── PrerequisitesService.cs  # Node.js / pnpm detection
│       │   └── UpdateService.cs    # version comparison + CDN fallback
│       └── Forms/
│           ├── MainForm.cs         # main window
│           └── FirstRunWizard.cs   # browser choice wizard
├── .gitignore
├── LICENSE
└── README.md
```

## Roadmap

- One-click harness *updates* (first-time install is done; add updates next).
- Settings page for engine path/port (instead of editing config.json).
- Custom hotkey to show/hide the launcher.
- Dark/light theme toggle.

## Contributing

Pull requests are welcome. Keep code simple, comment the "why", and make sure
`dotnet build -c Release` passes before opening a PR.

## License

[MIT](LICENSE)
