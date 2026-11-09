# 7D2D Map Editor: User Manual

This manual is for version 2.0.0. It tells you how to make and change worlds for 7 Days to Die.

## Contents

1. [What the editor does](#1-what-the-editor-does)
2. [Before you start](#2-before-you-start)
3. [The window](#3-the-window)
4. [Worlds and projects](#4-worlds-and-projects)
5. [The tools](#5-the-tools)
6. [The Generate panel](#6-the-generate-panel)
7. [The six stages](#7-the-six-stages)
8. [Example workflows](#8-example-workflows)
9. [Validate and export the world](#9-validate-and-export-the-world)
10. [Keyboard shortcuts](#10-keyboard-shortcuts)
11. [Words in this manual](#11-words-in-this-manual)
12. [Report a bug](#12-report-a-bug)

## 1. What the editor does

The editor makes and changes worlds for 7 Days to Die. You can open a world that the game generated, or you can make a new world.

You can make a world in two ways:

- **With the tools.** You shape the ground, paint biomes, draw roads and put each POI, town and spawn point on the map.
- **With the Generate panel.** The editor makes the world in six stages, the way the game's world generator does.

**You can use the tools and the stages together.** For example, generate the terrain, then change one mountain with the Raise tool. Or draw a city with the Town tool, then let the Highways stage connect it to the other towns. Every stage and every tool changes the same world.

The editor writes a world folder that the game loads directly. The game does not need a mod.

The editor reads stamps, prefabs and biome colours from your copy of the game. It does not include game files.

## 2. Before you start

You need:

- Windows 10 or 11 (64-bit).
- 7 Days to Die, version 3.x.

### Install the editor

Download the editor from the [releases page](https://github.com/jayfitt-droid/7d2d-map-editor/releases). Use one of these files:

- `7D2DMapEditor-win-Setup.exe` installs the editor for your Windows user. It does not need administrator rights.
- `7D2DMapEditor-win-Portable.zip` does not install. Unzip it and run `MapEditor.exe`.

Windows SmartScreen can show "Windows protected your PC". The editor is not signed. Click **More info**, then click **Run anyway**.

To get a newer version, click **Help > Check for Updates**.

### Set the game folder

The editor finds the game through Steam. If it cannot find the game, click **Settings > Game Folder**. Select the folder that contains the `Data` folder.

## 3. The window

The window has five areas.

| Area | What it holds |
|---|---|
| **Ribbon** (top) | The tools in groups: Navigate, Terrain, Paint and Build. It also holds the **Palette** (biome colours and road surfaces), **Layers** (what the map shows), **Validate** and **Export**. |
| **Tool options** (below the ribbon) | The settings for the tool that you use, and a hint in one line. Click **∨** at the right of the ribbon to hide them. |
| **Left rail** | The **size** and **strength** sliders for the brush. |
| **Side panel** (right) | Five tabs: **Prefabs**, **Placed**, **Issues**, **World** and **Generate**. Drag the left edge of the panel to make it wider or narrower. |
| **Status bar** (bottom) | The cursor position, height, biome and prefab under the cursor. It also shows the brush size, the world size, messages and the zoom. |

On a short window, the ribbon becomes one row of icons. To keep the ribbon full or compact, click **View > Ribbon**.

### Move around the map

- Turn the mouse wheel, or press **+** and **-**, to zoom.
- Press **Home** to see the whole map.
- Drag with the right mouse button to move the map. This works with every tool.

## 4. Worlds and projects

### Make a new world

1. Click **New World** (Ctrl+N).
2. Type a name, and set the size, the ground height and the seed.
3. Under **Biomes**, select one of these:
   - **One biome** paints one biome on the whole map.
   - **All biomes (generated layout)** makes a layout of all the biomes, as the game does. Select where the biomes start: forest or wasteland in the middle, a ring, two rings, or a line. Then set the **Share of the map** for each biome.
4. Set the **Edges**.
5. Click **Create**. The editor opens the Generate panel at stage 1.

The same seed always gives the same biome layout. A biome in the middle stays away from the map edges, so it can get a little less than its share.

### Open a world

- Click **Open** (Ctrl+O). Select a project (`.7dmap`), or the `dtm.raw` or `map_info.xml` of a world.
- Or click **File > Open World Folder** and select the world folder.

### Save your work

Click **Save** (Ctrl+S). The editor saves a project file (`.7dmap`). A project keeps all of the world and all of the Generate settings.

If you opened a world folder, Ctrl+S saves the world back to that folder.

The editor also saves a copy every 5 minutes. If the editor does not close normally, it offers to recover your work when you start it again.

### Undo

Press **Ctrl+Z** to undo and **Ctrl+Y** to redo. Each stroke, each placed item and each stage run is one undo step.

If an edit fails, the editor puts the world back as it was and shows a message. A failed road or town keeps its points or plan, so you can try again.

## 5. The tools

Click a tool on the ribbon, or press its key.

### Navigate

| Tool | Key | What it does |
|---|---|---|
| **Select** | V | Selects a POI, trader, street tile or spawn point. See [Select and move items](#select-and-move-items). |

### Terrain

These tools use a brush. Set the brush size and strength on the left rail. Press **[** or **]** to make the brush smaller or larger.

| Tool | Key | What it does |
|---|---|---|
| **Raise** | R | Makes the ground higher. |
| **Lower** | L | Makes the ground lower. |
| **Flatten** | F | Makes the ground the same height. Ctrl+click to take the height from the map. |
| **Smooth** | S | Makes the ground smoother. |
| **Roughen** | N | Makes the ground rougher. |
| **Stamp** | T | Puts one of the game's terrain stamps on the ground. Press Q or E to turn the stamp. |

With a square brush, press Q or E to turn the brush.

### Paint

| Tool | Key | What it does |
|---|---|---|
| **Paint biome** | B | Paints the biome that you select in the Palette. |
| **Paint radiation** | D | Paints radiation. Shift+drag removes radiation. |
| **Water fill** | W | Fills an area with water. Ctrl+click to take the water level from the map. |

### Build

| Tool | Key | What it does |
|---|---|---|
| **Road** | O | Draws a road. See [Roads](#roads). |
| **Prefabs** | I | Puts a POI, street tile or part on the map. Select it in the **Prefabs** tab. |
| **Trader** | A | Puts a trader on the map. Select a trader in the tool options, or click **Random**. |
| **Town** | G | Makes a whole town in a rectangle that you drag. See [Towns](#towns). |
| **Spawn points** | H | Puts player spawn points on the map. |

### Select and move items

With the **Select** tool:

- Click an item to select it. The info card shows its picture, tier and details.
- Drag the item to move it.
- Press **Q** or **E** to turn it.
- Press **PgUp** or **PgDn** to make it one block higher or lower.
- Press **Del** to remove it.
- Drag empty ground to move the map.

To replace a selected prefab, select a different prefab in the **Prefabs** tab. The info card shows it as the replacement. Click **Replace**, or click **Back** (Esc) to cancel.

Click **☆** next to a prefab to make it a favourite. **Favourites** in the category list shows your favourites.

### Roads

1. Click on the map to add each point of the road.
2. Press **Enter** to finish the road. Press **Backspace** to remove the last point. Press **Esc** to cancel.

The **Preset** list sets the size of common roads. **Highway** (18 m) and **Country road** (9 m gravel) are the same as the game's roads.

The road snaps as you draw:

- A segment within 5° of north, south, east or west becomes straight.
- Click near the end of a road to continue it. The new road gets the same surface and width.
- Hold **Alt** to draw without snapping.

To remove a road, hold **Shift** and drag over it.

Roads make their flat parts level to whole metres. Prefabs also sit at whole metres, so a POI next to a road meets it with no step.

On a hill, a flat building cannot meet a sloped road level. The editor shows a warning. To prevent this, flatten the ground first:

1. Select the **Flatten** tool and set **Whole metres** on.
2. With a large brush, drag along the road and the ground where the building goes.
3. Put the building on the map.

### Prefabs, street tiles and traders

- A **street tile** snaps to the 150 m grid of a street tile nearby, so the streets align. Hold **Alt** to put a tile anywhere.
- Street tiles cannot overlap. Drop a tile on the middle of another tile to replace it.
- A **POI** or trader snaps to the edges of street tiles near it.
- To replace a POI, drag a new POI over it until the old POI becomes red. Then drop the new POI.
- A prefab that you put next to a road gets the height of the road. The road does not change.

### Towns

The **Town** tool makes a complete town: streets, districts, POIs, traders and flat ground.

1. Drag a rectangle on the map. The rectangle snaps to the world's 150 m street-tile grid. The towns from the Town sites stage use the same grid, so the towns align.
2. Select the town type in the tool options.
3. Change the town if necessary:
   - Drag an edge or a corner of the rectangle to change its size.
   - Click a green link road to remove it. Click the dashed line to add it again.
   - Change the seed to get a different plan.
4. Press **Enter** to build the town. Press **Esc** to cancel.

Hold **Alt** when you start to drag to start the grid at the mouse position. Use this to put a town tightly against a road.

## 6. The Generate panel

The **Generate** tab in the side panel makes the world in stages, the way the game's world generator does.

### The stages

| Stage | What it makes |
|---|---|
| 1 Terrain | Plains, hills and mountains |
| 2 Town sites | Towns and cities, with traders |
| 3 Lakes, rivers, canyons, craters | Water and holes in the ground |
| 4 Highways | Asphalt roads between the towns |
| 5 Wilderness POIs and gravel roads | POIs outside the towns, and gravel roads to them |
| 6 Spawn points | Player spawn points |

Click the name of a stage to open its card. One card is open at a time. A **✓** marks each stage that you completed.

### Use stages and tools together

The stages and the tools change the same world. You can use them in any mix:

- Run a stage, then change its result with a tool.
- Make something with a tool, and a later stage uses it.
- Run a stage again later. The other stages and your tool work stay.

These are some examples:

- Generate the terrain. Then use **Raise** and **Smooth** to change one area.
- Draw a city with the **Town** tool. The **Highways** stage connects it to the other towns.
- Put a trader with the **Trader** tool. The **Spawn points** stage uses it.
- Draw a road with the **Road** tool. The **Wilderness** stage can connect gravel roads to it.
- Open a world that the game generated. Run only the **Spawn points** stage to make new spawns.

### Skip a stage

You do not have to run all six stages. You can skip a stage if you do not want what it makes, or if you make it with the tools.

Some stages use the result of an earlier stage, or of a tool. This table shows what each stage needs.

| Stage | It needs | You can skip it if |
|---|---|---|
| 1 Terrain | A biome map | You want to keep the current ground, or shape it with the tools. |
| 2 Town sites | Nothing | You want no towns, or you draw them with the Town tool. |
| 3 Lakes, rivers, canyons, craters | Nothing | You want no water features, or you add water with the Water fill tool. |
| 4 Highways | Towns in the world, from stage 2, the Town tool or an opened world | You want no highways, or you draw roads with the Road tool. |
| 5 Wilderness POIs and gravel roads | Nothing. Its gravel roads connect to any road. | You want no wilderness POIs, or you put them with the Prefabs tool. |
| 6 Spawn points | Traders in the world, from stage 2, the Trader tool or an opened world | You put spawn points with the Spawn points tool. |

The editor gives a message if a stage cannot run. For example, the Highways stage shows "There are no towns to join: build towns first."

**Do the stages in sequence, 1 to 6.** A later stage uses the result of the earlier stages. For example, the Highways stage connects the towns that are in the world when it runs.

### Seeds

The seed controls the random choices. The same seeds always make the same world.

- **World seed** is at the top of the panel. Click **New** to get a new world seed. The seed of each stage comes from the world seed.
- Each stage also has its own **Seed** and a **Re-roll** button. **Re-roll** runs that stage again with a new seed. It does not change the other stages.

### Presets

A preset is a saved set of Generate settings.

- To use a preset, select it in the **Preset** list and click **Use**. The seeds, the map edges and the tiles that you painted do not change.
- To save your settings, type a name and click **Save**.
- To remove a preset that you saved, select it and click **Delete**.

The editor has eight built-in presets. Each preset changes the game's numbers for your world size.

| Preset | What it changes |
|---|---|
| Game defaults | Nothing. It uses the game's own settings. |
| Big cities | 60% of the game's town count. Each town can have up to two times the street tiles. |
| Frontier | Half the towns, 150% of the roadside stops, many harder wilderness POIs, gravel roads up to 1.5 km. |
| Border towns | The game's counts. Towns can cross biome borders, and each town gets a trader. |
| Easy start | Easier wilderness POIs. At least 20 spawns in all biomes, near roads, within 1.2 km of a trader. |
| Rugged | Plains 2, hills 4, mountains 5. Lakes on mountains. Two times the canyons. |
| Flatlands | Plains 7, hills 3, no mountains. Half the canyons. |
| Waterways | Long rivers of up to 12 stamps. Two times the lakes and rivers. Causeways up to 100 m. |
| Wasteland heavy | Harder wilderness POIs and two times the craters. Spawns in the forest, as the game does. |

### Common buttons

Most stages have these buttons:

- **Generate** removes the result of the last run of the stage and makes a new result. It is one undo step.
- **Remove all** removes everything that the stage made, pinned items too. It puts back the ground. It is one undo step.
- **Game numbers** or **Game settings** puts back the game's values for your world size. The seed does not change.
- **Pinned** keeps an item when you run the stage again. The editor pins each item that you put on the map by hand.
- **Drop** removes one item.

Each stage runs in the background. A progress bar shows. Click **Cancel** to stop the stage.

## 7. The six stages

### Stage 1: Terrain

This stage makes plains, hills and mountains. It divides the map into tiles of 256 m. Each tile gets plains, hills or mountains. Then the editor puts the game's terrain stamps on the tiles.

The stage needs a biome map. If the world has no biome map, paint biomes first.

**Settings:**

- **Plains**, **Hills** and **Mountains** set how much of each type the map gets. The editor compares the three numbers. The game uses 4, 4 and 2.
- **Per biome**: tick a biome to give it its own numbers. A wasteland that you do not tick gets no mountains, as in the game.
- **Edges**: for the north, east, south and west edges, select **Land** (rough raised land), **Water** (ocean) or **None** (no change). The editor makes the edges again only when you generate the whole map.
- **Where**:
  - **Whole map** makes all of the terrain again.
  - **Only an area** makes only the tiles in a rectangle. Click **Select area**, then drag on the map. The new tiles blend into the land around them. Prefabs and roads do not move.
- **Tile types painted by hand**: select a type and click **Paint**. Then paint 256 m tiles on the map. Shift+drag removes painted tiles. **Clear** removes all painted tiles. When you generate, your painted tiles keep their type. The editor fills the other tiles.
- **Show the last run's tile types** colours each tile by the type that the last run gave it.

Click **Generate** to make the terrain.

### Stage 2: Town sites

This stage plans towns and cities, and then builds them. It uses the game's town types and the game's counts for your world size.

**Settings:**

- **The town table** has one row for each town type.
  - **How many**: the number of towns of this type.
  - **Smallest** and **Biggest**: the size of one town, in street tiles. One street tile is 150 m.
  - **About**: the approximate width of a town of that size.
- **One trader per town**:
  - Off: the game's rules. Each biome gets its own trader, 2 to 4 times, in the biggest towns of that biome.
  - On: each town gets one trader.
- **Let towns cross biome borders**:
  - Off: the game's rules. All street tiles of a town are in one biome.
  - On: a town can be on two biomes. Each lot gets POIs and traders for the biome under it.
- **Spacing and biomes**:
  - The distance of the towns from the map edge, in % of the map. The game uses 10%.
  - **Apart**: the minimum number of street tiles between two towns.
  - **Biomes**: the biomes where a town can start. Leave it empty to use the game's list.

**Plan, review, then build:**

1. Click **Plan towns**. The editor selects the town sites and shows each planned town on the map. The world does not change yet.
2. Click **Review on the map** to change the plan:
   - Click a town to select it.
   - Drag a town to move it. The editor plans it again at the new position.
   - Drag a rectangle on empty ground to add a town. **Add as** sets the type of the new town.
   - For the selected town, change its type or its number of street tiles. Click **Re-roll** to plan it again, or **Drop** to remove it.
   - Tick **Pinned** to keep a town when you re-roll the stage. The editor pins each town that you add.
3. Click **Build**. The editor builds all the planned towns: streets, districts, POIs, traders and flat ground. This is one undo step.

**Clear** removes all planned towns, pinned towns too.

All towns use the world's 150 m street-tile grid. The towns align with each other, and street tiles fit between them.

The stage does not make roads between the towns. Stage 4 makes them.

### Stage 3: Lakes, rivers, canyons, craters

This stage presses lakes, rivers, canyons and craters into the ground. It uses the game's counts for your world size.

**Settings:**

- **The feature table** has one row for each feature.
  - **Count**: how many of the feature.
  - **Size min** and **Size max**: the scale of the stamps. A scale of 1 is a stamp of about 700 m.
  - If a feature does not fit, the editor leaves it out.
- **Mountain lakes**: lakes fill at their own ground level, not at the game's fixed 30 m. Lakes can then hold water on high ground and in the mountains.
- **Long rivers**: each river goes downhill, stamp after stamp, until it gets to water or the map edge. A river that stops on dry land ends in a lake.
- **Most stamps in one river**: the maximum length of a long river.

**Place on the map**: select a feature in **Place**, then click empty ground. The editor pins each feature that you place. Click a feature to select it. Then click **Drop** to remove it, or tick **Pinned**. **Drop** removes all of a long river.

A crater or canyon that touches water fills with water, as in the game.

### Stage 4: Highways

This stage connects the towns with asphalt highways. It uses the towns that are in the world when it runs. These can come from stage 2, from the Town tool, or from an opened world.

The game's rules connect each town exit to the nearest of its three nearest towns. Big cities get ring roads. Towns that have no exits get gravel roads.

**Settings:**

- **Route wiggle**: how much the seed bends the routes, in %. Set 0 to get the game's exact routes.
- **Longest causeway over water**: the longest crossing over water, in metres. A highway goes around wider water.
- **Join every town**: after the game's rules, the editor connects a group of towns that has no road to the other towns.
- **Allow a second road between two towns**: the game can connect two towns with two highways side by side. Off: one highway connects two towns.

**Re-roll** gives new routes. **Remove all** removes the roads that this stage made and puts back the ground under them.

### Stage 5: Wilderness POIs and gravel roads

This stage puts POIs outside the towns. It also connects them to the roads with gravel roads.

In each biome, the editor first uses free 150 m cells far from towns. Each cell gets the wilderness POI that fits best. The ground must be dry, flat enough, and in one biome.

**Settings:**

- **Game count**: select the game's **Few**, **Default** or **Many** count. Then set **×%** to multiply that count. 100% is the game's count.
- **Tiers**: move the slider left for easier POIs or right for harder POIs. The middle is the game's mix.
- **Gap from towns and roads**: the minimum distance from towns and roads, in metres.
- **Gravel roads**: a gravel road goes from the road exit of each POI to the nearest road. Any road counts, also a road that you drew.
- **Longest road**: a POI farther from a road than this gets no gravel road.

**Place on the map**: select a POI in **Place**, then click empty ground. The editor pins each POI that you place. Click a POI that this stage placed to select it. **Drop** removes the POI and its gravel road.

### Stage 6: Spawn points

This stage puts player spawn points on the map, the way the game does:

1. Along the gravel roads from stage 5, next to POIs of tier 0 and 1, near a trader of the same biome. The spawns are at least 60 m apart.
2. Then on the free cells nearest to a trader, until there are enough spawns.

The stage uses all traders in the world. These can come from stage 2, from the Trader tool, or from an opened world. If the world has no traders, no spawns go along the gravel roads.

**Settings:**

- **Biomes**: tick the biomes for spawns. The game uses only the forest. Tick none to allow all biomes and all traders.
- **At least**: the minimum number of spawns. Spawns that are already in the world count. The game uses 12.
- **At most**: the maximum number of spawns. Set 0 for no limit, as in the game.
- **Road spawns within this of a trader**: the maximum distance from a trader, in metres. The game uses 900.

**Edit on the map**: add, move, turn and remove spawns by hand. A spawn that you add or move stays when you run the stage again.

## 8. Example workflows

### A complete generated world

1. Click **New World**. Select the size and a biome layout.
2. In the Generate panel, select a preset, or keep **Game defaults**.
3. Run stage 1. Change the terrain with the terrain tools if necessary.
4. Run stage 2: click **Plan towns**, review the towns, then click **Build**.
5. Run stages 3, 4, 5 and 6.
6. Press **F7** to validate. Then press **Ctrl+E** to export.

### A world with your own city

1. Run stage 1 to make the terrain.
2. Draw your city with the **Town** tool and press **Enter**.
3. Run stage 2. Set **How many** for the city type to 0 if you want no other cities.
4. Run stage 4. The highways connect your city to the other towns.
5. Run stages 5 and 6.

### New spawns for a world that you have

1. Open the world folder.
2. Open stage 6 and set the settings.
3. Click **Generate**.
4. Save the world.

## 9. Validate and export the world

### Validate

Press **F7**, or click **Validate**. The **Issues** tab lists the problems that the editor finds. For example, it lists a POI that does not meet its road level. Double-click an issue to go to it on the map.

### Export

1. Press **Ctrl+E**, or click **Export**.
2. Select a folder. To play the world, select the game's `GeneratedWorlds` folder.
3. The editor validates the world, then writes the world folder.

The world then shows under **New Game** in the game.

The game keeps the chunks that it already generated in a saved game. To see all of your changes, start a new game.

## 10. Keyboard shortcuts

Press **F1** in the editor to see this list.

| Key | Action |
|---|---|
| V, R, L, F, S, N, T | Select, Raise, Lower, Flatten, Smooth, Roughen, Stamp |
| B, D, W | Paint biome, Paint radiation, Water fill |
| O, I, A, G, H | Road, Prefabs, Trader, Town, Spawn points |
| [ and ] | Smaller or larger brush |
| Q and E | Turn the square brush, stamp, prefab or spawn |
| Ctrl+click | Take the height (Flatten) or water level (Water fill) from the map |
| Shift+drag | Remove roads or radiation |
| Alt+click | Put a road point, POI, trader or street tile without snapping |
| Alt+drag | Draw a town off the shared street-tile grid |
| Enter, Esc, Backspace | Finish, cancel, or remove the last point (roads, towns) |
| Del, PgUp, PgDn | Remove the selected prefab, or move it one block higher or lower |
| Wheel, + and -, Home | Zoom, see the whole map |
| Right drag | Move the map with any tool |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+E | New, open, save, save project as, export |
| F7, F1 | Validate, show shortcuts |

## 11. Words in this manual

| Word | Meaning |
|---|---|
| **Biome** | An area of one type: forest, burnt forest, desert, snow or wasteland. |
| **POI** | A point of interest: a building or other place that a player can visit. |
| **Prefab** | An object from the game that you can put on the map: a POI, a street tile or a part. |
| **Project** | A `.7dmap` file. It keeps the world and all of your settings. |
| **Seed** | A number that controls the random choices. The same seed gives the same result. |
| **Stage** | One step in the Generate panel. |
| **Stamp** | A height map from the game that the editor puts on the ground. |
| **Street tile** | A 150 m square of town streets. A town consists of street tiles. |
| **Tier** | How difficult a POI is, from 0 (easy) to 5 (difficult). |
| **World folder** | The folder of files that the game loads. |

## 12. Report a bug

Open an [issue](https://github.com/jayfitt-droid/7d2d-map-editor/issues) on GitHub. Tell us:

- The world size.
- The stage or tool that you used.
- What happened, and what you expected.

A screenshot helps. If the editor crashed, attach `crash.log` from `%APPDATA%\7D2DMapEditor`.

*This is a fan-made tool. It is not made by or affiliated with The Fun Pimps.*
