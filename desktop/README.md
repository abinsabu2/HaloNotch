# HaloNotch for Windows (preview)

C# / .NET 10 + [Avalonia](https://avaloniaui.net) port of HaloNotch. Windows first; the same code is meant to run on Linux later.
The Mac app (repo root) is not affected.

**Status – Phase 0:** tray icon (Quit), thin handle at the top centre of the primary screen that expands on hover
to an empty panel with the tab names, hidden from screen capture. No features yet.

## Build & run

```bash
dotnet run --project desktop/HaloNotch.Desktop                 # runs on Windows (and on macOS/Linux for layout checks)
dotnet publish desktop/HaloNotch.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out
```

Requires the .NET 10 SDK. CI (`.github/workflows/desktop.yml`) builds `win-x64` and `win-arm64` zips on every push
touching `desktop/`; on `main` it publishes pre-release `desktop-v<Version>` (version in `HaloNotch.Desktop.csproj`).

## Install (unsigned)

Unzip, run `HaloNotch.exe`. Windows SmartScreen may warn on first launch → **More info → Run anyway**.

## Translations

UI texts come from `shared/translations.json`, generated from the Mac app's `Translations.swift`:
run `./shared/export-translations.sh` (on a Mac) after changing translations. Use `L("English text")` from `Loc.cs`.

## Roadmap

1. **MVP:** open/close animation, dock tabs, clipboard (last 10, skip password managers, Ctrl+Alt+V),
   media (Windows media controls), weather (Open-Meteo), volume/brightness HUD.
2. Files & screenshots, notes, focus timer, screen time, equalizer, camera, usage, calendar (Microsoft Graph).
3. Settings & languages, winget / Microsoft Store, then Linux.
