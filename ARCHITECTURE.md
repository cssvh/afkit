# AFKit — Technical documentation

> This document describes **how AFKit is built inside**: architecture, data flow, persistence, injected scripts, localization and extension points. For the feature list from the user's point of view, shortcuts and installation, see [README.md](README.md).

## Overview

**AFKit** is a browser profile manager for Windows. Each profile is an independent Chromium session (Microsoft Edge **WebView2**) that runs **inside the app window**, with its own data folder: cookies, logins, history, cache, and extensions are isolated per profile.

Besides the multi-profile browser, version 2 brings site-specific integrations:

| Site | What AFKit does |
|---|---|
| **pokeidle.io** | A floating battle window per profile (Game Boy Advance style), automatic immersive mode in the grid and watermarks with session and captures. |
| **twitch.tv / kick.com** | Saves and restores the player's sound (muted/volume) and the live's quality when the session reopens. |

| Item | Value |
|---|---|
| Language / UI | C# 12, WPF (.NET 8, `net8.0-windows`) |
| Embedded browser | `Microsoft.Web.WebView2` 1.0.2903.40 |
| Other dependencies | WinForms only for the tray icon |
| Version | 2.0.0 (`src/AFKit/AFKit.csproj`) |
| Platform | Windows 10/11, x64 |
| UI languages | English, Português (Brasil), Español |

## Folder structure

```
AFKit/
├─ AFKit.sln
├─ Install AFKit.cmd          ← double-click: calls install.ps1
├─ install.ps1                ← builds (dotnet publish) and installs to %LOCALAPPDATA%\Programs\AFKit
├─ README.md                  ← user manual
├─ ARCHITECTURE.md            ← this file
└─ src/AFKit/
   ├─ App.xaml(.cs)           ← single instance, WebView2 Runtime check, theme, language startup
   ├─ MainWindow.xaml(.cs)    ← main window (partial class split by topic)
   │  ├─ MainWindow.Sidebar.cs     collapsible sidebar (animation, avatar strip)
   │  ├─ MainWindow.Features.cs    CPU/memory, mute all, tray, network, language switch, profile dragging
   │  ├─ MainWindow.GridSwap.cs    swapping grid cells
   │  ├─ MainWindow.Taskbar.cs     battle windows (one per profile)
   │  └─ MainWindow.Extensions.cs  opens the extensions dialog
   ├─ Controls/
   │  ├─ ProfileView.xaml(.cs)     a profile's session: WebView2 environment, tabs, address bar
   │  │  ├─ ProfileView.TabDrag.cs     reorder tabs by dragging
   │  │  ├─ ProfileView.Grid.cs        bridge with grid-overlay.js
   │  │  ├─ ProfileView.GridDrag.cs    colored frame and dragging the cell header
   │  │  ├─ ProfileView.Taskbar.cs     bridge with taskbar-probe.js (sprites, battle state)
   │  │  ├─ ProfileView.Live.cs        bridge with live-state.js
   │  │  └─ ProfileView.Extensions.cs  syncing extensions with the browser profile
   │  ├─ BrowserTab.cs             a tab's state (WebView2, mute, background mode, failures…)
   │  ├─ BattleCell.xaml(.cs)      GBA battle scene (sprites, platforms, HP/EXP boxes)
   │  └─ BattleInfo.xaml(.cs)      session + captures strip below the battle
   ├─ Dialogs/
   │  ├─ ProfileEditorWindow       create/edit a profile (name, color, icon, start URL)
   │  ├─ GroupEditorWindow         create/edit a group
   │  ├─ CredentialDialog          HTTP basic authentication login
   │  ├─ ExtensionsWindow          the profile's "Extensions…"
   │  ├─ BattleWindow              a profile's battle window (current)
   │  └─ TaskbarWindow             single window with several cells (old V2 model, no longer opened)
   ├─ Models/
   │  ├─ Models.cs                 Profile, ProfileGroup, TabSetting, ExtensionRecord, AFKitData
   │  └─ Taskbar.cs                PokemonInfo, GameSnapshot, SessionSummary, CaptureRow, TaskbarCell, CssColor
   ├─ Services/
   │  ├─ ProfileStore.cs           persistence in afkit.json
   │  ├─ ProfileArchiveService.cs  export/import .afkitprofile
   │  ├─ ExtensionService.cs       Chrome Web Store, .crx/.zip, folders, syncing
   │  ├─ TrayIcon.cs               tray icon and menu (WinForms NotifyIcon)
   │  ├─ WindowTheme.cs            dark title bar via DWM
   │  ├─ Loc.cs                    localization: language choice, lookup, {loc:T key} XAML extension
   │  └─ Strings*.cs               all UI texts, one file per area
   └─ Assets/
      ├─ afkit.ico
      ├─ Fonts/PressStart2P-Regular.ttf (+ OFL license)
      ├─ taskbar-probe.js          (embedded resource AFKit.TaskbarProbe.js)
      ├─ grid-overlay.js           (embedded resource AFKit.GridOverlay.js)
      └─ live-state.js             (embedded resource AFKit.LiveState.js)
```

## Architecture

```
┌──────────────────────────── MainWindow ────────────────────────────┐
│  Sidebar (groups/profiles)       │   Main area                      │
│                                  │   • 1 active ProfileView, or     │
│  ProfileStore ◄── afkit.json     │   • grid of up to 4 ProfileViews │
└──────────────┬───────────────────┴──────────────┬───────────────────┘
               │ 1 per open profile               │
        ┌──────▼──────────────────────────────────▼──────┐
        │ ProfileView                                     │
        │  CoreWebView2Environment (Profiles\{id} folder, │
        │  Chromium arguments, extensions)        │
        │   ├─ BrowserTab ─ WebView2 ─┐                   │
        │   ├─ BrowserTab ─ WebView2  │ injected scripts  │
        │   └─ …                      │ + postMessage     │
        └─────────────────────────────┼───────────────────┘
                                      ▼
                      BattleWindow (1 per profile, optional)
```

### Startup (`App.xaml.cs`)

1. **Restart handshake**: the language switch starts a new process with `--wait-pid <old pid>`; the new process waits (up to 30 s) for the old one to exit so the single-instance mutex is free.
2. **Data and language**: `ProfileStore.MigrateLegacyData()` moves the old `%LOCALAPPDATA%\Deck` folder to `AFKit` (only when the new folder has no real data), then `Loc.Initialize(...)` reads the saved language from `afkit.json` — before any window exists, because XAML texts are resolved while a window loads.
3. **Single instance**: a named `Mutex` called `AFKit.SingleInstance.{user}`. If an AFKit is already running, the new instance signals an `EventWaitHandle` (`…Show`) and exits; the original instance listens for that signal on a background thread and calls `RestoreFromTray()`. A second signal, `…Exit`, makes it exit for real (saving the sessions, like "Exit AFKit" in the tray); `install.ps1 -Auto` (`Update AFKit.cmd`) uses it to close the app before updating, then reopens the new version.
4. **WebView2 Runtime**: `CoreWebView2Environment.GetAvailableBrowserVersionString()`; if missing, it offers the download page and exits.
5. **Theme**: `WindowTheme.RegisterForAllWindows()` applies a dark title bar (DWM) to every window that loads.
6. Unhandled dispatcher exceptions become a `MessageBox` (the app does not close).

### Main window (`MainWindow`)

- Keeps one `ProfileView` per profile with an open session (`_views`, created on demand by `EnsureView`).
- **Single mode**: shows the active profile. **Grid mode** (Ctrl+G): up to 4 pinned profiles (`ProfileStore.MaxPinned`), with 1, 2, 2+1 and 2×2 layouts; draggable dividers saved in `GridColumnRatio`/`GridRowRatio`.
- Periodic timers measure CPU and memory per profile (adding up each session's WebView2 processes), and update the footer and the tray tooltip.
- Closing with the X sends it to the tray (`CloseToTray`): the window leaves the screen but stays "visible" to Chromium, so nothing is paused.

### A profile's session (`ProfileView`)

`InitializeAsync()` creates a `CoreWebView2Environment` with:

- **Data folder**: `%LOCALAPPDATA%\AFKit\Profiles\{id}` — this is what guarantees isolation.
- **Chromium arguments**: `--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --autoplay-policy=no-user-gesture-required` (pages keep running off screen; lives may play on their own).
- `AreBrowserExtensionsEnabled = true`.


On opening, the saved tabs (`Profile.SavedTabs`) are recreated in order. The first tab is born blank so the extensions are applied before any navigation. Each new tab (`CreateTabAsync`) receives, **before navigating**, the three injected scripts (see below), and inherits the profile's mute.

#### Background mode per tab (`BackgroundMode`)

| Mode | Behavior off screen |
|---|---|
| `Light` (default) | Does not draw (not even video), but scripts and timers keep running. |
| `Active` | Keeps drawing as if it were on screen. |
| `Suspended` | Freezes the page (`TrySuspend`) until it is back on screen. |

Tabs off screen also ask the browser for a low memory level.

#### Automatic recovery

`OnNavigationResult` classifies the result of each navigation:

- HTTP 5xx → "site is down";
- `Disconnected` / `ConnectionAborted` / `ConnectionReset` → "the connection dropped";
- `CannotConnect` / `HostNameNotResolved` / `ServerUnreachable` / `Timeout` → "no response";
- cancellations, credential requests and certificate errors are ignored (reloading does not fix them).

On failure, `HandleFailure` schedules a new load with growing waits (**5 s, 15 s, 30 s, 1, 2 and 5 min**). With no network at all, the tab goes to `WaitingForNetwork` and reloads as soon as `OnNetworkRestored()` is called. If the profile's whole browser process crashes, `MainWindow.RestartCrashedSessionAsync` reopens the session.

## Localization

All UI text lives in `Services/Strings*.cs`, one `Add("Area.Key", english, português, español)` line per text, so the three translations sit next to each other. `Loc` resolves them.

- **C#**: `Loc.T("Area.Key")` or `Loc.T("Area.Key", arg0, arg1…)` (`string.Format` placeholders).
- **XAML**: `xmlns:loc="clr-namespace:AFKit.Services"` and `Text="{loc:T Area.Key}"` (works in `Header`, `Content`, `ToolTip`, `Tag`, `Title` and `Setter Value`).
- **Injected scripts**: keys starting with `Js.` are sent to pages as `window.__afkitText` (without the prefix) — `ProfileView.LocalizeScript` prepends them to `grid-overlay.js`.
- **Choosing the language**: `AFKitData.Language` (`pt-BR`, `en`, `es`; null = follow Windows). The app reads it at startup (`Loc.PeekSavedLanguage`) and changing it restarts the app (`MainWindow.ChangeLanguage`), so every text — including ones set once in code — is rebuilt in the new language. Missing or empty translations fall back to English, then to the key itself.
- **Names that stay as they are**: the game's own DOM ids, localStorage keys and message fields (Portuguese, because they belong to PokeIdle) are never translated.

**Adding a language**: add its code and name to `Loc.Languages`/`Loc.Normalize`, add a fourth parameter to `Loc.Add`/`Strings.Add` and fill it in every `Add(...)` line (the compiler flags the ones you miss).

## Persistence

```
%LOCALAPPDATA%\AFKit\             (or the folder in the AFKIT_DATA_DIR environment variable)
  afkit.json                    ← AFKitData: groups, profiles, tabs, preferences, language
  afkit.json.corrupt-YYYYMMDD…  ← copy made if afkit.json cannot be read
  Profiles\{id}\               ← each profile's Chromium data
  Extensions\{id}\{version}\   ← unpacked extensions (shared between profiles)
%LOCALAPPDATA%\Programs\AFKit\  ← binaries installed by install.ps1
```

- `ProfileStore.Save()` writes to `afkit.json.tmp` and does `File.Move(..., overwrite: true)` — atomic write.
- Enums are saved as text (`JsonStringEnumConverter`).
- Per tab (`TabSetting`, indexed by position in `SavedTabs`): reload every N min, background mode, zoom and the live's last state (mute, volume, quality).
- Fields marked `[JsonIgnore]` (`IsLoaded`, `IsActive`, `UsageText`…) are runtime only.
- `AFKitData.TaskbarOverlay`/`TaskbarBounds` belong to the old single window; they are only preserved so no data is lost — today each `Profile` keeps its own.

### `.afkitprofile` backup (`ProfileArchiveService`)

A zip with the profile manifest (JSON) and the browser data folder, skipping caches (`Cache`, `Code Cache`, `GPUCache`, `*ShaderCache`, `Dawn*Cache`, `Crashpad`, `component_crx_cache`, `Service Worker\CacheStorage`) and `lockfile` files. On import the profile gets a new ID. Old `.deckprofile` files (same format) can still be imported.

## Extensions (`ExtensionService`)

- **Sources**: Chrome Web Store link/ID (downloads from `clients2.google.com/service/update2/crx`), a `.crx`/`.zip` file or an unpacked folder with `manifest.json`.
- The `.crx` is read by hand: CRX2/CRX3 header (in CRX3 the public key is extracted from the protobuf) to compute the extension ID; the rest is the zip.
- The package is unpacked to `Extensions\{id}\{version}`, from where WebView2 loads the "unpacked" extension — so the folder has to keep existing. Folders no profile uses are removed (`DeleteFolderIfUnused`).
- `Profile.Extensions` is AFKit's record; `SyncAsync` aligns it with the `CoreWebView2Profile` (installs, enables/disables, removes). With the session closed, changes stay pending (`RemovedExtensionIds`, records without an `Id`) and are applied on opening.
- WebView2 limitations: no extension button/popup in the toolbar; options open as a tab; some Chrome APIs do not exist.

## Injected scripts

The three files in `Assets/` are **resources embedded** in the assembly and injected into every tab with `AddScriptToExecuteOnDocumentCreatedAsync`. Each one checks the domain and only acts on its own site (and only on the main page, never in iframes). Communication uses `window.chrome.webview.postMessage` (page → AFKit) and `CoreWebView2.PostWebMessageAsString/AsJson` (AFKit → page). Every message from the page has an `afkit` field that identifies the channel.

### `taskbar-probe.js` — channel `tb` (pokeidle.io)

Read-only: it does not click, send anything to the server or change the game.

- **Your Pokémon**: reads the "Em campo" panel (`#p-ativo`): name, level, HP, EXP and the sprite canvas.
- **Enemy**: listens to the `campo` and `batalha` messages the server already sends over the page's WebSocket.

| Message | Content |
|---|---|
| `{afkit:'tb', type:'state', me, foe}` | `me`/`foe`: `{name, lv, hp, mhp, xp?, xpMax?, shiny, key}` or `null` |
| `{afkit:'tb', type:'sprite', key, frames, ms}` | Sprite frames as data URLs (`out:<sheet>:<direction>`) or the move's (`fx:<TYPE>`) |
| `{afkit:'tb', type:'hit', fx, damage}` | Your Pokémon hit the current enemy (triggers the move animation) |
| AFKit → page `afkit-tb-resync` | Asks to resend the state and the sprites |

`ProfileView.Taskbar.cs` decodes the frames into `BitmapImage`s, keeps them in a per-key cache and exposes `GetTaskbarData()`; `MainWindow.RefreshTaskbar()` opens, updates or closes the `BattleWindow`s (one per profile with `ShowInTaskbar`, an open session and the game in a tab — the tab pinned to the grid or, without one, the first with PokeIdle).

### `grid-overlay.js` — channel `grid` (pokeidle.io)

| AFKit → page (strings) | Meaning |
|---|---|
| `afkit-grid:1` / `afkit-grid:0` | The tab entered/left a grid cell |
| `afkit-info:1` / `afkit-info:0` | AFKit wants/no longer wants the data for the battle window |
| `afkit-vis:1` / `afkit-vis:0` | The tab is/is not visible on screen |
| `afkit-grid-ping` | Asks for `ready` again |
| `afkit-eco:1` / `afkit-eco:0` | Turn the game's Economy Mode on/off (cell header button, `HeaderEconomyButton`) |

| Page → AFKit | Content |
|---|---|
| `{type:'ready'}` | Game ready (session and captures scenes present, user logged in) |
| `{type:'eco', on}` | Economy Mode state (with `ready` and on every change); stored in `BrowserTab.Economy`, shows the header button only for game tabs |
| `{type:'summary', time, xp, gold, profit}` | Formatted session summary texts (at most 1/s, only when it changes) |
| `{type:'caps', record, recent}` | Session record + up to 5 recent captures (portrait, level, time, seals, border color) |

In the grid, it turns on the game's own **Immersive Mode** (respecting it if the user leaves it manually — noted in `sessionStorage`), draws the watermarks over the battle (with `pointer-events` off, except on the summary for the hover) and keeps the game's panels open underneath only when needed for the numbers to keep updating. AFKit only sends a signal when the state changes (`ProfileView.Grid.cs` → `Signal`).

The watermarks (summary card and captures) and the cell layout exist **only while the tab is in the grid with Immersive Mode on and Economy Mode off** (`inGridImmersive()`): leaving immersive through the game's button removes them right away. Economy Mode is the game's own (no scene, just the numbers panel); the header button drives it through the game's buttons (`#hunt-eco`/`#arena-eco` to turn it on, `#eco-sair` to turn it off), turns immersive off while it lasts and, if AFKit was the one that turned it on, turns it off when the tab leaves the grid.

The summary card also shows defeated Pokémon (total and per hour/second), captures (total and per hour/second, computed from the session time) and shinies seen/caught, read from the session panel's `data-k` cells (`abates`, `capturas`, `shiniesVistos`, `shinies`) and `#sessao-abates-h`; rows with no value (e.g. shinies where they cannot drop) stay hidden.

Above the captures list there is a pokéball button (shows/hides the list; saved in the page's localStorage `afkit-grid-caps-hidden`) and a **Clear all** button. Clear all never touches the game's history: it saves the time in `afkit-grid-caps-cleared:<nick>` and captures older than that stop being listed (in the grid and in the battle window); the game's record is ignored afterwards in favor of the best remaining capture.

### `live-state.js` — channel `live` (twitch.tv, kick.com)

| Message | Content |
|---|---|
| Page → AFKit `{type:'hello'}` | Start of each page; AFKit answers with the saved state |
| Page → AFKit `{type:'state', site, muted, volume, quality}` | Current state (at most every 2 s, only when it changes) |
| AFKit → page `{afkitLive:{muted, volume, quality}}` | State to reapply |

When the session reopens, the state is also injected before the page is born (`window.__afkitLive = …`), so the quality is rewritten before the player loads: on Kick in `sessionStorage['stream_quality']`; on Twitch in `localStorage['video-quality']`, `video-muted` and `volume` (only keys that already exist, in the site's format). Mute and volume are also applied straight to the video element.

## Battle window (`BattleWindow` + `BattleCell` + `BattleInfo`)

- One transparent, borderless WPF window per profile; drag with the left button, resize from the corner, the right button has *Overlay (always on top)* and *Hide*.
- Position and size are saved (debounced) in `Profile.TaskbarBounds`; `Profile.TaskbarOverlay` keeps the "always on top". New windows are stacked from the bottom-right corner (`DefaultBattlePlace`), opening another column when they do not fit.
- `TaskbarCell` (in `Models/Taskbar.cs`) is the observable view-model: texts, HP/EXP ratio, current sprite frame, bar color (green > 50%, yellow down to 20%, red below).
- Pixel font **Press Start 2P** (OFL license, in `Assets/Fonts`).

## Build, run and install

```powershell
# development
dotnet run --project src\AFKit

# release build (the same the installer does)
dotnet publish src\AFKit -c Release -r win-x64 --self-contained false -o "$env:LOCALAPPDATA\Programs\AFKit"
```

Or open `AFKit.sln` in Visual Studio 2022. Set `AFKIT_DATA_DIR` to run against a throwaway data folder (the legacy `Deck` migration is skipped then).

`Install AFKit.cmd` → `install.ps1`:

1. Checks for `dotnet` (suggests `winget install Microsoft.DotNet.SDK.8` if missing).
2. If AFKit is open, asks to quit through the tray (saving the tabs) and waits up to 2 min; then offers to force it closed. The same goes for the old `Deck`.
3. Publishes to `%LOCALAPPDATA%\Programs\AFKit`.
4. Creates Start Menu and desktop shortcuts, removes the old name's, and offers to open the app.

Profiles and logins (`%LOCALAPPDATA%\AFKit`) are never touched by the installer.

Requirements: Windows 10/11, .NET 8 SDK (to build), WebView2 Runtime (already present in Windows 11).

## Code conventions

- **Language**: code, identifiers and comments are in English. UI texts live in `Strings*.cs` (never hard-coded in C#/XAML). The only Portuguese left in code belongs to PokeIdle itself (its DOM ids, storage keys and message fields).
- **Partial classes by topic**: `MainWindow.*.cs` and `ProfileView.*.cs` split a large class into themed files.
- **Observable models**: `Observable` (base with `Set`/`Raise`) implements `INotifyPropertyChanged` for XAML binding.
- **Usings**: `System.IO` is included globally; `System.Windows.Forms` and `System.Drawing` are removed from the implicit usings to avoid conflicts with WPF (use the alias `Forms = System.Windows.Forms` when needed).
- **V2 features** are marked with `V2:` comments in the code.
- Injected scripts: IIFE with `'use strict'`, a guard against double injection (`window.__afkit…`) and a domain check right at the start; the protocol is documented in each file's header.

## Known limitations

- Isolation is of **data**, not of fingerprint: all profiles share the user agent and machine characteristics.
- Extensions depend on WebView2 support (no popup/toolbar icon; not every API exists).
- When importing a backup on another machine, extensions may show up as "not loaded" and need to be installed again.
- PokeIdle tabs opened before an AFKit update need to be reloaded once to receive the new scripts.
