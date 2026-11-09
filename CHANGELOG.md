# Changelog

What changed in each release of 7D2D Map Editor, newest first. Downloads are on the [releases page](https://github.com/jayfitt-droid/7d2d-map-editor/releases). Installed copies update from **Help > Check for Updates**.

## v2.0.0-beta.1 (2026-11-09)

This is a beta for testers. It is a pre-release, so copies of v1.2.1 do not get it as an update. A beta copy gets each later beta, and then v2.0.0.

### New

- **Generate panel.** The editor can make a complete world the way the game's world generator does. The **Generate** tab has six stages. Do them in sequence. You can run a stage again with a new seed, and the other stages stay as they are. Each stage runs in the background, with progress and Cancel. Each stage is one undo step.
  1. **Terrain.** Each 256 m tile gets plains, hills or mountains. The editor then puts the game's terrain stamps on the tiles. Set the weights for each biome, the land or ocean edges, and the tiles that you paint by hand. You can generate the whole map or an area that you drag.
  2. **Town sites.** The editor plans towns and cities from the game's town types, with the game's counts for the world size. Before you build, you can move a town, re-roll it, remove it or add one. **Build** makes all the towns: streets, districts, POIs, traders and flat ground. Traders follow the game's limits for each biome. A setting gives one trader to each town. Another setting lets towns cross biome borders.
  3. **Features.** The editor puts lakes, rivers, canyons and craters into the ground. Options include long rivers that flow downhill and lakes on mountains.
  4. **Highways.** The editor connects the towns with asphalt highways. By default, one highway connects two towns. A setting allows a second highway.
  5. **Wilderness.** The editor puts POIs outside the towns and connects them to the roads with gravel roads.
  6. **Spawns.** The editor puts player spawn points near traders and roads, as the game does. You can set the minimum and maximum number and the biomes.
- **Presets.** Eight built-in presets change the game's numbers for your world size: Big cities, Frontier, Border towns, Easy start, Rugged, Flatlands, Waterways and Wasteland heavy. You can also save your own presets. The project keeps the Generate settings.
- **The Town tool uses the world's 150 m street-tile grid.** A town that you draw now aligns with the towns from Town sites. Street tiles between towns then fit. Hold **Alt** when you start the drag to start the grid at the mouse position.

### Changes

- **Export writes every prefab at ground level.** The file uses the format that the game's own editor writes. A POI stays on its ground if a game update or a mod changes the POI's height offset. Prefabs that your game folder does not have keep the format that the editor read. The export dialog tells you how many.
- **Autosave does not stop the editor.** Autosave no longer copies the whole world first. On a 16k world, this prevents a pause and saves about 1.5 GB of memory.
- **The minimap does not go blank when you edit.** The old image stays until the new image is ready.
- **A failed edit is undone.** If an edit fails, the editor puts the world back as it was and tells you. A failed road or town keeps its points or plan, so you can try again.
- **The editor skips large extra files in a world folder.** It skips a file over 256 MB, or files over 1 GB in total, and shows a warning. The world still saves as a project that opens again.

### Known issues

- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v1.2.1 (2026-10-31)

### Fixes

- **Worlds saved in the game's own world editor now open at the right heights.** The game's editor stores each POI's height in a different way from the world generator. The map editor read them the generator's way, so after a save in the game's editor, Review listed hundreds of POIs that "don't meet the road" on a map nobody had changed. A POI moved in such a world could also end up buried in game. Both are fixed. Navezgane and the Pregen maps, which use the same format, are read correctly too, and each prefab is saved back in the format it came in.
- **Fewer road warnings.** Review now flags a POI or street tile only when the road beside it is more than 1 m (one block) off its ground. The world generator leaves many one-block steps, and players and vehicles get over them. The same limit applies when you place a prefab.

Projects saved by this version can't be opened by v1.2.0 or earlier.

## v1.2.0 (2026-10-24)

### New

- **Generated biome layouts.** New World can lay out all the biomes the way the game's world generator does, instead of painting one biome over the whole map. Choose where they start (forest or wasteland in the middle, a ring, two rings, or a line) and set each biome's share of the map. The same seed gives the same layout.
- **Placed list.** A **Placed** tab in the side panel lists every POI, street tile and part in your world. Search by name, filter by type, and double-click a row (or press Enter) to jump to that prefab and select it.
- **Favourite prefabs.** Click the ☆ beside a prefab in the browser, or on its info card. **Favourites** in the category list shows them, and they stay across worlds, projects and updates.
- **Replace from the browser.** With a prefab selected on the map, picking a different one in the browser shows it on the info card as the replacement. Press **Replace** to swap it in, or **Back** (Esc) to return.
- **Tier badge** on the info card.
- **Compact ribbon.** On short windows the ribbon becomes one row of icons. **View > Ribbon** keeps it full or compact.

## v1.1.2 (2026-10-20)

### Fixes

- **Safer saving.** A world folder is written in full before any old file is replaced, and the old files are put back if something fails, so a failed export never leaves a mix of old and new files.
- Closing the editor waits for a save in progress, and edits are paused while a save runs.
- An edited layer no longer exports a stale copy of the original image.
- Biome maps the editor can't repaint are left as they were instead of being painted over, and survive edit-and-undo.
- Damaged or unusual project files and PNGs are refused with a message instead of crashing the editor.

## v1.1.1 (2026-10-20)

### Fixes

- **No lip where a road meets a prefab.** Drawn roads now level their flat stretches to whole metres, the height prefabs sit at, so a POI, trader or street tile beside the road meets it level. Slopes and roads already drawn are unchanged.
- **Road warnings.** Placing a prefab where the road beside it is off its ground warns you, and the Issues panel lists any that don't meet their road level.
- The Flatten tool has a **Whole metres** box (on by default) that rounds the height picked from the map.

## v1.1.0 (2026-10-19)

### New

- **Road snapping.** Click near the end of any road to carry it on with the same surface and width. A segment within 5° of north, south, east or west snaps straight. Hold Alt to draw freely.
- **Road presets:** Highway (18 m), Main street, Town street, Residential, Country road (9 m gravel) and Track.
- **Select tool** (V) replaces Pan. Click a POI, trader, street tile or spawn point to select it, drag to move it, Q/E to turn it, PgUp/PgDn to raise or lower it, Del to remove it. Dragging empty ground pans the map.
- **Trader tool** (A): pick a trader or press **Random**, then place it like any prefab.
- **Town edits.** Before building a town, drag the box's edges or corners to resize it in 150 m tiles, and click a link road to turn it off or back on.
- **Snapping to street tiles.** POIs and traders snap to the edges of nearby street tiles. Drop one over another POI's middle to replace it. Street tiles snap to the grid of a tile nearby, or go where you put them; they can't overlap.
- **Stamp preview.** The Stamp tool's cursor shows the stamp from above, turned and sized as it will be stamped.
- **Check for Updates** in the Help menu. Copies from this version on update themselves.
- Message boxes follow the light or dark theme.

### Changes

- Placing a POI, trader, street tile or town never changes the roads you drew. One placed against a road takes the road's height instead.

## v1.0.0 (2026-10-18)

First release.

- **Open and edit** a world the game generated, or create a new one: any size that's a multiple of 512 m, with a land or water border.
- **Terrain tools:** Raise, Lower, Flatten, Smooth, Roughen, and stamps from the game's own `Data/Stamps`, blended the way the world generator does. Undo and redo.
- **Paint** biomes in the game's colours, radiation, and water.
- **Roads:** draw asphalt or gravel roads of any width; the ground under them is graded.
- **Prefabs:** browse the game's POIs with filters and pictures, then place, turn and move them. The ground under them is flattened and blended. Street tiles, whole towns and spawn points too.
- **Projects** (`.7dmap`) save everything in about a second. Autosave every 5 minutes, with recovery after a crash.
- **Validate and export.** Validate (F7) checks for problems such as overlapping or flooded POIs, missing spawns or traders, and prefabs outside the world. Export (Ctrl+E) writes a world folder the game loads directly.
- A Paint-style window: ribbon, palette, brush sliders, status bar, and light and dark themes.
