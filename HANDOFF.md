# Handoff: 7 Days to Die Map Editor

Last updated 2026-10-16. The last commit is `779e537` (tag `v0.1.1` is at `bdacad4`) on `main`. **All of milestone 7 is uncommitted** (see the file list below). There's no git remote.

## Goal

This is a Windows desktop app (C# + WPF on .NET 10, CommunityToolkit.Mvvm, xUnit) for building 7 Days to Die (v3.3) worlds. It can edit an existing world or author one from scratch, and it writes a world folder the game loads directly. It does not use the in-game generator (RWG) or the biome-brush mod.

Rules agreed with the user:
- **Public release:** never bundle game assets. Read stamps, prefabs, `biomes.xml` and `rwgmixer.xml` from the user's install at runtime. Reimplement behaviour from the decompiled code; never copy it in.
- **The plan:** `PLAN.md` in the repo (a copy of `C:\Users\Jason\.claude\plans\glittery-munching-pudding.md`) has 8 milestones. M1–M6 are done. **M7 (POIs and spawns) is in progress.** M8 covers the `.7dmap` format, validation, the export dialog, the installer and the release.

**How the user works:** they try each change in the app, then in a new game, and report back. **Commit only when they ask.** Never add `Co-Authored-By` or other attribution lines to commit messages. Use the task-list and wrap-up tools, and keep replies short.

## Current State

M7 so far, all built, with 65 tests passing:
- **Done and checked in game by the user:**
  - The Prefabs tool (I): a browser, placing, moving, rotating, deleting, lot snapping, street tiles and parts.
  - The Spawn points tool (H).
  - The user's test, on world "poi edit test", worked: houses swapped around the spawn, plus a new street tile with a bowling alley. The game log has no errors.
- **Done, but the user hasn't confirmed them in game:**
  - The info card for a selected prefab: preview, Q/E turns, PgUp/PgDn height nudge, "Replace with picked", and Delete.
  - Haloed outlines and tints, plus a "Dim the map" layer option.
  - Traders drawn in magenta.
  - POIs carry their own parts when moved or deleted.
- **In progress: the town generator.** Only the data layer exists so far: `RwgMixer.cs` and the new prefab fields. There is no generator, tool or UI yet.
- There's a stray `HANDOFF - Copy.md` that the user made; leave it alone.

## Active/Touched Files (this session)

All paths are relative to `C:\Users\Jason\Projects\7d2d\MapEditor`.

**Core:**
- `src/MapEditor.Core/GameData/PrefabLibrary.cs`:
  - `PrefabMarker` (with `TurnedOnce`, which turns counter-clockwise: (x, z) → (size.z - z, x)).
  - `PrefabInfo`, with `Markers`, `Zoning`, `TraderArea`/`IsTrader`, `ThemeTags`, `ThemeRepeatDistance`, `DuplicateRepeatDistance`, `DensityScore` (from `Stats.TotalVertices`), `IsStreetTile`, `IsPart`, `IsInternal`, `District`, `RotatedMarkers(rot)` and `FrontDirection(rot)` (0 N, 1 W, 2 S, 3 E).
  - `PrefabLibrary.Of(...)` (for tests).
- `src/MapEditor.Core/GameData/RwgMixer.cs` (**new, not used yet**):
  - `TownshipType`: biomes, spawn_trader, spawn_gateway, outskirt district/percent, and the `Tiles` range for the world-size section.
  - `DistrictType`: tag, prefab_name + "_", weight, required townships, avoided neighbours, custom sizes, preview colour, `Kind` and `SpawnOrder`.
  - `TileCounts`, `TileMaxDensity`, `SpawnAdjusts` and `BuildableTownships`.
- `src/MapEditor.Core/Ops/PrefabPlacer.cs` (new):
  - `PrefabSpot`, `LotSnap`.
  - `PrefabY` and `GroundUnder`.
  - `MedianGround`, `Flatten` (smoothstep blend), `SpotAt` (street tiles snap to the 150 m grid and take a neighbour's height; POIs snap into lots), `FindLot` (smallest lot that fits), `Check` (warnings).
  - `Place`, `Contents` (a tile's POIs and parts, or a POI's parts), `Move` (carries and turns contents), `Delete`, `Replace` and `MarkerParts`.
- `src/MapEditor.Core/Ops/SpawnHelper.cs`: `StandingHeight` (ceil(ground) + 1), `Near`, `Add`, `Move`, `Rotate`, `Delete`.
- `src/MapEditor.Core/Edit/UndoStack.cs`: `EditSession.TouchList` and `ListSnapshot<T>`, which snapshot the prefab and spawn lists for undo.
- `src/MapEditor.Core/Model/World.cs`: `PrefabInstance.Clone()`, `SpawnPoint.Clone()`.

**App:**
- `src/MapEditor.App/Editing/PrefabToolController.cs` (new): the Prefabs and Spawn tool input, ghost, selection, `ReplaceSelected`, `NudgeHeight`, `Rotate`, `RotateSpawn`, `DeleteSelected`, `Cancel`, `Deselect` and `Refresh` (called after undo/redo, a tool change or a new world).
- `src/MapEditor.App/Editing/TerrainToolController.cs`: ignores the Prefab and Spawn tools.
- `src/MapEditor.App/Editing/EditorTool.cs`: adds `Prefab` and `Spawn`.
- `src/MapEditor.App/MainViewModel.cs`:
  - Browser filters (category, tier, lot size, search) and `PrefabResults`.
  - `SelectedPrefabInfo` (the "armed" prefab), `PrefabRotation`, `PrefabFlatten`, `PrefabAddParts`.
  - `SelectionText` (card or spawn panel) and `PlacementText` (ghost).
  - `SelectedMapPrefab`, `ShowPrefabCard`, `CanReplace`/`ReplaceLabel`, `DimMap`.
- `src/MapEditor.App/MainWindow.xaml(.cs)`: the prefab and spawn panels, the info card over the map, Dim checkbox, card button handlers, keys (Q/E, Delete, Esc, PgUp/PgDn), and wiring to `_prefabTools`.
- `src/MapEditor.App/Map/MapView.cs`:
  - `PrefabGhost`, `SelectedPrefab`, `SelectedSpawn`, `ShowLots`, `DimMap`.
  - Prefab drawing in this order: tiles, lots, POIs, parts, then traders.
  - `FootprintMapRect` and `DrawFrontArrow`.
  - Spawn facing tick.
- `src/MapEditor.App/Map/OverlayRaster.cs`: `Fill` and `Outline(rect, colour, halo)`.
- `src/MapEditor.App/Map/ToolInput.cs`: the `PrefabGhost` record.
- `src/MapEditor.App/PreviewImageConverter.cs` (new, keyed `PreviewImage` in `App.xaml`).

**Tests:** `tests/MapEditor.Core.Tests/PrefabTests.cs` (new, 17 tests).

**Docs:** `docs/RWG-Research.md`, line 22: prefab y and rotation facts.

## Changes Made (key facts, verified)

- **Prefab y in dtm.raw terms is ground + 1 + YOffset.** RWG writes `dtm.raw` 1 m below its internal heights (`HeightMapUtils.SaveHeightMapRAW(stream, data, -1f)`). Checked exactly on 240 wilderness POIs in "Mezawoku Mountains" (v3.3).
- **Wilderness placement:** RWG sets the footprint to the median of the rounded-up heights, then blends back over 18 m with a smoothstep (`StreetTile.SmoothTerrainRect`). Street tiles blend over 50 m (2 streets or fewer) or 110 m.
- **What the game does on load:** `DynamicPrefabDecorator.copyPrefabsIntoHeightMap` copies the prefab's own terrain into the heightmap. With `RandomGeneratedWorld=true` (which new worlds set), it overwrites; otherwise it only raises.
- **Rotation:** each step is a quarter turn counter-clockwise seen from above. The front faces north when rotation == RotationToFaceNorth.
- **Lot snapping** reproduces `StreetTile.SpawnMarkerPartsAndPrefabs`:
  - Rotation = tile rotation + RotationToFaceNorth + marker rotations.
  - The POI is pushed toward the lot's street side, using the switch on marker.Rotations.
  - y = tile.y + marker.y + YOffset.
- **Parts:** one per marker group, weighted by chance. Marker rotations 1 and 3 are swapped, there's no YOffset, and tags are filtered by district ("wilderness" outside towns).
- **Check against RWG:** lot snapping and parts match 100% on Mezawoku: 5,526 lot POIs and 5,563 parts.
- **Scratch scripts:** `lotcheck.cs` and `wildcheck.cs` are .NET file-based scripts in the session scratchpad (`C:\Users\Jason\AppData\Local\Temp\claude\C--Users-Jason-Projects-7d2d-MapEditor\8f2dd0dc-...\scratchpad\`). They may be gone. Rewrite them with `#:project src\MapEditor.Core\MapEditor.Core.csproj` if needed.

## Failed Attempts & Gotchas

- **Old worlds:** worlds made in Alpha 21 ("Vafuru Valley", "Zezepexe County") have different YOffsets. Validate only against v3.x worlds such as **Mezawoku Mountains** (V.3.30.18). Govosare Valley is v3.2, and its parts differ.
- **Overlapping lots:** street tiles offer overlapping alternative lots of different sizes. Picking the first match gave the wrong lot, so `FindLot` picks the smallest lot that fits.
- **Build locks:** a running `MapEditor.exe` locks `bin/`. **Never kill the user's editor** (it may hold unsaved work; once it was open on Mezawoku with changes). Build with `-p:OutDir=<scratchpad>\appbuild\` instead. Check first with `tasklist //V //FI "IMAGENAME eq MapEditor.exe"`.
- **Shell:** inline `python - <<'EOF'` heredocs sometimes fail with "unexpected EOF". Write the script to the scratchpad and run it with `python -I <file>`. Use `D:\...` paths for Windows Python.
- **Computer use:**
  - Call `request_access` with "MapEditor.exe" only after the app is running.
  - The grant can drop; re-request it, then `open_application("Mapeditor")`.
  - The window showed fully on M28U this time.
  - Use `left_mouse_down`, `mouse_move` steps and `left_mouse_up` for drags.
  - Don't drive the screen while the user is using the editor or playing.
- **Saved games:** they keep generated chunks, so in-game tests need a **new game**.

## Next Steps

1. **Ask the user** whether to commit M7 so far; they test before committing. Also mention the unconfirmed items under Current State.
2. **Build the town generator** (`src/MapEditor.Core/Ops/TownGenerator.cs`, plus a "Town" tool, shortcut G). The user chose:
   - Pick the town type from the game's townships, defaulting by biome and by size against `TownshipType.Tiles`.
   - Preview, then reroll, then accept as one undo step.
   - Connect the town's exits to the nearest road.
   - Replace existing prefabs in the selection.
   - Flatten to one level.
   - They did not ask to force a trader, so keep RWG's own behaviour.
3. **The algorithm to port** (decompiled code in `D:\dotPeek Test\Assembly-CSharp\WorldGenerationEngineFinal\`):
   - **Cells:** the 150 m grid cells fully inside the selection, grid origin at the south-west corner, cell (cx, cz) at grid metres (cx·150, cz·150). Leave out cells under water, and cells with existing prefabs unless replacing.
   - **Core and ring:** core = cells whose 8 neighbours are all selected; the ring is kept for outskirts and gateways. If the core is empty, core = all cells.
   - **Outskirts** (`TownPlanner.Grow`): 4-way neighbours of the core (8-way if percent ≥ 1), thinned by the loop over (1 - percent). They get the township's outskirt district and go into Streets first.
   - **Districts** (`DistrictPlanner.GenerateDistricts`):
     - Use districts whose required_township includes the town and whose weight is above 0. Normalise the weights, and make avoided neighbours symmetric.
     - Order by SpawnOrder, highest first.
     - Sort tiles by distance to the centre tile's centre + (5, 0). Each district takes `round(count × weight)` tiles (banker's rounding); the last district takes the rest.
     - Use `FindFreeWithNeighbor` and `HasAvoidNeighbor`. Drop tiles left without a district.
   - **Gateways** (`GenerateGatewayDir`, ×2 for citybig): from ring cells. The candidate needs exactly one town neighbour and at least 2 valid neighbours. Roadside townships are gateways only.
   - **Exits** (`TownPlanner.Plan`, lines 198–225):
     - Start with exits to town neighbours.
     - Remove exits between different districts while `CountDistrictExitsBetween` > (downtown ? 2 : 1).
     - Run `ChangeLConnectionToCap` once per district kind.
     - Bits: 0 N, 1 E, 2 S, 3 W.
   - **Shapes** (`StreetTileShared`): exits {5, 7, 15, 4, 6} map to straight, t, intersection, cap, corner. Each rotation shifts the bits clockwise.
     - The tile name is `rwg_tile_` + district.PrefabName + shape. Pick at random among library prefabs with that prefix, respecting `TileCounts` max.
     - Prefab rotation = (base rotations + RTFN) & 3, then swap 1 and 3.
   - **Height** (`Township.CalcCenterStreetTile`): dtm ground = ceil(average of the 9 samples at the centre tile's centre ±50 m) + 3. Skip RWG's +25 snow and +12 wasteland bonuses. Tile y = `PrefabY(tile, ground)`.
   - **Filling lots** (`StreetTile.SpawnMarkerPartsAndPrefabs`, recursive to depth 5):
     - Sort POI lots by size, largest first. Take groups in order, shuffle each, and place **one POI per group**.
     - Lot size limits: custom sizes go from the previous `MarkerSizes` entry + 1 up to the lot size (MarkerSizes: 1, 25, 42, 60, 100). Otherwise the POI must match the lot exactly.
     - The density budget starts at `TileMaxDensity` (default 62) and drops for dense prefabs within 190 m.
   - **Choosing a POI** (`PrefabManager.GetPrefabWithDistrict`, shuffled by seed):
     - It must have all the district tags, any of the marker tags, or no "has" tag when the marker has none. Its density must fit the budget, and `isSizeValid` must pass.
     - Theme repeat checks apply (trader distances are divided by WorldSizeDistDiv: 1 for worlds over 4500), and so do name repeats (× scale).
     - Score with `getScoreForPrefab`: SpawnAdjusts, tier / 5, and division by world use count + 1.
     - If nothing fits, retry with the budget + 8 at scale 0.3, then 18 at scale 0. Add parts with `MarkerParts`.
   - **Apply:** remove the old prefabs, flatten each tile to the ground (blend 50 or 110 m), add the new prefabs, then draw the road links with `RoadOp.Draw`. Exit points come from `HighwayExitFromDir`. Search `world.Roads` for the nearest road within about 2 km. Use asphalt for town, city and citybig, gravel otherwise.
   - **UI:**
     - Drag a selection rectangle.
     - Panel: township combo, seed with a Reroll button, checkboxes, and Accept (Enter) or Cancel (Esc).
     - Preview: tiles tinted by district `PreviewColour`, POI outlines and road lines.
   - **Tests:** add unit tests, then compare district and tile choices with Mezawoku.
4. Then finish M7 (validation is in M8). Update `HANDOFF.md` and `docs/RWG-Research.md`.
