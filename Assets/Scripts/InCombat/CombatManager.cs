using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Handles selecting player units and their two-click movement confirmation.</summary>
public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }
    public event Action<int> OnRoundAdvanced;


    [SerializeField] private EnemyAIController enemyAI;
    [SerializeField] private VillagerAIController villagerAI;

    [Header("Enemy Reinforcements")]
    [Tooltip("When living enemies drop below this count, reinforcements are spawned.")]
    [SerializeField] private float enemyTurnDelay = 2f;
    [SerializeField] private int reinforcementThreshold = 10;
    [Tooltip("Reinforcements top the enemy count back up to this total.")]
    [SerializeField] private int reinforcementTargetCount = 8;
    [Tooltip("Extra enemies added to the target count for every time the map has grown.")]
    [SerializeField] private int enemiesPerExpansion = 1;
    [Tooltip("Extra enemies (added to both the reinforcement threshold and target) for every living player unit, so more units in the field means faster, bigger waves.")]
    [SerializeField] private int enemiesPerPlayerUnit = 1;
    [Tooltip("Reinforcement waves can arrive at most once per this many rounds.")]
    [SerializeField] private int minRoundsBetweenWaves = 7;
    private int lastWaveRound;
    // 0 = opening wave; incremented on each reinforcement wave to scale new enemies.
    private int enemyWaveLevel;
    private int mapExpansions;
    private const int EvacuationPointExpansionInterval = 3;
    private readonly List<UnitInstance> playerUnits = new List<UnitInstance>();
    private readonly List<UnitInstance> enemyUnits = new List<UnitInstance>();
    private readonly List<UnitInstance> villagerUnits = new List<UnitInstance>();
    // Player-owned worker villagers: kept apart from villagerUnits (strays to rescue) and from playerUnits (win/loss).
    private readonly List<UnitInstance> workerUnits = new List<UnitInstance>();
    private WorkerVillagerAIController workerAI;
    public bool HasActiveWorkers
    {
        get
        {
            workerUnits.RemoveAll(w => w == null);
            return workerUnits.Count > 0;
        }
    }
    private readonly List<HexTile> highlightedTiles = new List<HexTile>();
    private readonly HashSet<HexTile> reachableTiles = new HashSet<HexTile>();
    private readonly List<HexTile> previewPath = new List<HexTile>();
    private UnitInstance selectedUnit;
    private HexTile pendingDestination;
    private bool levelEnded;
    private bool buildMode;
    private BuildingType? pendingBuilding;
    private bool nonPlayerTurnInProgress;
    private bool draftOpen;
    private int barracksRounds;
    [Tooltip("With a Barracks standing, the player is offered a hero draft every this many rounds.")]
    [SerializeField] private int draftInterval = 3;
    [SerializeField] private int draftOptionCount = 3;
    public int currentRound { get; private set; } = 1;

    public CombatPhaseUIController combatUIManager;

    public NotificationManager notificationManager;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (enemyAI == null) enemyAI = FindAnyObjectByType<EnemyAIController>();
        if (enemyAI == null) enemyAI = gameObject.AddComponent<EnemyAIController>();
        if (villagerAI == null) villagerAI = FindAnyObjectByType<VillagerAIController>();
        if (villagerAI == null) villagerAI = gameObject.AddComponent<VillagerAIController>();
        workerAI = FindAnyObjectByType<WorkerVillagerAIController>();
        if (workerAI == null) workerAI = gameObject.AddComponent<WorkerVillagerAIController>();
    }

    private void OnEnable()
    {
        GameStateManager.OnGameOver += HandleGameOver;
        if (combatUIManager != null)
        {
            combatUIManager.OnActionRequested += HandleActionRequested;
            combatUIManager.OnBuildingChosen += HandleBuildingChosen;
            combatUIManager.OnBuildCancelled += HandleBuildCancelled;
        }
    }

    private void OnDisable()
    {
        GameStateManager.OnGameOver -= HandleGameOver;
        if (combatUIManager != null)
        {
            combatUIManager.OnActionRequested -= HandleActionRequested;
            combatUIManager.OnBuildingChosen -= HandleBuildingChosen;
            combatUIManager.OnBuildCancelled -= HandleBuildCancelled;
        }
    }

    private void Start()
    {
        if (SaveSystem.Loaded != null)
        {
            ApplySave(SaveSystem.Loaded);
            SaveSystem.Loaded = null;
        }
        TrackAll(FindObjectsByType<UnitInstance>());
        if (GameStateManager.Instance != null) GameStateManager.Instance.RoundsElapsed = currentRound;
    }

    // --- Save / load ---

    /// <summary>Saving is only allowed between player actions: nothing animating, no enemy phase, no open draft.</summary>
    public bool CanSave =>
        !levelEnded && !nonPlayerTurnInProgress && !draftOpen && !playerUnits.Exists(u => u != null && u.IsMoving);

    public void WriteSave(BattleSaveData data)
    {
        data.Round = currentRound;
        data.LastWaveRound = lastWaveRound;
        data.EnemyWaveLevel = enemyWaveLevel;
        data.MapExpansions = mapExpansions;
        data.BarracksRounds = barracksRounds;
    }

    private void ApplySave(BattleSaveData data)
    {
        currentRound = Mathf.Max(1, data.Round);
        lastWaveRound = data.LastWaveRound;
        enemyWaveLevel = data.EnemyWaveLevel;
        mapExpansions = data.MapExpansions;
        barracksRounds = data.BarracksRounds;
        combatUIManager?.SetRound(currentRound);
    }

    /// <summary>Called by the HUD's Save button.</summary>
    public void SaveGame()
    {
        if (!CanSave)
        {
            notificationManager?.ShowNotification("Can't save right now - wait for your turn");
            return;
        }

        bool saved = SaveSystem.SaveCurrentBattle();
        notificationManager?.ShowNotification(saved ? "Game saved" : "Save failed - see the console");
    }

    // The run is over: freeze input so nothing moves behind the result panel.
    private void HandleGameOver(GameStateManager.GameResult result)
    {
        levelEnded = true;
        ClearSelection();
    }

    private void HandleActionRequested(CombatPhaseUIController.CombatAction action, UnitInstance unit)
    {
        if (unit == null)
        {
            Debug.LogWarning($"CombatManager: {action} clicked without a selected unit.");
            return;
        }

        switch (action)
        {
            case CombatPhaseUIController.CombatAction.Fortify:
                HandleFortifyClicked(unit);
                if (unit.IsFortified) ClearSelection(); // action spent: drop stale move/attack highlights
                break;
            case CombatPhaseUIController.CombatAction.Extract:
                HandleExtractClicked(unit);
                ClearSelection();
                break;
            case CombatPhaseUIController.CombatAction.Scout:
                HandleScoutClicked(unit);
                ClearSelection();
                break;
            case CombatPhaseUIController.CombatAction.Build:
                EnterBuildMode(unit);
                break;
        }
    }

    // ---------------------------------------------------------------
    // Building: press Build, pick a building from the bottom bar's build menu,
    // then click one of the highlighted adjacent tiles. Clicking anywhere else
    // (or selecting another unit, or Back) cancels.
    // ---------------------------------------------------------------

    private void EnterBuildMode(UnitInstance unit)
    {
        if (MapManager.Instance == null) return; // building is a free action

        List<HexTile> buildable = MapManager.Instance.GetBuildableTiles(unit);
        if (buildable.Count == 0)
        {
            notificationManager?.ShowNotification("No free adjacent tile to build on");
            return;
        }

        // Swap the move/attack highlights for build highlights on the same unit.
        foreach (HexTile tile in highlightedTiles) if (tile != null) tile.ClearHighlight();
        highlightedTiles.Clear();
        reachableTiles.Clear();
        previewPath.Clear();
        pendingDestination = null;

        selectedUnit = unit;
        buildMode = true;
        pendingBuilding = null;
        foreach (HexTile tile in buildable)
        {
            highlightedTiles.Add(tile);
            tile.Highlight(TileHighlightType.Build);
        }

        combatUIManager?.ShowBuildMenu(true);
    }

    private void HandleBuildingChosen(BuildingType type, UnitInstance unit)
    {
        MapManager map = MapManager.Instance;
        if (!buildMode || map == null) return;

        if (!map.CanBuild(type, out string reason))
        {
            notificationManager?.ShowNotification(reason);
            return;
        }

        pendingBuilding = type;
        notificationManager?.ShowNotification($"Pick a highlighted tile for the {map.GetBuildingSpec(type).DisplayName}");
    }

    // Back in the build menu: drop the build highlights and return to the unit's normal move/attack view.
    private void HandleBuildCancelled(UnitInstance unit)
    {
        if (!buildMode) return;
        UnitInstance builder = unit != null ? unit : selectedUnit;
        ClearSelection();
        if (builder == null || builder.IsDead) return;
        SelectUnit(builder);
        combatUIManager?.SelectUnit(builder);
    }

    private void HandleBuildClick(HexTile tile)
    {
        // Off the highlighted tiles: cancel building.
        if (!highlightedTiles.Contains(tile)) { ClearSelection(); return; }

        // On a build tile before choosing what to build: keep the menu open.
        if (pendingBuilding == null)
        {
            notificationManager?.ShowNotification("Choose a building first");
            return;
        }

        UnitInstance builder = selectedUnit;
        MapManager map = MapManager.Instance;
        GameStateManager gsm = GameStateManager.Instance;
        BuildingType type = pendingBuilding.Value;

        bool built = false;
        string reason = null;
        if (builder != null && map != null && gsm != null && map.CanBuild(type, out reason))
        {
            BuildingSpec spec = map.GetBuildingSpec(type);
            if (gsm.TrySpend(spec.FoodCost, spec.MaterialCost))
            {
                built = map.BuildStructure(type, tile) != null;
                if (!built) // refund if placement failed
                {
                    gsm.AddFood(spec.FoodCost);
                    gsm.AddMaterials(spec.MaterialCost);
                }
            }
        }
        else if (reason != null)
        {
            notificationManager?.ShowNotification(reason);
        }

        // Building costs no action; just drop the selection.
        if (built) builder.AddExperience(UnitInstance.BuildExperience); // free action: no TakeAction
        combatUIManager?.RefreshSettlementLedger();
        ClearSelection();
    }

    private void HandleFortifyClicked(UnitInstance unit)
    {
        MapManager map = MapManager.Instance;
        GameStateManager gsm = GameStateManager.Instance;
        if (!unit.CanFortifyFurther)
        {
            notificationManager?.ShowNotification("Already at max fortification");
            return;
        }

        int cost = map != null ? map.fortifyMaterialCost : 0;
        if (gsm == null || !gsm.TrySpendMaterials(cost))
        {
            notificationManager?.ShowNotification($"Not enough materials (need {cost})");
            return;
        }

        unit.onFortify();
        Debug.Log($"CombatManager: Fortify clicked for {unit.unitName} (level {unit.FortifyLevel}).");
    }

    // Heroes no longer leave the map: Extract hands the villagers they carry over to the extraction point.
    private void HandleExtractClicked(UnitInstance unit)
    {
        if (unit == null || GameStateManager.Instance == null) return;

        MapManager map = MapManager.Instance;
        int slots = map != null ? map.GetEvacuationSlots(unit.currentTile) : 0;
        if (slots <= 0) return;

        unit.Say(UnitSpeechTrigger.Extract);
        int saved = GameStateManager.Instance.ProcessExtraction(unit, slots);
        map.UseEvacuationSlots(unit.currentTile, saved);
        if (saved > 0) notificationManager?.ShowNotification($"{saved} villager{(saved == 1 ? "" : "s")} evacuated");
        TakeAction(unit);
    }

    private static void HandleScoutClicked(UnitInstance unit)
    {
        unit.onScout();
        Debug.Log($"CombatManager: Scout clicked for {unit.unitName}.");
    }

    /// <summary>Called by HexTile when the player clicks it.</summary>
    public void SelectedTile(HexTile tile)
    {
        //Debug.Log($"CombatManager: SelectedTile called with {tile?.name ?? "null"}");
        if (tile == null || levelEnded || nonPlayerTurnInProgress || draftOpen) return;
        if (buildMode) { HandleBuildClick(tile); return; }
        // A hero standing inside a structure is picked first; clicking it again falls through to the building's own panel.
        UnitInstance garrisoned = tile.garrison;
        if (garrisoned != null && garrisoned.Faction == UnitFaction.Player && garrisoned != selectedUnit &&
            (selectedUnit == null || !highlightedTiles.Contains(tile)))
        {
            SelectUnit(garrisoned);
            combatUIManager.SelectUnit(garrisoned);
            return;
        }
        if (tile.occupyingUnit != null && MapManager.Instance != null &&
            MapManager.Instance.GetStructures(BuildingType.TownCenter).Contains(tile.occupyingUnit))
        {
            ClearSelection();
            combatUIManager.ShowTownCenterPanel(true);
            return;
        }
        if (tile.occupyingUnit != null && MapManager.Instance != null &&
            MapManager.Instance.GetStructures(BuildingType.Library).Contains(tile.occupyingUnit))
        {
            ClearSelection();
            combatUIManager.ShowLibraryPanel(true);
            return;
        }
        if (garrisoned != null && garrisoned == selectedUnit) { ClearSelection(); return; }
        if (selectedUnit == null)
        {
            if (tile.occupyingUnit != null && (tile.occupyingUnit.Faction == UnitFaction.Player))
            {
                SelectUnit(tile.occupyingUnit);
                combatUIManager.SelectUnit(tile.occupyingUnit);
            }
            return;
        }
        else
        {
            if (tile.occupyingUnit != null &&
                tile.occupyingUnit.Faction == UnitFaction.Player)
            {
                if (tile.occupyingUnit == selectedUnit) { ClearSelection(); return; }
                SelectUnit(tile.occupyingUnit);
                combatUIManager.SelectUnit(tile.occupyingUnit);
                return;
            }
            if (!highlightedTiles.Contains(tile)) { ClearSelection(); return; }
            if (tile.occupyingUnit != null &&
                tile.occupyingUnit.Faction == UnitFaction.Villager)
            {
                if (pendingDestination != tile)
                {
                    pendingDestination = tile;
                    return;
                }

                if (selectedUnit.Rescue(tile.occupyingUnit))
                    TakeAction(selectedUnit);
                ClearSelection();
                return;
            }
            if (tile.occupyingUnit != null && tile.occupyingUnit.Faction == UnitFaction.Enemy)
            {
                Debug.Log($"CombatManager: Attempting to attack enemy unit on {tile.name}");
                // Attack logic can be implemented here
                if (pendingDestination != tile)
                {
                    Debug.Log($"Previewing attack on {tile.name}");
                    pendingDestination = tile;
                    tile.Highlight(TileHighlightType.Path);
                }
                else
                {
                    Debug.Log($"Attacking enemy unit on {tile.name}");
                    selectedUnit.Attack(tile.occupyingUnit);
                    TakeAction(selectedUnit);
                    ClearSelection();
                }
                return;
            }

            // First click previews a route. Clicking the same destination commits it.
            if (pendingDestination != tile) { PreviewDestination(tile); return; }
            if (selectedUnit.MoveTo(tile)) TakeAction(selectedUnit);
            ClearSelection();
        }

    }
    public void TakeAction(UnitInstance selectedUnit)
    {
        selectedUnit.actionsRemaining = Mathf.Max(0, selectedUnit.actionsRemaining - 1);
        selectedUnit.NotifyStatsChanged();
        if (selectedUnit.actionsRemaining <= 0)
        {
            // check to see if turn is over. 
            CheckTurnEnd();
        }
    }
    /// <summary>Drops the current selection (and its highlights) so no unit is selected.</summary>
    public void DeselectUnit()
    {
        if (levelEnded || nonPlayerTurnInProgress) return;
        ClearSelection();
    }

    private void Update()
    {
        // Escape clears the selection.
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && selectedUnit != null)
            DeselectUnit();
    }

    // Keeps existing prefab/event bindings working.
    public void selectedTile(HexTile tile) => SelectedTile(tile);

    public void SelectUnit(UnitInstance unit)
    {
        if (unit == null || unit.Faction == UnitFaction.Enemy || unit.IsDead || unit.currentTile == null) return;
        ClearSelection();
        selectedUnit = unit;
        if (unit.actionsRemaining <= 0) return;

        foreach (HexTile tile in HexPathfinder.GetReachableTiles(unit.currentTile, unit.movementRange, unit))
        {
            if (!tile.CanEnter(unit)) continue;
            // GetReachableTiles walks through occupied tiles, but MoveTo cannot - only highlight tiles with a real route.
            if (HexPathfinder.FindPath(unit.currentTile, tile, unit.movementRange, unit) == null) continue;
            reachableTiles.Add(tile);
            highlightedTiles.Add(tile);
            tile.Highlight(TileHighlightType.Movement);
        }
        foreach (HexTile tile in HexPathfinder.GetAttackableTiles(unit.currentTile, unit.attackRange))
        {
            if (tile.occupyingUnit == null) continue;
            if (tile.occupyingUnit.Faction == UnitFaction.Villager)
            {
                if (HasRescueCapacity(unit))
                {
                    highlightedTiles.Add(tile);
                    tile.Highlight(TileHighlightType.Rescue);
                }
                continue;
            }
            if (tile.occupyingUnit.Faction != UnitFaction.Enemy) continue;
            highlightedTiles.Add(tile);
            tile.Highlight(TileHighlightType.Attack);
        }
    }

    private static bool HasRescueCapacity(UnitInstance unit)
    {
        if (unit.rescuedUnitData == null) return false;
        foreach (UnitData rescuedUnit in unit.rescuedUnitData)
        {
            if (rescuedUnit == null) return true;
        }
        return false;
    }

    public void CheckTurnEnd()
    {
        // Check if all player units have no actions remaining
        bool allPlayerUnitsDone = true;
        foreach (UnitInstance unit in playerUnits)
        {
            if (unit.actionsRemaining > 0)
            {
                allPlayerUnitsDone = false;
                break;
            }
        }

        if (allPlayerUnitsDone)
        {
            Debug.Log("All player units have completed their actions. Ending turn.");
            // Trigger end of turn logic here
            EndTurn();
        }
    }

    private void EndTurn()
    {
        if (nonPlayerTurnInProgress) return;

        Debug.Log("Ending player's turn and starting enemy's turn.");
        nonPlayerTurnInProgress = true;
        ClearSelection();
        StartCoroutine(BeginEnemyTurnWhenUnitsSettle());
    }

    // The last player action may still be animating; enemies must plan against
    // where units actually are, not the tile they just left.
    private IEnumerator BeginEnemyTurnWhenUnitsSettle()
    {
        float waited = 0f;
        while (waited < 5f && playerUnits.Exists(u => u != null && u.IsMoving))
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (enemyTurnDelay > 0f)
            yield return new WaitForSeconds(enemyTurnDelay);

        if (enemyAI != null)
        {
            notificationManager.ShowNotification("Enemy Turn begins");
            enemyAI.ExecuteTurn(FinishEnemyTurn);
            yield break;
        }

        FinishEnemyTurn();
    }

    private void FinishEnemyTurn()
    {
        Debug.Log("Enemy turn completed. Starting villager turn.");
        notificationManager.ShowNotification("Enemy Turn ends");
        StartVillagerTurn();
    }

    private void StartVillagerTurn()
    {
        if (villagerAI != null)
        {
            notificationManager.ShowNotification("Villager Turn begins");
            villagerAI.ExecuteTurn(StartWorkerTurn);
            return;
        }

        StartWorkerTurn();
    }

    // Second pass of the villager phase: the player's autonomous workers.
    private void StartWorkerTurn()
    {
        if (workerAI != null && HasActiveWorkers)
        {
            workerAI.ExecuteTurn(FinishVillagerTurn);
            return;
        }

        FinishVillagerTurn();
    }

    /// <summary>Current enemy wave level, used to scale worker buffs.</summary>
    public int EnemyWaveLevel => enemyWaveLevel;

    private void FinishVillagerTurn()
    {
        Debug.Log("Villager turn completed. Resetting player units for next round.");
        notificationManager.ShowNotification("Villager Turn ends");
        nonPlayerTurnInProgress = false;

        SpawnEnemyReinforcements();
        foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
            unit.ClearRoundAttackers();
        currentRound++;
        if (GameStateManager.Instance != null) GameStateManager.Instance.RoundsElapsed = currentRound;
        combatUIManager?.SetRound(currentRound);
        combatUIManager?.RefreshSettlementLedger(); // the score drops each round
        combatUIManager?.RefreshTownsfolkDisplay();
        OnRoundAdvanced?.Invoke(currentRound);
        HealHeroesInChurches();
        foreach (UnitInstance unit in playerUnits)
        {
            if (unit != null && !unit.IsDead)
            {
                unit.actionsRemaining = unit.maxActionsPerTurn;
                unit.NotifyStatsChanged();
            }
        }

        TickBarracksDraft();
    }

    // A hero that ends the round standing in a Church heals; the Church is the only source of healing.
    private void HealHeroesInChurches()
    {
        MapManager map = MapManager.Instance;
        if (map == null) return;
        int amount = ResearchManager.Current.ChurchHealAmount;
        if (amount <= 0) return;

        foreach (UnitInstance church in map.GetStructures(BuildingType.Church))
        {
            UnitInstance hero = church != null && !church.IsDead && church.currentTile != null ? church.currentTile.garrison : null;
            if (hero == null || hero.IsDead || hero.currentHealth >= hero.maxHealth) continue;

            int healed = Mathf.Min(amount, hero.maxHealth - hero.currentHealth);
            hero.currentHealth += healed;
            hero.NotifyStatsChanged();
            notificationManager?.ShowNotification($"{hero.unitName} heals {healed} HP");
        }
    }

    // With a Barracks standing, every draftInterval rounds the player picks one of a few heroes (never villagers) or skips.
    private void TickBarracksDraft()
    {
        MapManager map = MapManager.Instance;
        GameStateManager gsm = GameStateManager.Instance;
        if (levelEnded || map == null || gsm == null || gsm.HeroesFull) return;
        if (map.GetStructures(BuildingType.Barracks).Count == 0) { barracksRounds = 0; return; }
        if (++barracksRounds < draftInterval) return;

        var pool = new List<UnitData>();
        if (gsm.AvailablePlayerArchetypes != null)
            foreach (UnitData archetype in gsm.AvailablePlayerArchetypes)
                if (archetype != null && archetype.Faction != UnitFaction.Villager && archetype.ModelPrefab != null)
                    pool.Add(archetype);
        if (pool.Count == 0) return;

        var options = new List<DraftPanel.Option>();
        while (options.Count < draftOptionCount && pool.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, pool.Count);
            options.Add(new DraftPanel.Option { Data = pool[index], Rarity = UnitRarityTable.Roll() });
            pool.RemoveAt(index);
        }

        DraftPanel panel = combatUIManager != null ? combatUIManager.GetComponent<DraftPanel>() : null;
        if (panel == null) return;

        barracksRounds = 0;
        ClearSelection();
        draftOpen = true;
        panel.Show(options, HandleDraftClosed);
    }

    private void HandleDraftClosed(DraftPanel.Option? picked)
    {
        draftOpen = false;
        if (!picked.HasValue) return;

        Unit unit = GameStateManager.Instance.AddHero(picked.Value.Data, picked.Value.Rarity);
        UnitInstance hero = unit != null ? MapManager.Instance.SpawnHero(unit) : null;
        if (hero == null)
        {
            if (unit != null) { GameStateManager.Instance.FullRoster.Remove(unit); GameStateManager.Instance.ActiveTeam.Remove(unit); }
            notificationManager?.ShowNotification("No room beside the Barracks");
            return;
        }

        Track(hero);
        combatUIManager?.RefreshRoster();
        combatUIManager?.RefreshSettlementLedger();
        notificationManager?.ShowNotification($"{unit.UnitName} joins the squad");
    }

    // Uses the same clustered edge spawn as the opening wave in MapManager.
    private void SpawnEnemyReinforcements()
    {
        if (levelEnded || MapManager.Instance == null) return;

        enemyUnits.RemoveAll(unit => unit == null || unit.IsDead);
        int livingPlayers = playerUnits.FindAll(u => u != null && !u.IsDead).Count;
        int playerBonus = livingPlayers * enemiesPerPlayerUnit;
        if (enemyUnits.Count >= reinforcementThreshold + playerBonus) return;
        if (currentRound - lastWaveRound < minRoundsBetweenWaves) return;
        lastWaveRound = currentRound;

        // Each respawn is one escalation step: the map grows and the new wave is stronger.
        enemyWaveLevel++;
        bool mapGrew = MapManager.Instance.ExpandMap();
        if (mapGrew) mapExpansions++;

        // Every third time the map grows, an evacuation point opens on the water.
        HexTile evacPoint = mapGrew && mapExpansions % EvacuationPointExpansionInterval == 0
            ? MapManager.Instance.CreateEvacuationPoint() : null;
        bool evacOpened = evacPoint != null;
        if (evacOpened && CameraController.Instance != null)
            CameraController.Instance.PanToPoint(evacPoint.transform.position);

        int targetCount = reinforcementTargetCount + mapExpansions * enemiesPerExpansion + playerBonus;
        int toSpawn = targetCount - enemyUnits.Count;
        List<UnitInstance> reinforcements = MapManager.Instance.SpawnEnemyWave(toSpawn, enemyWaveLevel);
        TrackAll(reinforcements);

        // NotificationManager shows one message at a time (a new call replaces the last), so send a single combined message.
        if (reinforcements.Count > 0)
        {
            string message = $"{reinforcements.Count} enemy reinforcements have arrived - enemies are stronger (Lv {enemyWaveLevel})";
            if (mapGrew) message += "\nThe battlefield has expanded";
            if (evacOpened) message += "\nAn evacuation point has opened";
            notificationManager.ShowNotification(message);
        }
        else if (mapGrew)
        {
            notificationManager.ShowNotification(evacOpened
                ? "The battlefield has expanded\nAn evacuation point has opened"
                : "The battlefield has expanded");
        }
    }

    private void PreviewDestination(HexTile destination)
    {
        pendingDestination = destination;
        previewPath.Clear();
        List<HexTile> path = HexPathfinder.FindPath(selectedUnit.currentTile, destination, selectedUnit.movementRange, selectedUnit);
        if (path == null) { pendingDestination = null; return; }
        previewPath.AddRange(path);
        if (previewPath.Count > 0) previewPath.RemoveAt(0); // Start is not part of the route preview.

        // Restore each tile's own highlight type; attack/rescue tiles must not turn into movement tiles.
        foreach (HexTile tile in highlightedTiles)
        {
            UnitInstance occupant = tile.occupyingUnit;
            TileHighlightType type = occupant == null || tile.CanEnter(selectedUnit) ? TileHighlightType.Movement
                : occupant.Faction == UnitFaction.Villager ? TileHighlightType.Rescue
                : TileHighlightType.Attack;
            tile.Highlight(type);
        }
        foreach (HexTile tile in previewPath) tile.Highlight(TileHighlightType.Path);
    }

    private void ClearSelection()
    {
        combatUIManager.SelectUnit(null);
        selectedUnit = null;
        buildMode = false;
        pendingBuilding = null;
        pendingDestination = null;
        previewPath.Clear();
        reachableTiles.Clear();
        foreach (HexTile tile in highlightedTiles) if (tile != null) tile.ClearHighlight();
        highlightedTiles.Clear();
    }

    public void TrackAll(IEnumerable<UnitInstance> units) { foreach (UnitInstance unit in units) Track(unit); }
    public void Track(UnitInstance unit)
    {
        if (unit == null) return;
        List<UnitInstance> list;

        if (unit.Faction == UnitFaction.Enemy)
        {
            list = enemyUnits;
        }
        else if (unit.Faction == UnitFaction.Player)
        {
            list = playerUnits;
        }
        else if (unit.Faction == UnitFaction.Villager)
        {
            list = unit.IsWorker ? workerUnits : villagerUnits;
        }
        else if (unit.Faction == UnitFaction.Structure)
        {
            return; // walls don't count toward win/loss and need no death bookkeeping
        }
        else
        {
            Debug.LogWarning($"CombatManager: Track called with unit {unit.name} of unknown faction {unit.Faction}. Ignoring.");
            return;
        }
        if (list.Contains(unit)) return;
        list.Add(unit);
        unit.OnDeath += HandleUnitDeath;
    }

    private void HandleUnitDeath(UnitInstance unit)
    {
        unit.OnDeath -= HandleUnitDeath;
        playerUnits.Remove(unit); enemyUnits.Remove(unit); villagerUnits.Remove(unit); workerUnits.Remove(unit);
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm != null)
        {
            if (unit.Faction == UnitFaction.Villager) gsm.RegisterVillagerDeath();
            if (unit.Faction == UnitFaction.Player) gsm.RegisterCasualty(unit.PersistentUnit);
            else if (unit.Faction == UnitFaction.Enemy) gsm.RegisterEnemyKilled();
        }
        if (unit == selectedUnit) ClearSelection();
        CheckTurnEnd();
    }
    public void HandleVillagerEvacuated(UnitInstance villager)
    {
        villagerUnits.Remove(villager);
    }

    public int GetVillagerCount()
    {
        return villagerUnits.Count;
    }
    public UnitInstance[] GetPlayerUnits()
    {
        return playerUnits.ToArray();
    }
}
