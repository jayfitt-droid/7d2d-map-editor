# Handoff: 7 Days to Die Map Editor

Last updated 2026-10-16. The last commit is `1b08f09` "Add prefab and spawn tools (milestone 7, part 1)" on `main`; tag `v0.1.1` is at `bdacad4`. **The town generator is uncommitted** (files below). There's no git remote.

## Goal

This is a Windows desktop app (C# + WPF on .NET 10, CommunityToolkit.Mvvm, xUnit) for building 7 Days to Die (v3.3) worlds. It can edit an existing world or author one from scratch, and it writes a world folder the game loads directly. It does not use the in-game generator (RWG) or the biome-brush mod.

Rules agreed with the user:
- **Public release:** never bundle game assets. Read stamps, prefabs, `biomes.xml` and `rwgmixer.xml` from the user's install at runtime. Reimplement behaviour from the decompiled code; never copy it in.
- **The plan:** `PLAN.md` in the repo (a copy of `C:\Users\Jason\.claude\plans\glittery-munching-pudding.md`) has 9 milestones. M1–M6 are done. **M7 (POIs and spawns) is nearly done.** M8 covers the `.7dmap` format, validation, the export dialog, and the installer. M9 is UI polish: a Paint-style layout with a ribbon, palette, side sliders and a status bar. v1.0 ships after M9.

**How the user works:** they try each change in the app, then in a new game, and report back. **Commit only when they ask.** Never add `Co-Authored-By` or other attribution lines to commit messages. Use the task-list and wrap-up tools, and keep replies short.

## Current State

- **Committed and checked in game:** the Prefabs tool (I) and the Spawn points tool (H).
- **Committed, not yet confirmed in game by the user:** the prefab info card (Q/E, PgUp/PgDn, Replace, Delete), haloed outlines, "Dim the map", magenta traders, and POIs carrying their parts.
- **New this session, uncommitted, not yet tried by the user in the app or in game: the Town tool (G).**
  - Drag a rectangle. The town fills the whole 150 m cells inside it and shows as a preview: district tints, streets, POI outlines (traders in magenta), the prefabs it replaces in red, and link roads in green.
  - Panel: township (defaults by biome at the centre and by cell count against `TownshipType.Tiles`), seed with Reroll, "Replace prefabs in the way", "Join exits to the nearest road", "Add street furniture and props". Any change plans again.
  - Build (Enter) applies it as one undo step; Esc cancels.
  - 78 tests pass (13 new in `TownTests.cs`). The app builds. No screen check yet: the user declined computer-use access this session.
- There's a stray `HANDOFF - Copy.md` that the user made; leave it alone.

## Files touched this session

All paths are relative to `C:\Users\Jason\Projects\7d2d\MapEditor`.

- `src/MapEditor.Core/Ops/TownGenerator.cs` (new):
  - `TownOptions`, `TownTile` (cell, district, exit bits 0 N/1 E/2 S/3 W, `ExitPoint` = `HighwayExitFromDir`), `TownPlan`.
  - `Plan` → `LayOut` (core/outskirts/districts/gateways/exits) → `PlanRoads` → `Filler` (tiles, lots, parts). `Apply` does the world edit.
  - Public helpers used by tests: `ShapeFor`, `ShapeExitsAt`, `TileRotation`, `AssignDistricts`, `CentreTile`, `TownGround`, `CellsIn`, `DefaultTownship`, `BiomeTag(At)`.
- `src/MapEditor.Core/Ops/PrefabPlacer.cs`: new `LotSpot` (shared by `FindLot` and the generator). `MarkerParts` takes an optional shared `taken` list (RWG keeps part bounds per street tile).
- `src/MapEditor.App/Editing/TownToolController.cs` (new); `EditorTool.Town`; VM town properties (`Mixer`, `Townships`, `SelectedTownship`, `TownSeed`, `TownReplace`, `TownRoads`, `TownParts`, `TownText`, `HasTownPlan`); `MapView.TownPreview` + `DrawTownPreview`; `TownPreview`/`TownTilePreview` records in `ToolInput.cs`; the panel in `MainWindow.xaml`; `LoadMixer()` in `MainWindow.xaml.cs` (reads rwgmixer.xml for the open world's size).
- `tests/MapEditor.Core.Tests/TownTests.cs` (new).
- `docs/RWG-Research.md` §3 item 6: town facts.

## How the generator works (ported from `D:\dotPeek Test\Assembly-CSharp\WorldGenerationEngineFinal\`)

- **Cells:** whole 150 m cells in the selection. Cells with standing water are left out. Cells with prefabs are left out unless replacing; when replacing, every prefab overlapping a town tile is removed.
- **Rings:** the outer ring of cells is kept for gateways and the next for outskirts (core = cells with all cells within 2 selected; then 1; then all). Without this, a rectangle left almost no room for gateways (1 instead of RWG's ~7).
- **Outskirts** (`TownPlanner.Grow`), **districts** (`DistrictPlanner.GenerateDistricts`), **gateways** (`GenerateGatewayDir`, ×2 for citybig), **exits** (removal between districts, `ChangeLConnectionToCap` once per kind): as in RWG.
- **Gateways** always get the exit opposite their town street, because no gateway cap prefab exists. A lone roadside tile runs its street along the axis towards the nearest road. A town with no gateway opens the outer edge nearest a road.
- **Link roads:** from each open exit to the nearest road pixel ahead of it within 2 km (points: exit, 40 m out, the road). Asphalt radius 9 for town/city/citybig/wasteland_city/bforest_town, else gravel 4.5. Graded with `RoadOp.Draw`.
- **Tiles:** `rwg_tile_` + district prefab name + shape, random among library prefabs with that prefix within `TileCounts` max (min count boosts). Rotation = (shape rotations + RTFN) & 3 with 1↔3 swapped. Ground = ceil(avg of 9 samples) + 3; tile y = `PrefabY`.
- **Lots:** RWG's `SpawnMarkerPartsAndPrefabs` and `GetPrefabWithDistrict` (density budget, lot size rules, theme and name repeat distances, spawn adjust scores, retries at +8/0.3 and 18/0). Gateways also get `part_highway_transition` on sides with a street into town.
- **Apply:** remove what's in the way; clear splat3 roads in tile cells (RWG has none there); flatten each tile to the ground (blend 50 or 110 m); add prefabs; draw link roads.
- Seeds use `System.Random` and a fixed FNV hash, so the same seed gives the same town on every run. It is not bit-exact with RWG (which uses `GameRandom`).

## Checks against RWG (Mezawoku Mountains, v3.30.18)

- Scratch scripts in `C:\Users\Jason\AppData\Local\Temp\claude\C--Users-Jason-Projects-7d2d-MapEditor\281a6e77-6a0b-4dd3-89a2-8219715a23cf\scratchpad\towncheck\` (.NET file-based, `#:project` the Core csproj; they may be gone):
  - `districts.cs <world>`: re-plans every town's districts from its own tiles. 89.2% of core tiles match (730/818); 18 of 25 towns match fully.
  - `gen.cs <world> <township> <town index> <seed>`: replans a town's bounding box. A 135-cell city: RWG 136 tiles, 653 POIs, 592 parts, 3 traders; ours 129 tiles, 616 POIs, 570 parts, 2 traders, 4 gateways and 4 link roads. Plan 0.2 s, apply 0.6 s.
  - `splat.cs`: showed splat3 is empty under RWG town tiles.

## Failed attempts and gotchas

- **Old worlds:** worlds made in Alpha 21 ("Vafuru Valley", "Zezepexe County") have different YOffsets. Validate only against v3.x worlds such as **Mezawoku Mountains**. Govosare Valley is v3.2, and its parts differ.
- **Overlapping lots:** `FindLot` picks the smallest lot that fits.
- **Build locks:** a running `MapEditor.exe` locks `bin/`. **Never kill the user's editor.** Build with `-p:OutDir=<scratchpad>\appbuild\` instead. Check first with `tasklist //FI "IMAGENAME eq MapEditor.exe"`. At the end of this session an editor I launched on the scratch Mezawoku copy (`8f2dd0dc-...\scratchpad\worlds\Mezawoku Mountains`) was still open, with no user changes.
- `HashCode.Combine` is randomised per process; don't use it for seeds.
- **Shell:** inline `python - <<'EOF'` heredocs mostly work now; if one fails with "unexpected EOF", write the script to the scratchpad and run it with `python -I <file>`.
- **Computer use:** request "MapEditor.exe" only after the app is running; ask first, the user may decline. Use `left_mouse_down`, `mouse_move` steps and `left_mouse_up` for drags. Don't drive the screen while the user is using the editor or playing.
- **Saved games:** they keep generated chunks, so in-game tests need a **new game**.

## Next steps

1. The user tries the Town tool in the app, then builds a town and checks it in a new game (street tiles line up, POIs sit in lots at the right height, gateways meet the link roads, no log errors). Fix what they report.
2. Then ask whether to commit it ("Add town generator (milestone 7, part 2)").
3. Possible follow-ups: RWG's +25 m snow / +12 m wasteland town lift as an option; a background thread for planning very large selections; town names (`RandomCountyNameGenerator`) if the game shows them.
4. Then finish M7 and move on to M8 (validation, `.7dmap`, export dialog, installer).
