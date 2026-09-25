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
3. Play. Recognised games are added automatically the first time they're on screen, at the **New games** level.

The window has:

- **Games**: one slider per game. A green dot means that game is on screen right now. Dragging a slider shows the value live: on the game if it's visible, otherwise on the monitor the Vibra window is on. `✕` removes a game; removed games are never auto-added again unless you add them yourself.
- **+ Add game**: games running now, installed games found in your libraries, other running apps, or *Browse for a game .exe…*.
- **Defaults**: the **desktop** level for each monitor (used whenever no game is on it) and the **New games** level.
- **Start with Windows** and **Auto-add games when they start**.

In-game hotkeys:

| Keys | Action |
|---|---|
| `Ctrl+Alt+PgUp` | +5% for the game you're playing (adds it if it's new) |
| `Ctrl+Alt+PgDn` | −5% |

Closing the window keeps Vibra running in the tray. Right-click the tray icon to **Pause** (restores desktop colors) or **Exit**.

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

Every push is built on GitHub Actions (Windows). Open the **Actions** tab → latest **build** run → download the **Vibra** artifact (contains `Vibra.exe` and `Vibra.exe.config`).

Or build it yourself (Windows, .NET 8 SDK):

```
dotnet build src/Vibra/Vibra.csproj -c Release -o out
```

Run the tests with `dotnet test tests/Vibra.Tests`.

## Files

- Settings: `%APPDATA%\Vibra\settings.json`
- Log (useful if something doesn't switch): `%APPDATA%\Vibra\vibra.log`

## How it works (for the curious)

- `Core/ScreenDecider.cs` decides, per monitor, which window you're actually looking at. It walks windows top to bottom; a window owns a monitor if it covers at least half of it, or if it's the focused window mainly on that monitor.
- `App/VibranceEngine.cs` listens to Windows events (foreground, minimise/restore, move/resize end, window close/hide, cloak) and re-evaluates immediately. It only calls the driver when a monitor's target level actually changes. A 1-second watchdog catches anything the events miss and repairs driver resets.
- `Backends/NvidiaBackend.cs` calls NVAPI's digital vibrance functions (`NvAPI_GetDVCInfo` / `NvAPI_SetDVCLevel`) per display. `nvapi64.dll` is only ever loaded from System32.
- `Platform/WindowScanner.cs` lists windows read-only and ignores shell UI and overlays. `Platform/ProcessNameCache.cs` resolves exe names from a process snapshot, without opening game processes.

## Roadmap

- AMD backend (ADLX display saturation) and Intel backend (IGCL).
- Optional short fade when switching.
- Custom hotkeys.
