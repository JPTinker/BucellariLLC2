# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 6 (editor `6000.5.8f1`) hex-grid tactics / settlement-escape game (URP, Input System, UI Toolkit). Product name `BucellariLLC2`. There is no README, no test assemblies (`com.unity.test-framework` is installed but no tests exist), and no custom `.asmdef` — all gameplay code is in `Assembly-CSharp` under `Assets/Scripts/`.

**This branch (`Build2`) is a different game from `main`**: the campaign/planning layer was deleted and the battle phase is the only phase. If the direction is abandoned the plan is to revert to the earlier commit, so don't keep compatibility shims for removed code.

There is no CLI build/test workflow. Develop by opening the project in the Unity Editor (or Rider/VS via the generated `.csproj`/`.slnx`); scripts compile on editor focus and gameplay can only be verified by playing in the editor. `testing/` is a checked-in Linux player build output and `Data/`, `Library/`, `Temp/`, `Logs/` are generated — don't edit them.

## Game rules (Build2 direction)

Start with 2 heroes. Build a Town Center (TC), then Church (the **only** source of healing) and Barracks (drafts a hero every 3 rounds; each Barracks raises the hero cap by 2 above the starting 2, max 8). Win by getting 10 **villagers** across (only villagers are ever extracted — heroes never leave the map; Extract is renamed "Drop Off" and needs a carried villager next to an extraction point). Lose when morale (starts at 50) hits 0: hero death, villager death, and an extraction point expiring with unused slots cost morale. Extraction points are on water tiles, appear only when the map grows, take 2 villagers each, then close. Score = rounds, kills, friendly losses. Villagers are player-team units produced by the TC (every 3 rounds, max 5); heroes may still carry up to 2 to an extraction point. The tiles around a TC are permanently unbuildable.

Implementation status: scene flow, morale/win/lose/score and the build menu exist. Still to build: TC villager spawning and Food/Materials role assignment, church healing, barracks hero offers, random water-tile extraction with capacity/expiry, player-team villagers (neutral stray villagers and the fixed boat at (0,0) are still the old code). The working plan is in `~/.claude/plans/silly-imagining-barto.md`.

## Scene flow

Enabled build scenes: `TitleScreen` → `BattlePhase` → (on a win only, via `GameOverPanel`) `AfterAction`, the victory scene with cheering heroes and confetti (`AfterActionController`, `UIAnim`; the loss stays a card on the battle scene). `TitleScreenController` loads `GameStateManager.GameSceneName`. Saving: the HUD **Save** button (`CombatManager.SaveGame`, player phase only) writes one JSON slot (`SaveSystem`, `persistentDataPath/battlesave.json`). Each owner writes/applies its slice: `GameStateManager.WriteSave/ApplySave`, `MapManager.WriteSave/RestoreFromSave` (`MapManagerSave.cs`: tiles by prefab index, buildings, every unit), `CombatManager.WriteSave/ApplySave`. Title **Continue** sets `SaveSystem.LoadOnNextStart`; `MapManager.Awake` then rebuilds the battle instead of generating one. New persistent state must be added to `BattleSaveData`. The Decision/Voyage/AfterAction scenes and code are gone; leftover UXML/USS for them in `Assets/UI/` and the Shipwright/BattleLength research assets are dead and can be deleted.

## Architecture

**`GameStateManager`** (`GameManager/`) is a `DontDestroyOnLoad` singleton that comes from the `FullGameManager` prefab inside `BattlePhase` (the title scene has none). It owns the run: `Settlement` (Food/Materials/Morale), `FullRoster`/`ActiveTeam` (`Unit` records built from `UnitData` archetypes in `AvailablePlayerArchetypes`), run counters, `Balance` (`CycleBalanceConfig`: morale penalties, win target, score weights), `TrySpend`/`AddFood`/`AddMaterials`, `AdjustMorale`, and `EndGame(won)`, which raises `OnGameOver` with a `GameResult`. Anything that changes the economy should go through it and raise `OnSettlementChanged` (the HUD repaints on that event). `RestartGame()`/`ReturnToTitle()` destroy the manager before loading a scene so the next scene boots a clean one — there is no manual reset code, so keep run state on this object.

**Persistent vs. in-battle units:** `Unit` (`DataStructures/UnitData.cs`) is the roster record (HP, rarity, level); `UnitInstance` (`InCombat/UnitInstance.cs`) is the scene MonoBehaviour with a `PersistentUnit` back-reference. `UnitFaction` is `Player`, `Enemy`, `Neutral`, `Villager`, `Structure`; walls and buildings are `Structure` `UnitInstance`s, so enemies attack them through the normal combat pipeline, and `CombatManager.Track` ignores them for turn/death bookkeeping.

**Combat** (`InCombat/`):
- `MapManager` procedurally generates the hex map (Perlin noise → `TerrainBand`s → `TerrainType`), spawns tiles, places the team, enemy clusters and villagers, and owns building: `BuildingSpec` (`BuildingSpec.cs`, tunable in the Inspector as `buildingSpecs`), `GetBuildableTiles`, `CanBuild`, `BuildStructure`, and the standing-TC prerequisite. `HexTile.IsReserved` marks TC-adjacent tiles; farm/lumber/fortification/build checks all honor it.
- `HexCoordinates` / `HexPathfinder`: pointy-top **odd-r offset** coords (`HexTile.gridPosition`, x = column, y = row); neighbor tables differ by row parity.
- `CombatManager` (scene singleton): player selection, two-click move confirmation, per-unit actions, the round loop (player phase → `EnemyAIController` → `VillagerAIController` → worker AI → `FinishVillagerTurn`, which spawns reinforcements, advances `currentRound`, fires `OnRoundAdvanced`, and refills actions), reinforcements (each wave also calls `MapManager.ExpandMap`). The game ends only via `GameStateManager.EndGame` (morale ≤ 0 or 10 villagers escaped); `OnGameOver` locks input.
- Build flow: HUD **Build** → `CombatManager.EnterBuildMode` highlights adjacent buildable tiles and calls `CombatPhaseUIController.ShowBuildMenu(true)` → the bottom bar swaps to the menu → `OnBuildingChosen` sets `pendingBuilding` → clicking a highlighted tile spends Food/Materials and calls `BuildStructure`. Selecting any unit (or clearing selection) closes the menu.
- Rescue: a hero carries up to 2 villagers (`UnitInstance.rescuedUnitData`); `GameStateManager.ProcessExtraction` counts and clears them (each counts toward the 10). Villagers can also self-evacuate via `ProcessVillagerEvacuation`.
- Research (`ResearchManager`, `ResearchData`, `GameManager/ResearchController.cs`): kills complete projects; effects are derived on read via `ResearchManager.Current` (falls back to base values with no `GameStateManager`).

**UI**: all UI is UI Toolkit. UXML in `Assets/UI/UXML/`, styles in `Assets/UI/USS/`, shared `Assets/UI Toolkit/PanelSettings.asset`. `CombatPhaseUIController` drives the HUD and builds some elements in code (research HUD, build menu, worker-escape button); `GameOverPanel` is a code-built Win/Lose overlay added by that controller. Controllers query `rootVisualElement` in `Start()`/`OnEnable`; use `NotificationManager` for toasts.

Many tuning values are `[SerializeField]`/Inspector fields set in scenes/prefabs (`CycleBalanceConfig`, `AvailablePlayerArchetypes`, reinforcement thresholds, `buildingSpecs`, tile prefab database). Check the scene/asset before assuming a default; fields added in code with initializers take the code default in already-serialized scenes.

## Conventions

Version tags are the commit messages (`V 0.7.2 - short description`). Build2 work is committed per milestone and pushed only to `origin/Build2`, never `main`.

## Linting / compile check

`.editorconfig` defines the rules (first-party C# only: `Assets/Scripts`, `Assets/Editor`). Run `./lint.sh` (needs the .NET SDK and the Unity-generated `Assembly-CSharp*.csproj`): it builds both projects and runs `dotnet format whitespace/style --verify-no-changes`. Fix with `dotnet format whitespace|style Assembly-CSharp.csproj --no-restore --include <files>`. Gotchas:
- The `.csproj` files are gitignored and Unity regenerates them. After adding, moving or deleting `.cs` files outside the editor, the csproj is stale: edit its `<Compile Include=...>` lines (or open Unity) before `dotnet build Assembly-CSharp.csproj --no-restore -v q`. If the build fails with NETSDK1004 (missing `project.assets.json`), run `dotnet restore Assembly-CSharp.csproj` first.
- `EnemyAIController.cs` and `Assets/Editor/ResearchAssetSeeder.cs` have pre-existing lint findings.
- Files created outside Unity have no `.meta` until the editor imports them; commit the generated `.meta` afterwards.
