# Handoff: 7 Days to Die Map Editor

Last updated 2026-10-18. Tag `v1.0.0b` is on the latest commit on `main`, which holds all work to date. Earlier tags: `v0.1.2` at `f4d4509` and `v0.1.1` at `bdacad4`. There's no git remote.

The user tested the editor features and played exported worlds in game, and found no errors in the log. Don't tag `v1.0.0` until the user asks.

## Goal

This is a Windows desktop app (C# + WPF on .NET 10, CommunityToolkit.Mvvm, xUnit) for building 7 Days to Die (v3.3) worlds. It edits an existing world or authors one from scratch, and it writes a world folder the game loads directly. It does not use the in-game generator (RWG) or the biome-brush mod.

`PLAN.md` has 9 milestones, a **Status** section and a **Decided** section. All nine milestones are done in code. The current aim is to get the user's in-game sign-off, fix what they find, commit, and ship **v1.0**.

Rules agreed with the user:
- **Public release:** never bundle game assets. Read stamps, prefabs, `biomes.xml` and `rwgmixer.xml` from the user's install at runtime. Reimplement behaviour from the decompiled code (`D:\dotPeek Test\Assembly-CSharp\`); never copy it in.
- **How the user works:** they try each change in the app, then in a new game, and report back.
  - Commit only when they ask. Never add `Co-Authored-By` or other attribution lines to commit messages.
  - Use the task-list and wrap-up tools, and keep replies short.
  - **Simple visual fixes:** build and let the user check them; don't launch and drive the app to verify them (their words: "to save tokens"). It's saved in memory as `user-verifies-simple-ui`. Do check in the app yourself for alignment, performance, or anything they can't easily judge.

## Current State

- **The user is testing** with the Debug build at `src\MapEditor.App\bin\Debug\net10.0-windows\MapEditor.exe`. It is up to date with every change below.
- **Confirmed in the app by the user:**
  - The Town tool anchoring and snapping ("it does": a town drawn against a road now sits flush with it).
  - The New World dialog fix, the No-preview placeholder, Open Recent and the shortcuts window. They reported nothing further on these.
  - Prefab detail (2026-10-18): panning is smooth in a dense city, and the Layers toggle works.
  - Link roads in the editor (2026-10-18): they avoid prefabs and other towns, warn when an exit is left unjoined, and run straight.
  - Hand-placed street tiles snap to the grid of the town beside them (2026-10-18).
  - Saving and reopening a project, and Open Recent (2026-10-18).
  - Export (2026-10-18): the user has loaded many exported worlds in game.
  - The Paint-style UI (M9) look-over (2026-10-18): "ui looks great".
  - Validation (2026-10-18): the no-spawn-point, no-trader, overlapping-POI, flooded-POI, spawn-under-water and no-biomes warnings show (no biomes was tested by deleting biomes.png from an export), and export still goes ahead (warnings never block it). Only the out-of-bounds check is untested.
  - Autosave (2026-10-18). Known limit: every editor window shares one autosave file, `%APPDATA%\7D2DMapEditor\autosave\autosave.7dmap`.
- **Not yet confirmed:**
  - Link roads in game (in the editor they're confirmed).
  - All of M8/M9 in game: projects, autosave, validation, export, installer, the Paint-style UI.
  - M7 in game: tiles lining up, POI heights, gateways meeting roads, no errors in the log.
- **Tests:** 97 pass (`dotnet test MapEditor.slnx`). The whole solution builds with no warnings.
- **Release build:** `out/releases` predates all of this and must be rebuilt before shipping.
- **Clean-up:** none of my editor instances are running. I restored the user's `%APPDATA%\7D2DMapEditor\settings.json` after my test launches: LastWorldFolder and RecentFiles point at the scratch `Mezawoku Mountains.7dmap`. The prefab detail cache at `%LOCALAPPDATA%\7D2DMapEditor\PrefabDetail` holds about 400 small PNGs; that's expected.

## Active/Touched Files

All paths are relative to `C:\Users\Jason\Projects\7d2d\MapEditor`.

**This session and the last one:**
- `src/MapEditor.Core/GameData/PrefabTopView.cs` (new):
  - `PrefabTopViewReader.Read(ttsPath, yOffset)` parses the `.tts` file and its `.blocks.nim` name map.
  - `Classify` sorts block names into colours.
  - `PrefabTopView.Turned(rotation)` turns the picture the way `PrefabMarker.TurnedOnce` turns markers.
- `src/MapEditor.App/Map/PrefabDetailCache.cs` (new): builds pictures in the background (2 workers, newest request first). It keeps them in memory and in the disk cache at `%LOCALAPPDATA%\7D2DMapEditor\PrefabDetail\{name}-{ticks^length:x}.png`. `Get(info, rotation)` returns null while a picture is being built, and raises `Built` when it's done.
- `src/MapEditor.App/Map/MapView.cs`:
  - `Prefabs` is now a property with a backing field, so setting it makes a new `PrefabDetailCache`.
  - New `ShowPrefabDetail` dependency property.
  - `DrawPrefabs` draws the pictures first (tiles, then POIs) for prefabs whose shorter side is 40 device pixels or more on screen (`DetailMinPixels`). Pictured prefabs skip the tint fill.
  - `DrawStreetLines` sketches a tile's streets from its shape and rotation when the tile is 12–40 device pixels on screen.
  - `DrawTownPreview` now also draws the drag cells, the drag rectangle, and the red "too small" box and label.
- `src/MapEditor.App/Map/OverlayRaster.cs`: new `Bar` method, a fill at least 1 px thick.
- `src/MapEditor.App/Map/ToolInput.cs`: `TownPreview` gains `Drag`, `Cells` and `TooSmall`.
- `src/MapEditor.App/Editing/TownToolController.cs`:
  - `Snapped(start, end, worldSize)` snaps the drag to whole cells from the start corner and keeps it inside the world.
  - New `_drag` field. A drag that's too small keeps the red box until Esc or the next drag.
  - New tool hint and size text.
- `src/MapEditor.Core/Ops/TownGenerator.cs`:
  - `TownTile(cx, cz, originX, originZ)` and `TownPlan.OriginX/OriginZ`. Cells now count from the selection corner: `CellsIn`, `Plan`, `RoadPixels`.
  - `PlanRoads(..., obstacles)` keeps link roads clear of prefabs that stay and of other towns. It uses `NearestWhere` and `SegmentHits` (a Liang–Barsky test), with a clearance of the road radius plus its blend distance.
  - The town's `Removed` list is now worked out before `PlanRoads`.
- `src/MapEditor.Core/Ops/PrefabPlacer.cs`: the new `TileGridOffset` makes hand-placed street tiles follow the nearest street tile's grid, and `SpotAt` uses it.
- `src/MapEditor.App/AppSettings.cs`: `RecentFiles` (10 at most) and `AddRecent`. On load, the list is seeded from `LastWorldFolder`.
- `src/MapEditor.App/MainWindow.xaml(.cs)`:
  - File > Open Recent (`RecentMenu`, filled by `FileMenu_SubmenuOpened`, opened by `RecentItem_Click`). `AddRecent` runs in `OpenProjectAsync`, `OpenWorldAsync` and `SaveProjectAsync`.
  - `ShowShortcuts` opens one `ShortcutsWindow` that doesn't block the editor.
  - Layers popup: new "Prefab de_tail when zoomed in" checkbox.
- `src/MapEditor.App/ShortcutsWindow.xaml(.cs)` (new): grouped key-caps in a WrapPanel, 800×620. Esc or F1 closes it.
- `src/MapEditor.App/NewWorldDialog.xaml`: the local styles are now `BasedOn` the theme's styles, the edges are a 2×2 grid, and the error text uses `SystemFillColorCriticalBrush`.
- `src/MapEditor.App/App.xaml`: new `PrefabPreview` ContentControl style (picture, or a "No preview image" box). It's used for the browser preview and the info card in `MainWindow.xaml`.
- `src/MapEditor.App/MainViewModel.cs`: new `ShowPrefabDetail` property.
- `src/MapEditor.Cli/Program.cs`: new `topview <out.png> <prefab>[:rotation] ...` verb.
- Tests:
  - New `tests/MapEditor.Core.Tests/PrefabTopViewTests.cs`.
  - `TownTests.cs`:
    - New `Covering` helper.
    - New tests: `Link_roads_keep_out_of_a_neighbouring_town`, `Cells_start_at_the_selection_corner`, `A_street_tile_placed_by_hand_follows_the_grid_of_the_town_beside_it`.
- `PLAN.md`: the new **Decided** section records the town placement decision.

**Earlier uncommitted work:** M7, M8 and M9. See `PLAN.md` Status and the git status for those files (`ProjectStore.cs`, `Validate/`, `ExportDialog`, `Updates.cs`, `Program.cs`, `Icons.cs`, `build/release.ps1`, `.github/workflows/release.yml`, and others).

## Changes Made

1. **No-preview placeholder.** Prefabs without a `.jpg`, such as the 150 m `part_road_*` tiles, show a "No preview image" box. The game ships no image for these.
2. **Open Recent** on the File menu, with entries numbered for Alt+F, R, 1–9. A missing entry offers to remove itself, and there's a Clear Recent List item.
3. **The Keyboard Shortcuts window** is now styled like the app.
4. **New World dialog:**
   - Its own TextBox/ComboBox styles had replaced the Fluent theme's styles, so the Biome list was drawn light-on-white and looked empty. Fixed with `BasedOn`.
   - The edge boxes then got too narrow, so they're now a 2×2 grid.
5. **Straight link roads.** After the 40 m lead, a link road heads for the road nearest the end of the lead, not the road nearest the exit. Before, it hooked out and slanted back.
6. **Link roads keep clear of prefabs and other towns.** When there's no clear path, the exit is left unjoined with the warning "N town exits left unjoined: prefabs or another town are in the way". The user's road that ran through a POI and was "very hilly" in game came from this. The hills were almost certainly the POI's own ground painted with road texture; I didn't confirm that in game.
7. **Prefab detail on the map** (options 1 and 2 of the plan the user chose):
   - Close up: top-down pictures of tiles and POIs, built from their block data.
   - Mid zoom: street lines sketched from each tile's shape.
   - A Layers toggle switches it off.
   - Measured: Mezawoku's 1,158 different prefabs build in 3.2 s total on one thread (about 10 ms for a full street tile), using 13 MB of pixels.
   - Checked on Mezawoku that the pictures line up and turn correctly: tile streets meet each other and meet the splat roads.
8. **Town placement (option 1):**
   - Cells start at the drag's start corner, so the town sits flush with what was drawn. The user confirmed this in the app.
   - The drag snaps to whole 150 m cells, and the cells show while dragging.
   - A drag that's too small turns red with a note.
   - Hand-placed street tiles follow the nearest town's grid.

9. **Link roads copy the road they join (2026-10-18, confirmed by the user).** `TownGenerator.MeasureRoad` reads the surface and half-width from splat3 at the join point. It takes the road's direction from the long axis of the road pixels within 24 m, and the width from the median run across it. Asphalt anywhere across the road makes it asphalt (highways have gravel shoulders). The township's own style (9 m asphalt or 4.5 m gravel) is only the fallback. Clearance from prefabs now uses each candidate road's measured width.
10. **Town tool option "Match: The road they join / The town's streets" (2026-10-18, confirmed by the user).** `TownOptions.LinkStyle`. For "The town's streets", `TileStreet` and `StreetAcross` read the tile's top view at the exit edge. They take the median, over lines 1–5 m in from the edge, of the asphalt run nearest the middle, plus 1 m gravel shoulders; gravel is used only when there's no asphalt. To make this work, `Plan` now runs `OpenGateways`, then `Filler.Fill`, then `PlanLinks`, so tiles are picked before roads are routed. Measured on vanilla tiles: gateways are 18 m asphalt (RWG's highway width), countrytown 10–12 m, downtown 8–10 m, residential 6 m, and oldwest is gravel. The CLI `topview` verb now prints each edge's street.
11. **File > Close (Ctrl+W) (2026-10-18, confirmed by the user).** `CloseWorldAsync` in `MainWindow.xaml.cs` asks about unsaved changes, deletes the autosave, clears the world, tools, undo and Issues, sets `Map.Renderer` to null, and shows the start panel again. It's listed in the shortcuts window.
12. **Town tool "Traders" count, 0–5, default 1 (2026-10-18, confirmed by the user).** `TownOptions.Traders`. RWG leaves traders to chance (`spawn_trader` is parsed but unused in v3.3), so small towns rarely get one and big ones get several. Now the Filler keeps traders out of the ordinary candidates (`_traders` is a separate list), and `TraderLots` keeps that many street-tile lots that a trader fits, at most one per marker group. The first is nearest the town's middle; each next one is the farthest from those already kept. `ChooseTrader` fills them, ignoring district, density and repeat rules. Fewer lots than asked gives a warning ("only K of N traders…" or "no traders: no lot in this town is big enough for one"). The township's spawn_trader flag is ignored: the count is what the user gets. `Fill` picks every tile before filling any lot.
13. **Right-drag pans with any tool; middle drag does nothing for now (2026-10-18, confirmed by the user).** The user asked for this, as in image editors. In `MapView.OnMouseDown`, a second button pressed mid-drag is ignored, and `OnMouseUp` only ends a pan on the button that started it. `OnLostMouseCapture` clears a pan. The shortcuts window and README say "Right drag". The user may want the middle button for something later.
14. **Placement ghost shows the prefab from above (2026-10-18).** `PrefabGhost` carries `Info` and `Rotation`. `MapView.DrawPrefabGhost` draws `_detail.Get(info, rotation)` at 75% opacity (`GhostPictureOpacity`) inside the ghost outline, at any zoom and whatever the Layers toggle says. It falls back to the plain tint while the picture is built, or for prefabs without block data.
15. **Roads are re-smoothed after a town build or prefab flatten (2026-10-18, confirmed by the user in game).** The user's road in "My World" rolled 0.3–0.5 m beside a town in game. The heightmap showed why: `TownGenerator.Apply` flattens each tile in turn with a 110 m blend, and along a road beside the town the overlapping blends leave a ripple every 150 m (plus a ramp at the town's edge). Away from towns the road was smooth to 6 cm, and dtm_processed.raw matched dtm.raw exactly. `RoadOp.Resmooth(world, area, keep, session)` averages the heights of road pixels: a 151 m box (which cancels a 150 m ripple), then a 75 m one, masked to road pixels, so a road stays level across. It's full strength in the area and fades out over `ResmoothReach` (112 m) beyond it; ground within about 6 m beside the road follows. Footprints in `keep` are untouched. It's called in `Apply` after the tiles are flattened and before the link roads are graded, and from `PrefabPlacer.FlattenUnder` (Place and Move). Test: the roll beside a model town drops from 0.374 m to 0.016 m. Existing worlds only change when a town or prefab is built again.

## Failed Attempts & Gotchas

- **Implicit styles in dialogs must be `BasedOn="{StaticResource {x:Type ComboBox}}"`** (the same for TextBox). Without it the control drops the Fluent template: old-style boxes, and invisible light text in the drop-down list. Don't base a TextBlock style on `{x:Type TextBlock}` (the theme may not define one).
- **Fluent controls are wider and taller.** A layout that fitted the old controls, such as four combos in a row, may clip.
- **Block data (`.tts`):**
  - Layout: `"tts\0"`, a uint version, then int16 size x, y, z, then size×4 bytes of blocks, stored x fastest, then y, then z.
  - The block type is the low 16 bits from version 18 on, and the low 15 bits before that. Vanilla v3.3 has versions 13–19, about 40% of them below 18.
  - `.blocks.nim` layout: an int version, an int count, then (int id, a .NET `ReadString` name) pairs.
  - `terrainFiller` means "keep the ground", so draw it transparent.
- **Skip words in `PrefabTopViewReader.Classify`:** match "tree" with `StartsWith` only, because "street" contains it. Use "carwreck", not "car".
- **Street tile names can end in a variant number** (`rwg_tile_industrial_t_03`). Strip it before matching the shape. Shapes: straight, t, intersection, cap, corner.
- **A tile's rotation and its exits:** `TileRotation` swaps 1 and 3. To get exits from a placed tile, swap back, subtract `RotationToFaceNorth`, then use `ShapeExitsAt`.
- **Bash heredocs holding C# or Python with quotes often fail** with "unexpected EOF". Write the edit script to the scratchpad and run `python -I <file> <args>`.
- **Window captures:** an always-on-top window from another app covers the middle of the screen in computer-use screenshots.
  - Use F12 in the editor, which saves `%TEMP%\mapeditor-snapshot.png` (software-rendered).
  - Or use PrintWindow from a DPI-aware process. The screen is a 4K display at 150% scaling, so call `SetProcessDPIAware` or the capture comes out cropped.
  - UI Automation (`ValuePattern`, `SelectionItemPattern`, `TransformPattern.Move`) is more reliable than typing. Clipboard typing fails without a clipboard grant.
- **Launching the editor with a world path changes the user's `LastWorldFolder` and `RecentFiles`.** Restore them afterwards.
- **Build locks:** a running `MapEditor.exe` locks `bin/`. Check `Get-Process MapEditor`, never kill the user's editor, and build with `-p:OutDir=<scratchpad>\appbuild\` to check compiles.
- **Carried over from the earlier handoff:**
  - App.xaml must keep `StartupUri`.
  - Each overlay needs its own `OverlayRaster`.
  - Call `InvalidateRequerySuggested` after `IsBusy` changes.
  - Write `System.Windows.Threading.Dispatcher.Yield`, not `Dispatcher.Yield`.
  - Don't use `HashCode` for seeds.
  - In-game tests need a **new game**.
  - Validate against v3.x worlds such as Mezawoku, not Alpha 21 ones.
  - When testing exports, write to the scratchpad, never over the user's worlds.

## Next Steps

1. **Wait for the user's test results and fix what they report.** Things still to check:
   - Link roads in game.
   - Towns in game: tiles, POI heights, gateways, the log.
   - Projects, autosave, validation and export.
2. **Traders:** handled by the option in Changes Made 12. Earlier city plans in the forest had 0 traders, but a recent town got 2. If towns without traders keep turning up, look at `Filler` and the `GetPrefabWithDistrict` port in `TownGenerator`.
3. **When the user asks, commit in parts.** A suggested split:
   - "Add town generator (milestone 7, part 2)": the township fix, link-road fixes and town anchoring.
   - "Add projects, validation, export and installer (milestone 8)": also Open Recent.
   - "Rework the main window in a Paint-style layout (milestone 9)": also the shortcuts window, the dialog fixes and the no-preview box.
   - "Show prefabs from above on the map": `PrefabTopView`, `PrefabDetailCache`, the street lines and the CLI `topview` verb.
4. **For v1.0:**
   - Update PLAN.md's Status section.
   - Add a git remote and set `Updates.BuiltInFeed` to its releases URL.
   - The user should read the game EULA.
   - Rebuild with `build/release.ps1`, or tag `v1.0.0` and let the workflow publish.
5. **Testing setup:** the scratch world copies are in `C:\Users\Jason\AppData\Local\Temp\claude\C--Users-Jason-Projects-7d2d-MapEditor\b1dc4d9c-0928-4b9f-8767-755617d04eda\scratchpad\worlds\`: `Mezawoku Mountains/` and `Mezawoku Mountains.7dmap`. If they're gone, copy `%APPDATA%\7DaysToDie\GeneratedWorlds\Mezawoku Mountains` into the new session's scratchpad.
6. **Possible follow-ups:**
   - RWG's town lift (+25 m in snow, +12 m in wasteland) as an option.
   - Planning large town selections on a background thread.
   - Town names.
   - Code signing for the installer.
   - A warning when the chosen township doesn't suit the biome or size.
   - Tuning `Classify` colours and skip words as the user looks at more prefabs.
