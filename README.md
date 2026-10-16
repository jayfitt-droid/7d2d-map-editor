# 7D2D Map Editor

A Windows editor for 7 Days to Die worlds. It reads and writes the game's world folder directly (`dtm.raw`, `biomes.png`, `splat3.png`, `splat4.png`, `radiation.png`, `prefabs.xml`, `spawnpoints.xml`, `map_info.xml`, `main.ttw`). The game processes the folder itself on first load, so no mod is needed.

Game assets are read from your own install and never shipped with the editor.

See [docs/RWG-Research.md](docs/RWG-Research.md) for how the game's generator and world format work.

## Layout

| Project | Purpose |
|---|---|
| `src/MapEditor.Core` | World model, file I/O (own PNG codec, no alpha premultiply), game-install discovery |
| `src/MapEditor.App` | WPF editor |
| `src/MapEditor.Cli` | `mapeditor-cli info`, `roundtrip` and `installs` for testing |
| `tests/MapEditor.Core.Tests` | xUnit tests |

## Build

```
dotnet build MapEditor.slnx -c Release
dotnet test MapEditor.slnx -c Release
```

Requires the .NET 10 SDK (pinned in `global.json`). Set `MAPEDITOR_TEST_WORLD` to a world folder to also round-trip a real world in the tests.
