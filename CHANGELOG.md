# Changelog

What changed in each release of Gnostic - 7D2D Map Editor, newest first. Downloads are on the [releases page](https://github.com/jayfitt-droid/7d2d-map-editor/releases). Installed copies update from **Help > Check for Updates**.

## v2.0.0 (2026-11-13)

The editor has a new name: **Gnostic - 7D2D Map Editor**. Copies of v1.2.1 and of the betas update to this version from **Help > Check for Updates**. They keep your settings, and the Start menu and desktop shortcuts get the new name.

This version contains all the changes in v2.0.0-beta.1, beta.3 and beta.4 below. The most important changes since v1.2.1 are:

- **Generate panel.** Make a complete world in six stages: terrain, town sites, features, highways, wilderness and spawns. Run a stage again with a new seed, and the other stages stay as they are.
- **Land shapes.** A new world can be one island, several islands or a peninsula. Stage 4 (Highways) can connect the islands with bridges.
- **Presets.** Eight built-in presets change the game's numbers for your world size. You can also save your own presets.
- **Your own prefabs.** The editor loads the prefabs in `%APPDATA%\7DaysToDie\LocalPrefabs`.
- **Export writes every prefab at ground level**, in the format that the game's own editor writes.

### Fixes

- Some bridges from stage 4 (Highways) had a step in the road at one end. The step was up to 3 m high. The editor now builds a bridge only where the road can go up or down to the ground at each end of the bridge. ([#9](https://github.com/jayfitt-droid/7d2d-map-editor/issues/9))
- Setup said "already installed" after you used the portable editor. The portable editor kept its POI pictures in the folder that Setup installs to. The editor now keeps the pictures in `%LOCALAPPDATA%\7D2DMapEditor-Cache` and removes the old folder. ([#11](https://github.com/jayfitt-droid/7d2d-map-editor/issues/11))
- **Help > About** showed version 1.0.0. It now shows the full version, for example 2.0.0. ([#7](https://github.com/jayfitt-droid/7d2d-map-editor/issues/7))

### Changes

- The program file is now `Gnostic.exe`. The downloads are `Gnostic-7D2DMapEditor-win-Setup.exe` and `Gnostic-7D2DMapEditor-win-Portable.zip`.
- In stage 6 (Spawns), the card tells you that **At least** is a minimum. Gravel roads from stage 5 (Wilderness) add spawn points first, with no limit, as in the game. A note shows when stage 5 has not put roads on the map. ([#6](https://github.com/jayfitt-droid/7d2d-map-editor/issues/6))

### Known issues

- Bridges from stage 4 (Highways) leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.0.0-beta.4 (2026-11-13)

### Fixes

- In stage 1 (Terrain), **Plan the biomes again for this land** stays ticked when you run the stage again. Before, each run cleared the tick, and the next run kept the old biome layout. Only you can clear the tick. When you open a world, the tick shows if its biome layout does not fit its land. ([#2](https://github.com/jayfitt-droid/7d2d-map-editor/issues/2))
- The map shows a plain sea floor under water. Before, the map showed the biome colours under the sea, so the sea between islands had blocky patches of colour.

### Changes

- Tests now open projects from each earlier release, so an update cannot stop your saved projects from opening.

### Known issues

- Bridges from stage 4 (Highways) leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.0.0-beta.3 (2026-11-11)

### New

- **Land shapes.** A new world can be one island, several islands or a peninsula. Select the shape in **New World** under **Land shape**, or in stage 1 (Terrain). Set the land share, the coast roughness, the number of islands and the peninsula edge. The coast has its own seed. The land goes down from a beach to a sea floor 12 m below the water. The editor fills the sea with water to 30 m.
- **Biome layouts fit the land.** On an island world, the share of each biome is a share of the land. Biomes also grow across the sea to other islands. The project keeps the biome layout. If you change the land shape, stage 1 can make the biome layout again for the new land.
- **Bridges between islands.** In stage 4 (Highways), tick **Bridge islands**. The editor then connects towns on different islands with the game's bridge sections. Each bridge goes straight north–south or east–west. The water stays open under the bridge, so boats can go under it. **Longest bridge** sets the maximum length (1,500 m by default).
- **Shortcut bridges.** This setting adds a direct bridge between two islands if the route between them crosses three or more bridges.
- **Your own prefabs.** The editor now loads the prefabs in `%APPDATA%\7DaysToDie\LocalPrefabs`. The game's prefab editor saves prefabs in this folder. If a prefab in this folder has the same name as a game prefab or a mod prefab, the editor uses your prefab. The game does the same.
- **Placed search on the map.** When the **Placed** tab shows and the search box has text, the map shows a ring around each prefab that matches. Click a row to zoom to its prefab. To also select the prefab, double-click the row or press Enter. Each row shows the biome at the centre of the prefab.

### Changes

- In stage 1 (Terrain), **Show the last run's tile types** is now off by default.
- Stage 1 (Terrain) puts no plains, hills or mountains on sea tiles.
- Stage 3 (Features) does not start a lake, river, canyon or crater in the sea of a land shape.
- No gravel road or country road connects to a highway within 80 m of a bridge.
- **Validate** does not flag a bridge as under water. For the game's bridge sections, it compares the road with the deck, not with the ground.
- The side panel shows by default. The editor remembers if you close it.

A project with bridges does not open in v2.0.0-beta.1 or earlier.

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

- **Worlds that you saved in the game's own world editor now open at the right heights.** The game's editor stores the height of each POI differently from the world generator. The map editor read these heights the generator's way. So after a save in the game's editor, Review listed hundreds of POIs that "don't meet the road", on a map that nobody changed. A POI that you moved in such a world could also be buried in the game. This version fixes both problems. Navezgane and the Pregen maps use the same format, so the editor now reads them correctly too. The editor saves each prefab back in the format that it read.
- **Fewer road warnings.** Review now flags a POI or street tile only in one case. The road beside it is more than 1 m (one block) higher or lower than its ground. The world generator leaves many one-block steps, and players and vehicles can cross them. The same limit applies when you place a prefab.

Projects that this version saves do not open in v1.2.0 or earlier.

## v1.2.0 (2026-10-24)

### New

- **Generated biome layouts.** New World can arrange all the biomes the way the game's world generator does. You do not have to paint one biome over the whole map. Choose where the biomes start: forest or wasteland in the middle, a ring, two rings, or a line. Set the share of the map for each biome. The same seed gives the same layout.
- **Placed list.** A **Placed** tab in the side panel lists every POI, street tile and part in your world. Search by name and filter by type. To go to a prefab and select it, double-click its row or press Enter.
- **Favourite prefabs.** Click the ☆ beside a prefab in the browser or on its info card. **Favourites** in the category list shows them. Your favourites stay across worlds, projects and updates.
- **Replace from the browser.** Select a prefab on the map, then pick a different prefab in the browser. The info card shows the new prefab as the replacement. Press **Replace** to use it, or press **Back** (Esc) to keep the old prefab.
- **Tier badge.** The info card shows the tier of the prefab.
- **Compact ribbon.** On short windows, the ribbon becomes one row of icons. **View > Ribbon** keeps it full or compact.

## v1.1.2 (2026-10-20)

### Fixes

- **Safer saving.** The editor writes a world folder in full before it replaces any old file. If something fails, the editor puts the old files back. So a failed export never leaves a mix of old and new files.
- When you close the editor, it waits for a save in progress to finish. The editor pauses edits while a save runs.
- Export no longer writes an old copy of the original image for a layer that you edited.
- If the editor cannot repaint a biome map, it now keeps the map as it was. Before, it painted over the map. The map also stays as it was after an edit and an undo.
- The editor now refuses damaged or unusual project files and PNGs, and shows a message. Before, it crashed.

## v1.1.1 (2026-10-20)

### Fixes

- **No lip where a road meets a prefab.** Drawn roads now level their flat parts to whole metres, the height that prefabs sit at. So a POI, trader or street tile beside the road is level with the road. Slopes do not change. Roads that you drew before do not change.
- **Road warnings.** If you place a prefab where the road beside it is higher or lower than its ground, the editor warns you. The Issues panel lists each prefab that is not level with its road.
- The Flatten tool has a **Whole metres** box, which is on by default. The box rounds the height that you pick from the map.

## v1.1.0 (2026-10-19)

### New

- **Road snapping.** Click near the end of any road to continue it with the same surface and width. A segment within 5° of north, south, east or west snaps straight. Hold Alt to draw freely.
- **Road presets:** Highway (18 m), Main street, Town street, Residential, Country road (9 m gravel) and Track.
- **Select tool** (V) replaces Pan. Click a POI, trader, street tile or spawn point to select it. Drag it to move it. Press Q/E to turn it, PgUp/PgDn to raise or lower it, or Del to remove it. Drag empty ground to pan the map.
- **Trader tool** (A). Pick a trader or press **Random**. Then place it like any prefab.
- **Town edits.** Before you build a town, you can drag the edges or corners of its box. This changes the size of the town in 150 m tiles. Click a link road to disable it. Click it again to enable it.
- **Snapping to street tiles.** POIs and traders snap to the edges of nearby street tiles. Drop a POI over the middle of another POI to replace it. Street tiles snap to the grid of a nearby tile, or stay where you put them. Street tiles cannot overlap.
- **Stamp preview.** The cursor of the Stamp tool shows the stamp from above. The preview has the same angle and size as the stamp that you will apply.
- **Check for Updates** in the Help menu. Copies from this version onwards update themselves.
- Message boxes follow the light or dark theme.

### Changes

- Placing a POI, trader, street tile or town never changes the roads that you drew. If you place one against a road, it takes the height of the road.

## v1.0.0 (2026-10-18)

First release.

- **Open and edit** a world that the game generated, or create a new world. The size can be any multiple of 512 m, with a land or water border.
- **Terrain tools:** Raise, Lower, Flatten, Smooth, Roughen, and stamps from the game's own `Data/Stamps`. The editor blends stamps the way the world generator does. Undo and redo.
- **Paint** biomes (in the game's colours), radiation and water.
- **Roads:** draw asphalt or gravel roads of any width. The editor grades the ground under them.
- **Prefabs:** browse the game's POIs with filters and pictures. Then place, turn and move them. The editor flattens and blends the ground under them. You can also place street tiles, whole towns and spawn points.
- **Projects** (`.7dmap`) save everything in about a second. The editor autosaves every 5 minutes and offers to recover your work after a crash.
- **Validate and export.** Validate (F7) finds problems such as overlapping or flooded POIs, missing spawns or traders, and prefabs outside the world. Export (Ctrl+E) writes a world folder that the game loads directly.
- A Paint-style window with a ribbon, palette, brush sliders, status bar, and light and dark themes.
