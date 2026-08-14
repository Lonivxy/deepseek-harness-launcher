# DSH Launcher

[![CI](https://github.com/YOUR_USERNAME/dsh-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/YOUR_USERNAME/dsh-launcher/actions/workflows/ci.yml)

A friendly Windows desktop companion for [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness).
No command windows, no browser tabs — just a clean GUI that starts the engine, streams its logs,
manages your API key, checks for updates, and opens the interface in the browser of your choice.

> **Note:** this project manages and launches DeepSeek Harness; it is not affiliated with DeepSeek AI.

## Features

- **Built-in log panel** — live engine output inside the GUI (backend + web UI, which share one process).
- **Status indicator** — shows whether your DeepSeek Harness installation is up to date.
- **Three main buttons**:
  1. **Check Update** — compares the installed harness against the latest GitHub release.
  2. **Restart Backend** — stops and restarts the engine cleanly (no orphaned processes).
  3. **Open DS Harness** — opens the interface in a standalone app-style browser window.
- **First-run wizard** — choose Chrome or Edge, with a "Remember my choice" checkbox.
- **Prerequisite check** — detects missing Node.js / pnpm at startup and points
  new users to the correct install pages instead of failing silently.
- **Guaranteed cleanup** — the engine runs inside a Windows Job Object; closing the app kills
  the entire process tree (`cmd → pnpm → node`), so nothing is left running.

## Screenshot

*Add a screenshot here once you have one — this makes the README much friendlier.*

## Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (only needed to build from source)
- [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) installed locally
  (see one-time setup below)
- Chrome or Edge installed

## Quick start (end users)

1. Download the latest `DSHLauncher.exe` from the **Releases** page.
2. Run it. On first launch a wizard asks which browser to use.
3. Click **Open DS Harness** once the engine reports online and enter your API key
   in the harness's own settings.

Closing the app stops the engine. No console windows, no leftover processes.

## One-time setup: install DeepSeek Harness

If you have not installed the harness yet:

```powershell
git clone https://github.com/deepseek-ai/deepseek-harness.git D:\dsh
cd D:\dsh
pnpm install
pnpm run build
```

Then start DSH Launcher. The default engine path is `D:\dsh`; change it in
`%APPDATA%\DshLauncher\config.json` if your checkout lives elsewhere.

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
| App settings | `%APPDATA%\DshLauncher\config.json` | Browser choice, harness path, port |

The API key is entered in the harness itself, so it never passes through this launcher.

## Release workflow

Pushing a version tag builds the standalone exe in GitHub Actions and attaches it to a release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

See [.github/workflows/release.yml](.github/workflows/release.yml).

## Project structure

```
dsh-launcher/
├── .github/
│   └── workflows/
│       ├── ci.yml          # builds on every push/PR
│       └── release.yml     # builds + publishes releases on version tags
├── src/
│   └── DshLauncher/
│       ├── DshLauncher.csproj
│       ├── Program.cs              # entry point
│       ├── app.manifest            # DPI awareness, non-admin
│       ├── Assets/
│       │   └── app.ico             # app icon
│       ├── Models/
│       │   └── AppConfig.cs        # persisted settings
│       ├── Services/
│       │   ├── BackendService.cs   # engine lifecycle + Job Object cleanup
│       │   ├── BrowserService.cs   # Chrome/Edge app-window launcher
│       │   ├── ConfigService.cs    # config.json + .env API key
│       │   └── UpdateService.cs    # GitHub release comparison
│       └── Forms/
│           ├── MainForm.cs         # main window
│           ├── FirstRunWizard.cs   # browser choice wizard
│           └── ApiKeyDialog.cs     # masked key input + confirmation
├── .gitignore
├── LICENSE
└── README.md
```

## Roadmap

- Auto-download and install harness updates from the GUI.
- Settings page for engine path/port (instead of editing config.json).
- Custom hotkey to show/hide the launcher.
- Dark/light theme toggle.

## Contributing

Pull requests are welcome. Keep code simple, comment the "why", and make sure
`dotnet build -c Release` passes before opening a PR.

## License

[MIT](LICENSE)
