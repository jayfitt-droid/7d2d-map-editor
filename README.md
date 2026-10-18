# 7D2D Map Editor

A Windows editor for 7 Days to Die worlds. You can open a world the game generated and change it, or build a new one from scratch. The editor writes a world folder the game loads directly, with no mod needed: `dtm.raw`, `biomes.png`, `splat3.png`, `splat4.png`, `radiation.png`, `prefabs.xml`, `spawnpoints.xml`, `map_info.xml` and `main.ttw`.

Game assets (stamps, prefabs, biome colours, `rwgmixer.xml`) are read from your own install and never shipped with the editor. This is a fan-made tool and is not made by or affiliated with The Fun Pimps.

## Install

Download `7D2DMapEditor-win-Setup.exe` from the latest release and run it. It installs for your Windows user only, so it needs no administrator rights, and adds Start menu and desktop shortcuts. To run without installing, unzip `7D2DMapEditor-win-Portable.zip` instead. Installed copies can update themselves from **Help > Check for Updates**.

It needs Windows 10 or 11 (64-bit) and a copy of 7 Days to Die (v3.x).

## First run

The editor looks for the game through Steam. If it can't find it, use **Settings > Game Folder** and pick the folder that contains `Data`. Biome colours, prefabs and stamps load from there.

## Using it

The window is laid out like Microsoft Paint:

| Area | What it holds |
|---|---|
| **Ribbon** | Tools in groups (Navigate, Terrain, Paint, Build), the **Palette** (biome colours and road surfaces), **Layers** (what the map shows), and **Validate** / **Export** |
| **Tool options** | Settings for the active tool and a one-line hint. The ∨ at the right of the ribbon hides them |
| **Left rail** | Brush **size** and **strength** sliders |
| **Side panel** | The prefab browser, validation **Issues** (double-click one to jump to it) and **World** info |
| **Status bar** | Cursor position (game coordinates, height, biome, prefab), brush size, world size, messages and zoom |

The usual workflow:

1. **New World** (Ctrl+N) or **Open** (Ctrl+O) a project, or a world's `dtm.raw` or `map_info.xml`. **File > Open World Folder** opens a folder directly.
2. Shape the terrain with Raise, Lower, Flatten, Smooth, Roughen and Stamp. Paint biomes, radiation and water. Draw roads. Place POIs, street tiles, whole towns and spawn points.
3. **Save** (Ctrl+S) your work as a `.7dmap` project. A project keeps everything and saves in about a second. The editor also autosaves every 5 minutes and offers to recover if it didn't close normally.
4. **Export** (Ctrl+E) checks the world, then writes a world folder. Export to the game's `GeneratedWorlds` folder and the world shows up under New Game.

The **Select** tool (V) picks up POIs, traders, street tiles and spawn points: click one to select it and see its details, drag it to move it, Q/E to turn it, PgUp/PgDn to raise or lower it, Del to remove it. Dragging empty ground pans the map. The Prefabs and Spawn tools can still select things too.

**Roads** snap as you draw. Click near the end of any road to carry it on: the new road starts there with the same surface and width, and its first segment snaps to the old road's heading. A segment within 5° of north, south, east or west snaps straight. Hold Alt to draw freely. The **Preset** list in the road options sets common sizes: Highway (18 m) and Country road (9 m gravel) match RWG's roads.

**Towns** can be adjusted before you build them. Drag the box's edges or corners to resize it in 150 m tiles, and click a green link road to turn it off (click the dashed line to bring it back).

**POIs** and traders snap to the edges of street tiles: against a tile's side, flush with it from inside, or lined up with its sides. Drag a POI over another until the ghost covers its middle and that POI turns red: dropping there replaces it.

A road you drew first ramps up or down to meet a POI, trader or town built against it, so there is no step where they join.

The **Trader** tool (A) places a trader: pick one in the options or press **Random**.

A world opened from a folder saves straight back to that folder with Ctrl+S, as before. Saved games keep the chunks they've already generated, so start a **new game** to see your changes everywhere.

### Keyboard shortcuts

| Key | Action |
|---|---|
| V, R, L, F, S, N, T | Select, Raise, Lower, Flatten, Smooth, Roughen, Stamp |
| B, D, W | Paint biome, Paint radiation, Water fill |
| O, I, A, G, H | Road, Prefabs, Trader, Town, Spawn points |
| [ and ] | Smaller or larger brush |
| Q and E | Turn (square brush, stamp, prefab, spawn) |
| Ctrl+click | Sample height (Flatten) or water level (Water) |
| Shift+drag | Erase roads or radiation |
| Alt+click | Place a road point, POI or trader without snapping |
| Enter, Esc, Backspace | Finish, cancel, or undo a point (roads, towns) |
| Del, PgUp, PgDn | Delete the selected prefab, or raise or lower it one block |
| Wheel, + and -, Home | Zoom, fit to window |
| Right drag | Pan with any tool |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+E | New, open, save, save project as, export |
| F7, F1 | Validate, show shortcuts |

## Build from source

```
dotnet build MapEditor.slnx -c Release
dotnet test MapEditor.slnx -c Release
```

This needs the .NET 10 SDK (pinned in `global.json`). Set `MAPEDITOR_TEST_WORLD` to a world folder to also round-trip a real world in the tests.

| Project | Purpose |
|---|---|
| `src/MapEditor.Core` | World model, file I/O (its own PNG codec, no alpha premultiply), `.7dmap` projects, validation, terrain/paint/road/prefab/town operations, game-install discovery |
| `src/MapEditor.App` | WPF editor (Fluent theme, follows the Windows light/dark setting) |
| `src/MapEditor.Cli` | `mapeditor-cli` with `info`, `roundtrip`, `export`, `validate`, `compare`, `render`, `new` and `installs`, for testing and scripting |
| `tests/MapEditor.Core.Tests` | xUnit tests |

## Release

```
./build/release.ps1 -Version 1.0.0
```

The script runs the tests, publishes a self-contained win-x64 build, and packs it with [Velopack](https://velopack.io) into `out/releases`. Pushing a `v*` tag does the same on GitHub Actions and attaches the files to a GitHub release. To turn on update checks for installed copies, set `Updates.BuiltInFeed` (in `src/MapEditor.App/Updates.cs`) to the releases URL.

## How it works

[docs/RWG-Research.md](docs/RWG-Research.md) explains how the game's world generator and world format work. The editor reimplements that behaviour, such as stamp blending, road grading, prefab heights and town layout. It does not copy game code.
