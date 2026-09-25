# Vibra

Per-game digital vibrance for Windows. Games look more colorful, your desktop stays normal, and it all happens automatically.

- **Per game.** Each game gets its own vibrance level (Valorant 85%, CS2 100%, …).
- **Per monitor.** Only the monitor the game is on changes. YouTube on your second screen keeps normal colors.
- **Zero FPS cost.** Colors are changed by the GPU's display hardware as the image goes to the monitor, not by rendering. No filters, no post-processing.
- **Anti-cheat friendly.** Nothing is injected into games, no game memory is read, and no handles to game processes are opened. It uses the same driver setting as NVIDIA Control Panel.
- **Automatic.** Games are detected when they start, and settings are saved.
- **Small.** One ~200 KB exe, no installer, no admin rights, and no runtime to install (uses .NET Framework 4.8, which comes with Windows 10/11).

Requires an NVIDIA GPU (see [Roadmap](#roadmap) for AMD/Intel).

## How it avoids the usual vibrance-switcher problems

| Problem | What Vibra does |
|---|---|
| Other monitor gets oversaturated too | Only the monitor showing the game is changed; every monitor has its own desktop level. |
| Colors flip when you click on your second monitor | Switching follows what is *visible*, not keyboard focus. The game keeps its boost while it's still on screen. |
| Colors flicker while holding Alt+Tab, opening Start, or when overlays pop up | The Alt+Tab switcher, Start/Search, taskbar, notifications, Xbox Game Bar, the NVIDIA overlay and click-through overlays are ignored. |
| Desktop stays oversaturated for a moment after alt-tabbing out | Switching is event-driven: Windows notifies Vibra the instant focus or minimise state changes, and the switch is a single driver call. There is no polling delay. |
| Vibrance randomly resets (resolution change, sleep, exclusive fullscreen) | Vibra re-applies on display changes, resume and unlock, and checks every 2 s that the driver hasn't reset it. |
| Doesn't work for some games | Games are matched by executable name from the process list, which works even for anti-cheat-protected games. Unreal launcher/game exe pairs (`Game.exe` / `Game-Win64-Shipping.exe`) match each other. |

### What it can't do (and why)

Digital vibrance is a **per-monitor** hardware setting. When you alt-tab to something else *on the same monitor*, that monitor switches back to the desktop level at the same moment. You see the desktop in normal colors, which is the point, but the switch is still a change on that screen.

The only way to recolor *just the game's pixels* is to draw inside the game's frames (ReShade, NVIDIA App filters). That costs FPS and is exactly what anti-cheats like Vanguard block or ban. A capture-and-redraw overlay would add input lag. Vibra sticks to the approach that is free and safe.

Tips:
- **Borderless / windowed-fullscreen** gives the cleanest switching. In exclusive fullscreen, Windows changes display modes on every alt-tab (black flash) regardless of any tool.
- Some drivers ignore digital vibrance while **HDR** is on.
- On laptops, the built-in screen is often driven by the integrated GPU. Vibra can only change screens connected to the NVIDIA GPU.

## Using it

1. Download `Vibra.exe` (see [Getting the exe](#getting-the-exe)) and put it anywhere, e.g. `C:\Tools\Vibra\`.
2. Run it. It appears in the tray and opens its window.
3. Play. Recognised games are added automatically the first time they're on screen, at your **New games** level.

### Main window: your games

Games are shown as a grid of tiles with their icon, name and level. A green dot means that game is on screen right now. Click a tile to edit it in the panel below:

- **Slider**: that game's vibrance. It changes the monitor the game is on, live, and only that monitor. If the game isn't on screen, the new level is used next time it is.
- **Remove** (or right-click a tile, or press Delete): removes the game. Removed games are never auto-added again unless you add them yourself.

**+ Add game** lists games running now, installed games found in your libraries (or *Add all*), other running apps, and *Browse for a game .exe…*.

**Undo:** `Ctrl+Z` undoes and `Ctrl+Y` (or `Ctrl+Shift+Z`) redoes, in the main window and in Settings: adding games (including *Add all*), removing games, level changes (a whole slider drag is one step) and every setting. After adding or removing games, a bar at the bottom offers **Undo** too. Games Vibra adds by itself when they start and in-game shortcut presses aren't part of undo.

**Icons** are looked up in this order: the game's exes and `.ico` files in its install folder, Steam's cached icon for the game, Xbox/Game Pass logo images, and for Riot, Battle.net and similar launchers the game's install folder from Windows' installed-apps list. If none of those has one, Vibra takes it from the running game's exe (located from Windows' process list without touching the game) or its window the first time it's on screen. A colored initials badge is only shown when a game truly has no icon.

### Settings (gear button, or right-click the tray icon)

- **Displays**: your monitors, laid out like in Windows' display settings. Click one to set its **desktop level**, used whenever no game is on it. You see a live preview on that monitor while dragging.
- **New games**: the level for newly added games, and whether games are added automatically when they start.
- **Shortcuts**: click a box and press the keys you want; Backspace turns a shortcut off. If another app already uses a combination, Vibra tells you straight away.

  | Action | Default |
  |---|---|
  | Increase vibrance of the game you're playing (adds it if it's new) | `Ctrl+Alt+PgUp` |
  | Decrease vibrance | `Ctrl+Alt+PgDn` |
  | Pause / resume Vibra | off |
  | Step per press | 5% |

- **General**: Start with Windows.

Closing the main window keeps Vibra running in the tray. Right-click the tray icon for **Settings**, **Pause** (restores desktop colors) or **Exit**.

### Which monitor gets boosted

A monitor shows a game's level while that game is what you're looking at there:

- a fullscreen or borderless game keeps its monitor even while you click around on another monitor;
- a windowed game keeps its monitor as long as nothing covers most of it, including while you use Vibra or another monitor;
- clicking into another app on the same monitor, or alt-tabbing to it, switches that monitor back to its desktop level at the same moment;
- launchers and game clients (Steam, Epic, Battle.net, Riot Client, League's lobby client) are not games and stay at the desktop level. Only the match itself is boosted.

### Your NVIDIA Control Panel setting (e.g. 70%)

On first start, Vibra reads each monitor's current vibrance and uses it as that monitor's **desktop** level, so your 70% stays your desktop color. You don't need to change anything in NVIDIA Control Panel. When you pause or exit Vibra it puts the desktop levels back. If it ever closed unexpectedly while a game was boosted, the next start fixes it.

Vibrance uses the NVIDIA Control Panel scale: 50% is neutral and 100% is maximum.

### Game detection

Vibra finds games in three ways:

1. **Library scan** (on start, and via *Rescan installed games*). Reads Steam (`libraryfolders.vdf` + app manifests), Epic Games (launcher manifests), GOG (registry), Xbox / Game Pass (`X:\XboxGames`), and Windows' installed-apps list for well-known titles from Riot, Battle.net, EA, Ubisoft and others. It only reads files and registry keys; games are never started or touched.
2. **Built-in list** of popular games (Valorant, CS2, Fortnite, Apex, Overwatch, League, Siege, Rocket League, CoD, PUBG, Dota 2, Marvel Rivals, THE FINALS, …).
3. **Unreal Engine games**, recognised by their `*-Win64-Shipping.exe` process name.

Anything else can be added from *Other running apps*, by browsing to its `.exe`, or with the in-game hotkey.

## Getting the exe

Download `Vibra.exe` from the [latest release](https://github.com/ViozoDesigns/Vibra/releases/latest). Put it in a folder you'll keep, e.g. `C:\Tools\Vibra\`, and run it. Windows may show "Windows protected your PC" because the exe isn't signed; click **More info → Run anyway**.

Every green build of the default branch is published as a new release automatically.

### Updates

Vibra updates itself. It checks the latest release in the background, downloads a newer `Vibra.exe`, and swaps it in the next time no game is on screen, so a restart never blinks your colors mid-game. It restarts to the tray and tells you it updated. You can switch this off or check manually under **Settings → General**.

This needs the exe's folder to be writable (not `Program Files`) and the releases to be downloadable without signing in, i.e. a public repository.

If you start a different version of Vibra while another is running (e.g. a fresh download), the new one replaces the running one.

### Building it yourself

Windows, .NET 8 SDK:

```
dotnet build src/Vibra/Vibra.csproj -c Release -o out
```

Run the tests with `dotnet test tests/Vibra.Tests`.

## Files

- Settings: `%APPDATA%\Vibra\settings.json`
- Log (useful if something doesn't switch): `%APPDATA%\Vibra\vibra.log`. It records which game is on which monitor whenever that changes.
- Icon cache: `%APPDATA%\Vibra\icons-v2`

## How it works (for the curious)

- `Core/ScreenDecider.cs` decides, per monitor, which window you're actually looking at. It walks windows top to bottom; a window owns a monitor if it covers at least half of it, if it's the focused window mainly on that monitor, or if it's a game mainly on that monitor that isn't mostly covered.
- `App/VibranceEngine.cs` listens to Windows events (foreground, minimise/restore, move/resize end, window close/hide, cloak) and re-evaluates immediately. It only calls the driver when a monitor's target level actually changes. A 1-second watchdog catches anything the events miss and repairs driver resets.
- `Backends/NvidiaBackend.cs` calls NVAPI's digital vibrance functions (`NvAPI_GetDVCInfo` / `NvAPI_SetDVCLevel`) per display. `nvapi64.dll` is only ever loaded from System32.
- `Platform/WindowScanner.cs` lists windows read-only and ignores shell UI and overlays. `Platform/ProcessNameCache.cs` resolves exe names from a process snapshot, without opening game processes.

## Roadmap

- AMD backend (ADLX display saturation) and Intel backend (IGCL).
- Optional short fade when switching.
- Renaming games.
