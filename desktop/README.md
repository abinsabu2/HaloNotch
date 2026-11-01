# HaloNotch for Windows (preview)

C# / .NET 10 + [Avalonia](https://avaloniaui.net) port of HaloNotch. Same code is meant to run on Linux later.
The Mac app (repo root) is not affected.

Windows PCs have no notch: HaloNotch shows a thin handle at the top centre of each display. Move the mouse onto it
and it opens into the panel; drag files onto it to park them. The handle is thin so it doesn't cover the title bars
of maximised windows.

## Features

| Tab | Windows implementation |
|---|---|
| **Dashboard** | Up Next, Now Playing, Weather, Clipboard, Screen Time, Day Progress, Quick controls (Focus optional) – Settings → Dashboard |
| **Weather** | Open-Meteo; Windows location (rounded to ~1 km) or a city |
| **Screen Time** | Foreground app per day, categories by executable, idle > 5 min not counted |
| **Clipboard** | Last 50 items in columns (Text, Code, Links, Files, Media, Colors), pin, search, **Vault** (PIN-encrypted secrets) |
| **Files** | File tray (drop on the handle, drag out, double-click to open) + screenshots from `Pictures\Screenshots` |
| **Media** | Any app using Windows media controls (Spotify, browsers, Media Player …), real equalizer from system audio |
| **Calendar** | ICS links from Outlook / Google / iCloud (Settings → Calendar), Join for Teams / Zoom / Meet |
| **Tools** | Focus timer, notes, camera mirror |
| **Usage** | Claude Code / Codex token usage from local logs (opt-in) |

Also: volume / brightness HUD on the handle, **Ctrl+Alt+V** opens the clipboard (keys 1–0 paste), tray icon menu,
start with Windows, 11 languages (dates follow the chosen language).

## Install (unsigned preview)

1. Download `HaloNotch-Windows-<version>-win-x64.zip` (or `-win-arm64`) from
   [Releases](https://github.com/abinsabu2/HaloNotch/releases) (pre-releases tagged `desktop-v…`).
2. Unzip and run `HaloNotch.exe` (one self-contained file, no .NET install needed).
3. Windows SmartScreen may warn on first launch → **More info → Run anyway**.
4. Optional: Settings → General → **Start with Windows**.

Windows 10 2004 or newer (Windows 11 recommended). Permissions are asked by Windows when a feature is first used:
location (weather), camera (mirror). Brightness works on laptop displays only.

## Build & run

```bash
dotnet run --project desktop/HaloNotch.Desktop -f net10.0-windows10.0.19041.0      # on Windows
dotnet run --project desktop/HaloNotch.Desktop -f net10.0                          # macOS/Linux: UI only (no Windows APIs)
dotnet publish desktop/HaloNotch.Desktop -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

Requires the .NET 10 SDK. Two targets: `net10.0-windows10.0.19041.0` (the real app; `Windows/` and `#if WINDOWS`
code) and `net10.0` (runs anywhere for layout checks).

- **Layout check without Windows:** a debug build with `HALONOTCH_SNAPSHOT=<dir>` renders every tab and the Settings
  window to PNG and quits. `HALONOTCH_DATA=<dir>` points settings/data to another folder (never touches yours).
- **Self-checks:** debug builds assert the clipboard classification, vault crypto round trip and calendar recurrence at startup.
- **CI:** `.github/workflows/desktop.yml` builds `win-x64` and `win-arm64` zips on every push touching `desktop/` or
  `shared/` (download them from the run's artifacts). On `main` it publishes pre-release `desktop-v<Version>`
  (version in `HaloNotch.Desktop.csproj`) unless that version already exists.

## Translations

UI texts come from `shared/translations.json` (generated from the Mac app's `Translations.swift` with
`./shared/export-translations.sh`) plus `translations.desktop.json` for Windows-only texts. Use
`L("English text")` / `LF("…%@…", value)` from `Loc.cs`; every new key needs all 11 languages.

## Credits

Icons: subset of Microsoft's [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT,
`Assets/Fonts/FluentSystemIcons-LICENSE.txt`), bundled so they look the same on Windows 10/11, macOS and Linux.

## Privacy

Everything stays on the PC (`%APPDATA%\HaloNotch`), except: the weather location (rounded) goes to Open-Meteo,
and calendar links are fetched from your calendar provider.

- Clipboard history is in memory only; pinned texts are encrypted for your Windows account (DPAPI).
- Copies marked "don't record" by password managers (and anything copied while one is in front) are never stored.
- Vault: PBKDF2-SHA256 (600k) + AES-GCM, locks after 5 min; copied secrets are excluded from Win+V history and cleared after 30 s.
- The panel is excluded from screen sharing and recordings.

## Known limitations

- Not tested on real Windows hardware yet beyond CI builds – please report issues.
- Calendar: ICS links only (read-only, 15-min refresh). No Microsoft Graph sign-in yet.
- Media artwork/controls depend on the player supporting Windows media controls.
- Screenshots shelf watches `Pictures\Screenshots` only. No AirDrop equivalent (Nearby Share) yet.
- Brightness: built-in laptop displays only (external monitors need DDC/CI).
- No "live" notch while music plays or a meeting is near (the Mac shows art/countdown in the closed notch).

## Distribution

- **GitHub pre-releases** (above) are automatic.
- **winget:** after a release, `wingetcreate new <zip URL>` (installer type `zip`, nested `portable` `HaloNotch.exe`)
  and submit the generated manifest to `microsoft/winget-pkgs`.
- **Microsoft Store:** needs MSIX packaging and a Partner Center account – not set up.
- **Code signing** (removes the SmartScreen warning): needs a certificate or Azure Trusted Signing – not set up.

## Roadmap

1. ✅ MVP: panel, clipboard, media, weather, HUD.
2. ✅ All tabs: files, screenshots, notes, focus, screen time, equalizer, camera, usage, calendar (ICS).
3. ✅ Settings, 11 languages, start with Windows, CI pre-releases. Open: signing, winget/Store submission.
4. Linux (same code; Wayland limits clipboard history, window placement and capture exclusion – see the plan).
