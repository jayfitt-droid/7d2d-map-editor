# 7D2D Map Editor

A Windows editor for 7 Days to Die worlds. You can open a world the game generated and change it, or build a new one from scratch. The editor writes a world folder the game loads directly, with no mod needed: `dtm.raw`, `biomes.png`, `splat3.png`, `splat4.png`, `radiation.png`, `prefabs.xml`, `spawnpoints.xml`, `map_info.xml` and `main.ttw`.

Game assets (stamps, prefabs, biome colours, `rwgmixer.xml`) are read from your own install and never shipped with the editor. This is a fan-made tool and is not made by or affiliated with The Fun Pimps.

## Install

Download `7D2DMapEditor-win-Setup.exe` from the [latest release](https://github.com/jayfitt-droid/7d2d-map-editor/releases/latest) and run it. It installs for your Windows user only, so it needs no administrator rights, and adds Start menu and desktop shortcuts. To run without installing, unzip `7D2DMapEditor-win-Portable.zip` instead. Installed copies can update themselves from **Help > Check for Updates**.

It needs Windows 10 or 11 (64-bit) and a copy of 7 Days to Die (v3.x).

## First run

The editor looks for the game through Steam. If it can't find it, use **Settings > Game Folder** and pick the folder that contains `Data`. Biome colours, prefabs and stamps load from there.

## Using it

The window is laid out like Microsoft Paint:

| Area | What it holds |
|---|---|
| **Ribbon** | Tools in groups (Navigate, Terrain, Paint, Build), the **Palette** (biome colours and road surfaces), **Layers** (what the map shows), and **Validate** / **Export**. On a short window it turns into one compact row of icons; **View > Ribbon** keeps it full or compact |
| **Tool options** | Settings for the active tool and a one-line hint. The ∨ at the right of the ribbon hides them |
| **Left rail** | Brush **size** and **strength** sliders |
| **Side panel** | The **Prefabs** browser, the **Placed** list (search the prefabs in your world; double-click one to find and select it), validation **Issues** (double-click one to jump to it) and **World** info |
| **Status bar** | Cursor position (game coordinates, height, biome, prefab), brush size, world size, messages and zoom |

The usual workflow:

1. **New World** (Ctrl+N) or **Open** (Ctrl+O) a project, or a world's `dtm.raw` or `map_info.xml`. **File > Open World Folder** opens a folder directly.
2. Shape the terrain with Raise, Lower, Flatten, Smooth, Roughen and Stamp. Paint biomes, radiation and water. Draw roads. Place POIs, street tiles, whole towns and spawn points.
3. **Save** (Ctrl+S) your work as a `.7dmap` project. A project keeps everything and saves in about a second. The editor also autosaves every 5 minutes and offers to recover if it didn't close normally.
4. **Export** (Ctrl+E) checks the world, then writes a world folder. Export to the game's `GeneratedWorlds` folder and the world shows up under New Game.

The **Select** tool (V) picks up POIs, traders, street tiles and spawn points: click one to select it and see its details, drag it to move it, Q/E to turn it, PgUp/PgDn to raise or lower it, Del to remove it. Dragging empty ground pans the map. The Prefabs and Spawn tools can still select things too.

The info card shows the selected prefab's picture, tier and details. Pick another prefab in the browser while one is selected and the card shows that one as its replacement: press **Replace** to swap it in, or **Back** (Esc) to return. Click the ☆ beside a prefab in the browser, or on the card, to make it a favourite; **Favourites** in the browser's category list shows them.

**New World** can paint one biome over the whole map, or generate a layout of all biomes the way the game's world generator does: pick where they start (forest or wasteland in the middle, a ring, two rings, or a line) and each biome's share of the map. The same seed gives the same layout. As in the game, a biome in the middle keeps off the map's edges, so it can end up a little under its share. You can repaint biomes afterwards as usual.

**Roads** snap as you draw. Click near the end of any road to carry it on: the new road starts there with the same surface and width, and its first segment snaps to the old road's heading. A segment within 5° of north, south, east or west snaps straight. Hold Alt to draw freely. The **Preset** list in the road options sets common sizes: Highway (18 m) and Country road (9 m gravel) match RWG's roads.

**Towns** can be adjusted before you build them. Drag the box's edges or corners to resize it in 150 m tiles, and click a green link road to turn it off (click the dashed line to bring it back).

**Street tiles** snap to the 150 m grid of a street tile nearby, so their streets line up. Away from other tiles they go where you put them. Hold Alt to place one freely beside a tile. Tiles can't overlap: drop one over another tile's middle to replace it (and what's on it), or anywhere else clear of tiles.

**POIs** and traders snap to the edges of street tiles: against a tile's side, flush with it from inside, or lined up with its sides. Drag a POI over another until the ghost covers its middle and that POI turns red: dropping there replaces it.

Placing a POI, trader or street tile never changes the roads you drew. One placed against a road takes the road's height, so they meet level; the ground around it still blends in, easing off beside roads.

Drawn roads level their flat stretches to whole metres, the height prefabs sit at, so a POI beside the road meets it with no lip. On hilly ground a flat building can't meet a sloping road level, and placing one there warns you. To fix it, **flatten first**: with the Flatten tool (Whole metres on), drag a large brush along the road and the ground on both sides where the building will go, then place it. The road and the building end up at the same whole metre, with a gentle ramp at the edge of the flattened area. The Issues panel lists any POI, trader or street tile that doesn't meet its road level.

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
| Alt+click | Place a road point, POI, trader or street tile without snapping |
| Enter, Esc, Backspace | Finish, cancel, or undo a point (roads, towns) |
| Del, PgUp, PgDn | Delete the selected prefab, or raise or lower it one block |
| Wheel, + and -, Home | Zoom, fit to window |
| Right drag | Pan with any tool |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+E | New, open, save, save project as, export |
| F7, F1 | Validate, show shortcuts |

## How it works

A 7D2D world is a folder of files the game reads on first load. The editor writes those files itself and reimplements the generator's behaviour, such as stamp blending, road grading, prefab heights and town layout. It does not copy game code.

## Feedback

Found a bug or have an idea? Open an [issue](https://github.com/jayfitt-droid/7d2d-map-editor/issues).
