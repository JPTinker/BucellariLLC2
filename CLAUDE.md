# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 6 (editor `6000.5.8f1`) turn-based hex-grid tactics / settlement-management game (URP, Input System, UI Toolkit). Product name `BucellariLLC2`. There is no README, no test assemblies (`com.unity.test-framework` is installed but no tests exist), and no custom `.asmdef` — all gameplay code is in `Assembly-CSharp` under `Assets/Scripts/`.

There is no CLI build/lint/test workflow. Develop by opening the project in the Unity Editor (or Rider/VS via the generated `.csproj`/`.slnx`); scripts compile on editor focus. Batch build, if needed: `Unity.exe -batchmode -quit -projectPath . -buildTarget <target> ...`. `testing/` is a checked-in Linux player build output and `Data/`, `Library/`, `Temp/`, `Logs/` are generated — don't edit them.

## Scene flow

Enabled build scenes: `TitleScreen` → `DecisionPhase` → `BattlePhase` → back to `DecisionPhase` (`SampleScene` is disabled).

- **TitleScreen** (`Global/TitleScreenController`): New Game / Continue. Continue sets `SaveSystem.LoadOnNextStart = true` and loads `DecisionPhase`.
- **DecisionPhase** (planning): one `UIDocument` driven by `PhaseShellController`, which swaps tab content (Roster, Decision, Map, Store-placeholder) into a content slot. Tab controllers (`PlanningPhaseController` = roster tab, `DecisionPhaseController`, `CampaignMapController`) are plain components with no `UIDocument` of their own; templates and controller refs are assigned in the Inspector. `PhaseTabSwitcher` is a legacy predecessor of `PhaseShellController`.
- **BattlePhase** (`InCombat/`): hex-grid combat.

## Architecture

**`GameStateManager`** (`GameManager/`) is the central `DontDestroyOnLoad` singleton and owns all persistent campaign state: `Resources` (gold, stage index), `Settlement` resources (food/materials/morale), `FullRoster` and `ActiveTeam` (`Unit` records built from `UnitData` ScriptableObject archetypes), draft options, the per-cycle allocation/forecast math (`TryAdjustAllocation`, `ComputeForecast`, `ExecuteCycle`), and scene transitions (`LoadCombatMap`, `ReturnToTeamManager`, `ProcessExtraction`, `CheckForCombatEnd`). UI code should call into it rather than duplicating the math. Because it persists across scenes, it reacts to `SceneManager.sceneLoaded` (e.g. resolving morale after battle when `DecisionPhase` loads). Scene names are hardcoded strings (note `ReturnToTeamManager` defaults to `"TeamManager"`, which is not a build scene — `CheckForCombatEnd` passes `"DecisionPhase"`).

**Persistent vs. in-battle units:** `Unit` (`DataStructures/UnitData.cs`) is the persistent roster record (CurrentHP, rarity, level); `UnitInstance` (`InCombat/UnitInstance.cs`) is the battle-scene MonoBehaviour with `PersistentUnit` back-reference. Battle damage is written back to `Unit.CurrentHP` on extraction; only Rest heals. `UnitRarityTable` holds rarity tiers.

**Save system** (`SaveSystem.cs`): single-slot JSON (`JsonUtility`) at `Application.persistentDataPath/savegame.json`. `GameStateManager` builds/applies `SaveData` itself; `SaveSystem` only does file IO and the `LoadOnNextStart` handoff consumed in `GameStateManager.Awake()`. Saves happen only on the Decision screen, never mid-battle. Bump `SaveSystem.CurrentVersion` when changing `SaveData`/`UnitSaveData`.

**Combat** (`InCombat/`):
- `MapManager` procedurally generates the hex map (Perlin noise → ordered `TerrainBand`s → `TerrainType`), spawns `HexTile` prefabs, wires neighbor lists, places the player team, enemy clusters, exfil tile and villagers.
- `HexCoordinates` / `HexPathfinder`: pointy-top **odd-r offset** coords (`HexTile.gridPosition`, x = column, y = row); neighbor tables differ by row parity.
- `CombatManager` (scene singleton): player selection, two-click move confirmation, per-unit actions (Fortify / Extract / Scout via `CombatPhaseUIController.OnActionRequested`), rounds, reinforcements, win/loss events. Enemy and villager turns are run by `EnemyAIController` / `VillagerAIController` (auto-added to the CombatManager object if missing; `EnemyBattlefieldState` supports AI planning).
- Rescue mechanic: a player unit carries up to 2 rescued villagers (`UnitInstance.rescuedUnitData`) and extracts them at the exfil zone; villagers can also self-evacuate via `GameStateManager.ProcessVillagerEvacuation`.

**UI**: all UI is UI Toolkit. UXML in `Assets/UI/UXML/`, styles alongside, shared `Assets/UI Toolkit/PanelSettings.asset`. Controllers query `rootVisualElement` (do it in `Start()`/`OnEnable` once the document is ready) and use `NotificationManager` for toasts.

Many tuning values live in `[SerializeField]`/Inspector fields (e.g. `CycleBalanceConfig`, `AvailablePlayerArchetypes`, reinforcement thresholds, tile prefab database) — they are set in scenes/prefabs, not in code, so check the scene/asset before assuming a default.

## Conventions

Version tags are the commit messages (`V 0.4.7`, etc.); work is committed as whole-project snapshots.
