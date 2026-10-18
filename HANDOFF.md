# Handoff: 7 Days to Die Map Editor

Last updated 2026-10-18.

> **Update:** v1.0.0 has been released (tag `v1.0.0`, GitHub release from `release.yml`). The user's three last bugs were fixed first: Open World Folder starts in GeneratedWorlds, loading a world resets the tool to Pan, and Save Project starts in Documents and refuses the game's world folders. Still open: the EULA, and testing the installer. (Updates: the repo is now public and `Updates.cs` uses Velopack's GitHub source; see PLAN.md.) The rest of this file predates the release.

**Repo state:**
- **HEAD:** the commit that adds this handoff, on top of `d572321` "Polish towns, roads and placement from in-game testing" (tagged `v1.0.0b`). Everything is committed.
- **Remote:** the private repo https://github.com/jayfitt-droid/7d2d-map-editor (`origin`, signed in as `jayfitt-droid` through `gh`). `main` and every tag (`v0.1.0`, `v0.1.1`, `v0.1.2` at `f4d4509`, `v1.0.0b`) are pushed.
- **CI:** the `build` workflow passed on `d572321`.

## Goal

This is a Windows desktop app (C# + WPF on .NET 10, CommunityToolkit.Mvvm, xUnit) for building 7 Days to Die (v3.3) worlds. It edits an existing world or authors one from scratch, and it writes a world folder the game loads directly. It does not use the in-game generator (RWG) or the biome-brush mod.

`PLAN.md` has 9 milestones, a **Status** section and a **Decided** section. All nine are done, and the user has tested nearly everything in the editor and in game. The aim now is to ship **v1.0**.

Rules agreed with the user:
- **Public release:** never bundle game assets. Read stamps, prefabs, `biomes.xml` and `rwgmixer.xml` from the user's install at runtime. Reimplement behaviour from the decompiled code (`D:\dotPeek Test\Assembly-CSharp\`); never copy it in.
- **v1.0 gate:** the user keeps their own bug list, and every bug on it must be fixed before v1.0. Don't tag `v1.0.0` or publish a release until they say so. This is in memory as `mapeditor-project-decisions`.
- **How the user works:** they try each change in the app, then in a new game, and report back. Commit, tag or push only when they ask. Never add `Co-Authored-By` or other attribution lines to commit messages. Use the task-list and wrap-up tools, and keep replies short.
- **Simple visual fixes:** build and let the user check them; don't drive the app yourself (memory: `user-verifies-simple-ui`). Do check in the app yourself for alignment, performance, or anything they can't easily judge.
- **Design decisions** (how a feature should behave) are the user's: offer two or three options and let them choose.

## Current State

- **Everything is committed, pushed and passing.** 113 tests pass (`dotnet test MapEditor.slnx`), and the solution builds with no warnings.
- **The user's Debug build** at `src\MapEditor.App\bin\Debug\net10.0-windows\MapEditor.exe` is up to date.
- **No releases exist on GitHub.** The `v0.1.x` commits predate `release.yml`, and GitHub raises no push event when more than three tags go in one push, so the `v1.0.0b` push started nothing. `out/releases` locally is stale.
- **Confirmed by the user** (2026-10-17/18):
  - **In the editor:**
    - Towns: town anchoring and snapping. Link roads avoid prefabs and other towns, run straight, and copy the joined road. The Match option, and the Traders count.
    - Placement: hand-placed street tiles snap to the town's grid.
    - Map view: prefab detail on the map (smooth panning, and the Layers toggle), and right-drag panning.
    - Projects and files: saving and reopening projects, Open Recent, File > Close, autosave, and export.
    - Validation warnings: no spawn, no trader, overlapping POIs, flooded POI, spawn under water, and no biomes. Warnings never block export.
    - The UI look-over: "ui looks great".
  - **In game:** many exported worlds load. The user built their own road, a town and a city, link roads and hand-placed street tiles: "everything is working as expected". The log is clean (only an Xbox Live error at shutdown). After the resmoothing fix, roads beside towns no longer roll.
- **Not yet confirmed:**
  - **Out-of-bounds validation:** it can only be triggered with a hand-edited `prefabs.xml`, and the user agreed to skip it.
  - **Placement ghost picture:** the user hasn't commented on it yet, though nothing was reported wrong.
  - **The installer** (item 11): it hasn't been built.

## Active/Touched Files

All paths are relative to `C:\Users\Jason\Projects\7d2d\MapEditor`. This session's work is all in `d572321`.

- `src/MapEditor.Core/Ops/TownGenerator.cs`:
  - `TownOptions.LinkStyle` (`LinkRoadStyle.JoinedRoad`/`Tile`) and `TownOptions.Traders` (default 1).
  - `Plan` now runs `OpenGateways`, then `Filler.Fill()`, then `PlanLinks` (`PlanRoads` was split in two). Tiles are picked before link roads are routed.
  - `PlanLinks`: per-exit style from `TileStreet` (tile mode) or `MeasureRoad` at each candidate join point. `Clearance` is per candidate.
  - `MeasureRoad(Grid<byte>, Vector2)` (internal): it takes the road's direction from the long axis of road pixels within 24 m, and the width from the median run across it. Asphalt anywhere across the road wins.
  - `TileStreet` and `StreetAcross(PrefabTopView, dir)` (public, used by the CLI) read a tile's street at an exit edge. Asphalt comes first, plus a 1 m gravel shoulder.
  - In `Filler`, traders are a separate `_traders` list (never in `_candidates`). `Fill()` picks every tile, then `TraderLots(placed, count)` (the first is nearest the middle, then farthest-point; one per marker group). `ChooseTrader` fills them, and `IsTraderLot` puts a reserved lot first in its group.
  - `Apply` calls `RoadOp.Resmooth` after the tiles are flattened and before the link roads are drawn.
- `src/MapEditor.Core/Ops/RoadOp.cs`:
  - `Resmooth(world, area, keep, session)`: masked box means over road pixels, first 75 m radius, then 37 m. Full strength in the area, fading over `ResmoothReach` = 112 m beyond it; side ground follows within 6 m.
  - Helpers `MaskedMean` and `MaskedMeanWithCover`.
- `src/MapEditor.Core/Ops/PrefabPlacer.cs`: new `FlattenUnder`, which flattens and then calls `Resmooth`. Used by `Place` and `Move`.
- `src/MapEditor.Core/GameData/PrefabTopView.cs`: `PrefabTopViewReader.SurfaceOf(pixel)` maps a top-view colour to Asphalt, Gravel or None.
- `src/MapEditor.Core/Edit/UndoStack.cs`: `GridRect.Contains(x, z)`.
- `src/MapEditor.Core/GameData/GameInstall.cs` and `PrefabLibrary.cs`: prefabs also load from `%APPDATA%\7DaysToDie\Mods` (`UserModsDir`). This change was already in the working tree; it wasn't written in this session.
- `src/MapEditor.App/MainWindow.xaml(.cs)`:
  - File > Close (`CloseWorldCommand`, Ctrl+W, `CloseWorldAsync`).
  - Town options: a "Match" combo (`TownLinkStyle`, `xmlns:ops`) and a "Traders" combo (`TownTraders` / `TraderCounts`).
- `src/MapEditor.App/MainViewModel.cs`: `TownLinkStyle`, `TownTraders`, `TraderCounts`.
- `src/MapEditor.App/Editing/TownToolController.cs`: passes `LinkStyle` and `Traders`, and replans when they change.
- `src/MapEditor.App/Map/MapView.cs`:
  - Right-drag pans (`_panButton`), middle does nothing, and one drag at a time. `OnLostMouseCapture` clears a pan.
  - `DrawPrefabGhost` draws the top view at `GhostPictureOpacity` = 0.75.
- `src/MapEditor.App/Map/ToolInput.cs`: `PrefabGhost` gains `Info` and `Rotation`.
- `src/MapEditor.App/Editing/PrefabToolController.cs`: `Ghost` passes `info` and `spot.Rotation`.
- `src/MapEditor.App/ShortcutsWindow.xaml.cs`: adds "Ctrl+W Close" and "Right drag Pan with any tool". `README.md` has the same.
- `src/MapEditor.Cli/Program.cs`: `topview` prints `streets: N … E … S … W …` for each prefab.
- Tests:
  - New `tests/MapEditor.Core.Tests/ResmoothTests.cs`.
  - `TownTests.cs`: link-road matching, `MeasureRoad`, `StreetAcross`, the tile-street option, and four trader tests (`WithTrader` helper). `MakeLibrary(tileFolder)` takes a folder.
- `PLAN.md` Status and `HANDOFF.md`.

## Changes Made

This session's work, each confirmed by the user unless noted:

1. **Link roads copy the road they join:** surface and width, measured from splat3. The township's style (9 m asphalt, 4.5 m gravel) is only a fallback.
2. **Town tool "Match" option:** link roads match either the road they join (the default) or the town's street where it leaves the tile. Measured on vanilla tiles: gateways are 18 m asphalt (RWG's highway width), countrytown 10–12 m, downtown 8–10 m, residential 6 m, and oldwest is gravel.
3. **File > Close (Ctrl+W):** asks about unsaved changes, deletes the autosave, and returns to the start screen.
4. **Town tool "Traders" count (0–5, default 1):** RWG leaves traders to chance (`spawn_trader` is parsed but unused in v3.3). Now ordinary lots never get one, and exactly N lots that fit are kept for traders. Warnings appear if there are too few. It went through two earlier versions (a guarantee pass, then a checkbox) before the user asked for a count.
5. **Right-drag pans with any tool** (the user's request, as in image editors). Middle drag is free for a future feature.
6. **The placement ghost shows the prefab's top view** at 75% opacity. The user hasn't commented on it yet.
7. **Roads are re-smoothed after a town build or prefab flatten:**
   - The user's road rolled 0.3–0.5 m beside a town in game.
   - Cause: `Apply` flattens each 150 m tile in turn with a 110 m blend, and the overlapping blends leave a ripple every 150 m along a road beside the town. Away from towns the road was smooth to 6 cm, and `dtm_processed.raw` equalled `dtm.raw`.
   - The 151 m box cancels that ripple. In a test the roll fell from 0.374 m to 0.016 m, and the user confirmed it in game.
8. **Release housekeeping:**
   - Committed `d77b55f` (M7–M9) and `d572321` (this session).
   - Tagged `v0.1.2` and `v1.0.0b`.
   - Created the private GitHub repo and pushed everything.

## Failed Attempts & Gotchas

- **Tags and the release workflow:**
  - `release.yml` runs on `v*` tag pushes, but GitHub ignores a push of more than three tags at once.
  - Velopack needs semver, so `1.0.0b` would fail. Use `v1.0.0` or `v1.0.0-beta`, pushed on its own.
  - A tag only runs the workflow if its commit contains `release.yml`.
- **A resmooth window that's too short doesn't help:** 40 m passes only cut the town ripple by about 45%. The window must be about one tile (150 m) wide to cancel it.
- **A trader "guarantee" pass that refilled the town** added a second Filler run and could still give big towns several traders. It was replaced by keeping traders out of normal picks and reserving lots.
- **Lots in one marker group are alternatives:** only the first that gets a POI is filled. A reserved lot must be ordered first in its group.
- **Tile top views count gravel verges as street** unless asphalt is measured first. Count only an adjacent 1 m gravel shoulder.
- **PowerShell 5.1 `Set-Content -Encoding utf8` adds a BOM.** The repo's files have none, so edit with Edit/Write or Python, or strip the BOM afterwards.
- **Build locks:** a running `MapEditor.exe` locks `bin/`. Check `Get-Process MapEditor`, never kill the user's editor, and compile-check with `-p:OutDir=<scratchpad>\appbuild\`.
- **Bash heredocs holding C# or Python with quotes often fail.** Write the script to the scratchpad and run `python -I <file> <args>`.
- **Implicit styles in dialogs** must be `BasedOn="{StaticResource {x:Type ComboBox}}"` (and the same for TextBox), or the Fluent template is lost.
- **Block data (`.tts`):**
  - Layout: `"tts\0"`, a uint version, then int16 size x, y, z, then size×4 bytes of blocks, x fastest, then y, then z.
  - The block type is the low 16 bits from version 18 on, and 15 bits before that.
  - `.blocks.nim`: an int version, an int count, then (int id, a .NET string) pairs.
  - `terrainFiller` is transparent.
- **Classify skip words:** match "tree" with `StartsWith` only ("street" contains it), and use "carwreck", not "car".
- **Tile names may end in a variant number** (`_03`); strip it before matching the shape. `TileRotation` swaps 1 and 3.
- **Launching the editor with a world path changes the user's `LastWorldFolder` and `RecentFiles`.** Restore them afterwards.
- **Window captures:** an always-on-top window covers the screen centre. Use F12 in the editor (`%TEMP%\mapeditor-snapshot.png`) or a DPI-aware PrintWindow (4K at 150%).
- **Carried over:**
  - App.xaml keeps `StartupUri`.
  - One `OverlayRaster` per overlay.
  - Call `InvalidateRequerySuggested` after `IsBusy` changes.
  - Write `System.Windows.Threading.Dispatcher.Yield`.
  - No `HashCode` for seeds.
  - In-game tests need a **new game**.
  - Validate against v3.x worlds.
  - Test exports go to the scratchpad, never over the user's worlds.
- **Useful checks:**
  - **Road heights:** the scratch script measured bumps under splat3 roads from `dtm.raw`. Rebuild it if needed: road mask = splat3 PNG flipped (north-up) with RGB ≠ 0; height = u16 / 257.
  - **Game logs:** `%APPDATA%\7DaysToDie\logs\output_log_client__*.txt`.
  - **Exported worlds:** `%APPDATA%\7DaysToDie\GeneratedWorlds\` (the latest is "My World", 6144 m).

## Next Steps

1. **Fix any bugs the user reports** from their own list. That list gates v1.0.
2. **Prepare v1.0 when the user asks:**
   - Set `Updates.BuiltInFeed` (`src/MapEditor.App/Updates.cs`) to the repo's releases feed, `https://github.com/jayfitt-droid/7d2d-map-editor/releases/latest/download/`; check what Velopack's GitHub source expects first. Note that the repo is private, so an installed app can't read the feed without a token. Raise this with the user: make the repo public, or use a separate public releases repo.
   - Remind the user to read the game EULA (PLAN.md open item).
   - Update PLAN.md's Status for v1.0.
   - Build the installer with `build/release.ps1 -Version 1.0.0` and let the user test it (item 11). Or push a lone `v1.0.0` tag so the workflow publishes.
3. **Possible follow-ups (ask first):**
   - A use for the middle mouse button.
   - A separate autosave per editor window (they currently share one file).
   - A setting so `spawn_trader=false` townships default to 0 traders.
   - RWG's town lift as an option, and town names.
   - Planning large towns on a background thread.
   - Code signing.
   - Tuning the `Classify` colours.
