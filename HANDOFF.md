# Handoff: 7 Days to Die Map Editor

Last updated 2026-10-16, at tag `v0.1.1` (commit `bdacad4`) on `main`. There's no git remote, so nothing has been pushed.

## Goal

This is a Windows desktop app for building 7 Days to Die (v3.3) worlds. It can edit an existing world or author one from scratch, and it writes a world folder the game loads directly. It does not use the in-game generator (RWG) or the biome-brush mod.

Decisions made with the user:
- **Public release.** Never bundle game assets. Read stamps, prefabs and `biomes.xml` from the user's game install at runtime. Reimplement game behaviour from `docs/RWG-Research.md` and the decompiled code; never copy decompiled code in.
- **C# + WPF on .NET 10** (SDK pinned in `global.json`), CommunityToolkit.Mvvm and xUnit.
- **Independent of biome-brush.**
- **v1 does both:** editing existing worlds and authoring new ones.

The approved plan is `C:\Users\Jason\.claude\plans\glittery-munching-pudding.md`. It has 8 milestones:

1. Scaffold. Done.
2. World file I/O and round-trip. Done.
3. Viewer. Done; tagged v0.1.0.
4. New World and terrain tools. Done.
5. Paint layers. Done.
6. Roads. Done; tagged v0.1.1.
7. **POIs and spawns.** This is next.
8. `.7dmap` project format, validation, export dialog, Velopack installer, release.

## Current State

- Milestones 1–6 are done and committed. All 48 tests pass (`dotnet test`).
- The user has checked every milestone in game, most recently in a new game on the world **JasonTest**. The roads and the lake causeway look right.
- Brush shapes (circle or square, rotation, smooth/linear/hard edge) were added between M5 and M6 at the user's request.

**How the user works:** they try each change in the app, then in game, and report back. **Commit only when they ask**, and they usually ask after an in-game check. Never add `Co-Authored-By` or other attribution lines to commit messages. Use the task list and wrap-up tools, and keep replies short.

## Active/Touched Files

All paths are relative to `C:\Users\Jason\Projects\7d2d\MapEditor`.

**Core** (`src/MapEditor.Core`, no UI):
- `Model/World.cs` and `Model/Grid.cs`: rasters keep row 0 at the **south** edge, with index `x + z*Width`. Heights are uint16 metres × 257. `Grid.Version` stays 0 while a layer is "as loaded", which is how unchanged layers are written back byte-for-byte.
- `IO/WorldReader.cs`, `IO/WorldWriter.cs`, `IO/LayerImages.cs`, `IO/Png.cs` (the project's own PNG codec), `IO/LevelFile.cs` (`main.ttw` template plus a new GUID), `IO/WorldXml.cs`.
- `GameData/GameInstall.cs`: finds the Steam install. It also has `UserGeneratedWorldsDir`, `UserSavesDir`, and `SavedGamesOn(world)`, which is new in M6.
- `GameData/BiomeTable.cs`, `PrefabLibrary.cs`, `StampLibrary.cs`.
- `Edit/UndoStack.cs`: `EditSession.Touch(grid, rect, metresPerCell)` snapshots 64×64 tiles before each change, and `UndoStack` has a 1 GB budget.
- `Ops/TerrainBrush.cs`: `BrushShape`, `BrushFalloff`, `BrushTip` (shared by all radius tools) and `BrushSettings`.
- `Ops/StampOp.cs`, `Ops/NewWorldGenerator.cs`, `Ops/SpawnHelper.cs`, `Ops/Noise.cs`.
- `Ops/LayerPaint.cs` (M5): biome painting at W/8 grows the palette when needed, and radiation painting at W/32 writes R=255.
- `Ops/WaterFill.cs` (M5): `Fill` floods connected ground below the surface and adds a one-cell shoreline. `Drain` removes only water at the clicked surface level.
- `Ops/RoadOp.cs` (M6):
  - `RoadPath(Points, Type, Radius, Grade)`.
  - `Centreline` resamples every 4 m and rounds corners (about 30 m for asphalt, 15 m for gravel).
  - `HeightProfile` smooths the height along the road and carries the road across water as a causeway from bank to bank, at least 1 m above the surface.
  - `Draw` rasterises the road and grades the terrain with a t² blend over 10 m (asphalt) or 6 m (gravel). Asphalt from 8 m wide gets a 1 m gravel shoulder, and gravel never overwrites asphalt.
  - `Erase` removes road pixels only.
- `Rendering/MapRenderer.cs`: mip tiles. Biome display colours rebuild when the palette grows.

**App** (`src/MapEditor.App`, WPF):
- `MainWindow.xaml(.cs)`: menus, the tool panel, saving (`SaveAsync`, `WarnAboutExistingGames`), the unsaved-changes prompts, and keyboard shortcuts in `OnPreviewKeyDown`.
- `MainViewModel.cs`: all tool settings.
- `Editing/EditorTool.cs` and `Editing/TerrainToolController.cs`: turn map input into edits, with one undo step per stroke. They also handle road point editing (`FinishRoad`, `CancelRoad`, `UndoRoadPoint`).
- `Map/MapView.cs`: pan and zoom, tile workers, the brush cursor, the road preview, prefab outlines (drawn by `OverlayRaster`) and spawns.
- `Map/ToolInput.cs`: `ToolInputEventArgs` (with `ClickCount`), `BrushCursor` and `RoadPreview`.
- `NewWorldDialog`, `SaveWorldDialog`, `RgbToBrushConverter.cs`, `AppSettings.cs` (stored in `%APPDATA%\7D2DMapEditor\settings.json`).

**Tests** (`tests/MapEditor.Core.Tests`): `WorldIoTests`, `PngTests`, `MapRendererTests`, `EditingTests`, `PaintTests` and `RoadTests`.

**CLI** (`src/MapEditor.Cli/Program.cs`) has five commands: `info`, `roundtrip`, `installs`, `render` and `new`.

**Docs:** `docs/RWG-Research.md` covers the file formats, the RWG pipeline and what has been verified in game.

## Changes Made (by milestone)

- **M1–2:** the solution scaffold, CI, the PNG codec, and world read/write that round-trips exactly.
- **M3:** the tiled mip-map viewer with hillshade, layer toggles, cursor readout and minimap.
- **M4:**
  - A New World dialog (size, noise, biome, and land or water edges using the game's border stamps).
  - Raise, Lower, Flatten, Smooth and Roughen brushes, and a Stamp tool.
  - Undo/redo, heightmap import/export, and Save As with a new `main.ttw` GUID.
  - A spawn point is added automatically if the world has none.
- **M5:** paint biome (B), paint radiation (D; Shift erases) and water fill (W; Shift+click drains, Ctrl+click samples a level).
- **Brush tips:** circle or square with Q/E rotation, and an Edge option (Smooth, Linear or Hard) for the terrain brushes.
- **M6:**
  - The Road tool (O): click to add points; double-click or Enter finishes, Backspace removes the last point, Esc cancels, and Shift+drag erases.
  - Asphalt or Gravel surface, a width slider (RWG defaults are 18 m and 9 m), and an optional grading checkbox.
- **Fixes in v0.1.1:**
  - Choosing not to save on exit no longer crashes. The window now closes through `Dispatcher.BeginInvoke(Close)` instead of calling `Close()` inside the Closing event.
  - Saving warns when the game already has saved games on that world.

## Failed Attempts & Gotchas

- **Saved games keep their chunks.** Saves in `%APPDATA%\7DaysToDie\Saves\<world>\<game>\Region` hold every chunk generated so far. After editing a world, the user must start a **new game** to see the edits there; a reused save mixes old terrain, plants and water with the new world. The editor now warns about this on save.
- **"All Ores in All Biomes by FNS" mod:** it caused `NullReferenceException` errors in `DecorateChunkOverlapping` at `Prefab.CopyIntoLocal` (in the decoration rock and ore-vein prefabs). The errors stopped once the user removed the mod. The flat ground in editor worlds made the errors more frequent. A possible follow-up is adding fine noise to the New World defaults; it isn't done.
- **Old versions:** a world whose `map_info.xml` has a `GameVersion` from a different major version makes the game warn that it's an old map. New worlds leave `GameVersion` out.
- **Empty `spawnpoints.xml`:** the game has no fallback spawn. `SpawnHelper.EnsureOne` runs on every save to cover this.
- **Game stamps:** their height channel is constant (255 to raise, 0 to carve) and the shape is in alpha. `water_border` has no water channel, so alpha is used as the water mask.
- **Road heights over water:** sampling the lake bed, or even clamping to 1 m above the water before smoothing, cut trenches into the approaches. The fix is to interpolate the road deck between the bank heights across wet stretches, then smooth while holding the minimum height on every pass.
- **Square brush rotation:** it must be clockwise on the north-up map, matching `StampOp` and the cursor's `RotateTransform`. A 45° test can't catch a sign error, so test with an asymmetric angle.
- **WPF drawing:** a single `StreamGeometry` with thousands of tiny rectangles draws stray spikes on the GPU. Prefab outlines are rasterised in software (`OverlayRaster`) because of this. The F12 snapshot doesn't show these GPU-only artifacts.
- **Build locks:** a running `MapEditor.exe` locks `bin/`, so the build fails. Never kill the user's editor, because it may have unsaved work. To check that the code compiles, build with `-p:OutDir=<scratch>`, or ask the user to close it.
- **Shell:** inline Python heredocs in Bash sometimes fail with "unexpected EOF". Write the script to the scratchpad and run it with `python -I <file>`. Windows Python can't open `/d/...` paths; use `D:\...`.
- **Visual testing (computer use):**
  - Request access to "MapEditor.exe" with `systemKeyCombos`.
  - An always-on-top window covers the M28U monitor, so move the editor with Win+Shift+Right to "LG ULTRAGEAR" and `switch_display` to it.
  - Message boxes can appear behind the masked window.
  - `left_click_drag` sends no intermediate moves; use `left_mouse_down`, then `mouse_move` steps, then `left_mouse_up`.
  - Don't drive the screen while the user is playing the game.
- **Headless checks:** a .NET 10 file-based script with `#:project <path to MapEditor.Core.csproj>` can build a world, apply operations and render PNGs with `MapRenderer`, without the UI. An example is in the session scratchpad as `roadcheck.cs`.

**Locations:**
- Game logs: `%APPDATA%\7DaysToDie\logs\output_log_client__*.txt`.
- Editor crash log: `%APPDATA%\7D2DMapEditor\crash.log`.
- The user plays on the Steam install at `D:\Games\Steam\steamapps\common\7 Days To Die`, with mods in `%APPDATA%\7DaysToDie\Mods`.
- The decompiled DLLs are in `D:\dotPeek Test\Assembly-CSharp`; RWG is under `WorldGenerationEngineFinal/`.
- The dev install is `C:\7D2D\Custom\v3.3`.
- `ilspycmd` is installed if you need IL offsets.

## Next Steps

1. **Start milestone 7: POIs and spawns.** Do it when the user asks, and confirm the scope with them first.
   - A prefab browser using `PrefabLibrary`: filter by tags, difficulty tier and size, and show the preview `.jpg`.
   - Placing, rotating, moving and deleting `PrefabInstance`s. Position is the bounding-box min corner in centred world coordinates, and rotation is 0–3. Use `PrefabInfo.Footprint(rotation)`.
   - Footprint flattening with a feathered edge. Y is the median ground height plus the prefab's `YOffset`. Read the decompiled `CopyWorldPrefabHeightsIntoHeightMap` (game load) and RWG `PrefabManager`/`POISmoother` first, to see how much flattening the game already does.
   - Warnings for overlaps, water and steep slopes.
   - A spawn point tool to add, move and delete spawns.
   - Street-tile (`rwg_tile_*`) placement for hand-built towns.
2. Keep each piece testable in game: save to `%APPDATA%\7DaysToDie\GeneratedWorlds`, start a **new game**, and check the log for errors.
3. Later (milestone 8): the `.7dmap` project format, which keeps roads as editable polylines (today they're baked to raster on finish). Also autosave, a validation panel, an Export dialog with a `GameVersion` option, a Velopack installer, and a check of the game EULA before release.
4. Optional ideas the user hasn't asked for:
   - Fine default roughness for New World terrain.
   - Lower memory when opening 8k worlds (about 1.2 GB today, from the temporary RGBA decode).
   - Keeping water for worlds that only ship `splat4_processed.png`.
