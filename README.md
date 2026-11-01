HaloNotch – README - Version: 0.3 - 11 2026

# HaloNotch

A notch hub for the MacBook – and, as a preview, for Windows. Hover over the notch (on Windows: the thin handle at the
top centre of the screen) → it expands into a panel with nine tabs:

| Tab | What it does |
|---|---|
| **Dashboard** | Up next, now playing, weather, clipboard, screen time, day progress and quick controls at a glance. |
| **Weather** | Current weather, 12 hours and 7 days (Open-Meteo). |
| **Screen Time** | Time per app and category today, app switches, activity by hour – stored only on your computer. |
| **Clipboard** | Last 50 copied items in columns (text, code, links, files, media, colours), pin, search, and a PIN-protected **Vault**. |
| **Files** | Drag files onto the notch to park them (references only), drag them out again; latest screenshots. |
| **Media** | Now playing with controls and a real equalizer (Mac: Spotify / Apple Music; Windows: any player). |
| **Calendar** | Month view and your events, with a Join button for Teams / Zoom / Meet. |
| **Tools** | Focus timer, quick notes, camera mirror. |
| **Usage** | Claude Code / Codex token usage from local logs (opt-in). |

Plus a **volume / brightness HUD**, and a clipboard shortcut (Mac ⌃⌥V, Windows Ctrl+Alt+V; keys 1–0 paste).

| Platform | Download | Details |
|---|---|---|
| **macOS** 14+ | `HaloNotch-<version>.zip` on [Releases](https://github.com/abinsabu2/HaloNotch/releases) (tags `v…`) | below |
| **Windows** 10/11 (preview) | `HaloNotch-Windows-<version>-win-x64.zip` / `-win-arm64.zip` (pre-releases `desktop-v…`) | [`desktop/README.md`](desktop/README.md) |

## Screenshots

| | |
|---|---|
| ![Dashboard](docs/screenshots/dashboard.png) | ![Weather](docs/screenshots/weather.png) |
| ![Screen Time](docs/screenshots/screenTime.png) | ![Clipboard](docs/screenshots/clipboard.png) |
| ![Files](docs/screenshots/files.png) | ![Media](docs/screenshots/media.png) |
| ![Calendar](docs/screenshots/calendar.png) | ![Tools](docs/screenshots/tools.png) |

## Install on macOS (app is not notarized)

HaloNotch is not signed with an Apple Developer ID, so macOS warns on first launch. Two ways to use it:

**Option 1 – build from source (no warning)**

```bash
xcode-select --install          # once, if Xcode is not installed
git clone https://github.com/abinsabu2/HaloNotch.git
cd HaloNotch
./build.sh
open build/HaloNotch.app
```

Apps built on your own Mac are not quarantined, so Gatekeeper does not block them.

**Option 2 – download the prebuilt zip**

1. Download `HaloNotch-<version>.zip` from [Releases](https://github.com/abinsabu2/HaloNotch/releases), unzip, move `HaloNotch.app` to `/Applications`.
2. Open it. macOS shows *"Apple could not verify HaloNotch…"* → click **Done**.
3. Go to **System Settings → Privacy & Security**, scroll down, click **Open Anyway** and confirm. (On macOS 15+ right-click → Open no longer bypasses the warning.)

   Or in Terminal:
   ```bash
   xattr -dr com.apple.quarantine /Applications/HaloNotch.app
   ```

Note: because the build is only ad-hoc signed, macOS may ask for permissions again after each update.

## Install on Windows (preview, unsigned)

1. Download `HaloNotch-Windows-<version>-win-x64.zip` (Intel/AMD) or `-win-arm64.zip` (ARM) from the
   [Releases](https://github.com/abinsabu2/HaloNotch/releases) page – Windows builds are pre-releases tagged `desktop-v…`.
2. Unzip and run `HaloNotch.exe` (one self-contained file, no installer, no .NET needed).
3. Windows SmartScreen may warn on first launch → **More info → Run anyway**.
4. Move the mouse to the top centre of the screen. Settings → General → **Start with Windows** if you like.

Calendar on Windows uses ICS links (Settings → Calendar). Build, privacy and limitations: [`desktop/README.md`](desktop/README.md).

## Build & run

Requires macOS 14+ (target: macOS 26 Tahoe) and Xcode or the Command Line Tools.

```bash
cd HaloNotch
./build.sh
open build/HaloNotch.app
```

Always start the app via `build/HaloNotch.app` – the Calendar and Automation prompts need the Info.plist that only the app bundle contains.

## Permissions (asked on first use)

| Permission | Needed for | Where to change |
|---|---|---|
| Paste from Other Apps | Clipboard history | Privacy & Security → Paste from Other Apps |
| Automation (Music / Spotify) | Media tab | Privacy & Security → Automation |
| Calendars (full access) | Calendar tab – click "Allow Calendar Access" | Privacy & Security → Calendars |

The app is only ad-hoc signed. After a rebuild macOS may ask for these permissions again.

## Menu bar

Clear Clipboard History · Clear File Tray · Show Volume HUD (on/off) · Show Brightness HUD (on/off) · Quit

## Privacy

- **Clipboard history** stays in memory. Pinned texts are stored in the login keychain. Copies marked as concealed (password managers) are never stored.
- **Vault** (Clipboard → Vault): secrets are encrypted with your PIN or password (PBKDF2-SHA256 + AES-GCM) in `~/Library/Application Support/HaloNotch/vault.json`. It locks after 5 min, on sleep and on screen lock. Copied secrets are cleared from the clipboard after 30 s. A forgotten PIN cannot be recovered; the vault can only be erased.
- **Weather**: your approximate location (rounded to about 1 km) or the city you type is sent to [Open-Meteo.com](https://open-meteo.com) to get the forecast. Nothing else leaves the Mac.
- **Usage tab**: reads Claude Code / Codex session logs only after you enable it. Only token counts are kept.
- The notch panel is excluded from screen sharing and screen recordings.

## Known limitations

- History and file tray are kept in memory only (cleared on quit).
- Media: only Spotify and Apple Music (AppleScript). No artwork yet. Browser audio is not shown.
- HUD: the system's own volume/brightness overlay is still shown as well (hiding it is not supported by macOS).
- Brightness uses the private DisplayServices API – it may stop working with a macOS update; it is then disabled automatically. Auto-brightness changes can also trigger the HUD – switch it off in the menu bar if that bothers you.
- Not suitable for the Mac App Store (private API).

---

Author: Abin Sabu (based on [Tenfold](https://github.com/sivaprasad1988/Tenfold) by Siva, MIT License)
