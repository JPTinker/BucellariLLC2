using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Handles selecting player units and their two-click movement confirmation.</summary>
public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }
    public event Action OnLevelWon;
    public event Action OnLevelLost;


    [SerializeField] private EnemyAIController enemyAI;
    private readonly List<UnitInstance> playerUnits = new List<UnitInstance>();
    private readonly List<UnitInstance> enemyUnits = new List<UnitInstance>();
    private readonly List<UnitInstance> villagerUnits = new List<UnitInstance>();
    private readonly List<HexTile> highlightedTiles = new List<HexTile>();
    private readonly HashSet<HexTile> reachableTiles = new HashSet<HexTile>();
    private readonly List<HexTile> previewPath = new List<HexTile>();
    private UnitInstance selectedUnit;
    private HexTile pendingDestination;
    private bool levelEnded;
    private bool enemyTurnInProgress;
    public int currentRound { get; private set; } = 1;

    public CombatPhaseUIController combatUIManager;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (enemyAI == null) enemyAI = FindAnyObjectByType<EnemyAIController>();
        if (enemyAI == null) enemyAI = gameObject.AddComponent<EnemyAIController>();
    }

    private void OnEnable()
    {
        if (combatUIManager != null)
            combatUIManager.OnActionRequested += HandleActionRequested;
    }

    private void OnDisable()
    {
        if (combatUIManager != null)
            combatUIManager.OnActionRequested -= HandleActionRequested;
    }

    private void Start() => TrackAll(FindObjectsByType<UnitInstance>());

    private void HandleActionRequested(CombatPhaseUIController.CombatAction action, UnitInstance unit)
    {
        if (unit == null)
        {
            Debug.LogWarning($"CombatManager: {action} clicked without a selected unit.");
            return;
        }

        switch (action)
        {
            case CombatPhaseUIController.CombatAction.Heal:
                HandleHealClicked(unit);
                break;
            case CombatPhaseUIController.CombatAction.Fortify:
                HandleFortifyClicked(unit);
                break;
            case CombatPhaseUIController.CombatAction.Extract:
                HandleExtractClicked(unit);
                break;
            case CombatPhaseUIController.CombatAction.Scout:
                HandleScoutClicked(unit);
                break;
        }
    }

    private static void HandleHealClicked(UnitInstance unit)
    {
        unit.onHeal();
        Debug.Log($"CombatManager: Heal clicked for {unit.unitName}.");
    }

    private static void HandleFortifyClicked(UnitInstance unit)
    {
        unit.onFortify();
        Debug.Log($"CombatManager: Fortify clicked for {unit.unitName}.");
    }

    private static void HandleExtractClicked(UnitInstance unit)
    {
        if (unit == null) return;

        Debug.Log($"CombatManager: Extract clicked for {unit.unitName}.");

        // Tell GameStateManager to handle the extraction logic (saving, tracking villagers, etc.)
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ProcessExtraction(unit);
        }
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
        if (tile == null || levelEnded || enemyTurnInProgress) return;
        if (selectedUnit == null)
        {
            if (tile.occupyingUnit != null && (tile.occupyingUnit.Faction == UnitFaction.Player)) {
                SelectUnit(tile.occupyingUnit);
                combatUIManager.SelectUnit(tile.occupyingUnit);
            }
            return;
        }
        else {
            if (tile.occupyingUnit != null &&
                tile.occupyingUnit.Faction == UnitFaction.Player)
            {
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
                else {
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
        if (selectedUnit.actionsRemaining <= 0)
        {
            // check to see if turn is over. 
            CheckTurnEnd();
        }
    }
    // Keeps existing prefab/event bindings working.
    public void selectedTile(HexTile tile) => SelectedTile(tile);

    public void SelectUnit(UnitInstance unit)
    {
        if (unit == null || unit.Faction == UnitFaction.Enemy || unit.IsDead || unit.currentTile == null) return;
        ClearSelection();
        selectedUnit = unit;
        if (unit.actionsRemaining <= 0) return;

        foreach (HexTile tile in HexPathfinder.GetReachableTiles(unit.currentTile, unit.movementRange))
        {
            if (!tile.CanEnter(unit)) continue;
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
        if (enemyTurnInProgress) return;

        Debug.Log("Ending player's turn and starting enemy's turn.");
        enemyTurnInProgress = true;
        ClearSelection();

        if (enemyAI != null)
        {
            enemyAI.ExecuteTurn(FinishEnemyTurn);
            return;
        }

        FinishEnemyTurn();
    }

    private void FinishEnemyTurn()
    {
        Debug.Log("Enemy turn completed. Resetting player units for next round.");
        enemyTurnInProgress = false;
        foreach (UnitInstance unit in playerUnits)
        {
            if (unit != null && !unit.IsDead)
                unit.actionsRemaining = unit.maxActionsPerTurn;
        }
    }

    private void PreviewDestination(HexTile destination)
    {
        pendingDestination = destination;
        previewPath.Clear();
        List<HexTile> path = HexPathfinder.FindPath(selectedUnit.currentTile, destination, selectedUnit.movementRange);
        if (path == null) { pendingDestination = null; return; }
        previewPath.AddRange(path);
        if (previewPath.Count > 0) previewPath.RemoveAt(0); // Start is not part of the route preview.

        foreach (HexTile tile in highlightedTiles) tile.Highlight(TileHighlightType.Movement);
        foreach (HexTile tile in previewPath) tile.Highlight(TileHighlightType.Path);
    }

    private void ClearSelection()
    {
        combatUIManager.SelectUnit(null);
        selectedUnit = null;
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

        if (unit.Faction == UnitFaction.Enemy) {
            list = enemyUnits;
        } 
        else if (unit.Faction == UnitFaction.Player) {
            list = playerUnits;
        } 
        else if(unit.Faction == UnitFaction.Villager){
            list = villagerUnits;
        }
        else {
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
        playerUnits.Remove(unit); enemyUnits.Remove(unit); villagerUnits.Remove(unit);
        if (unit == selectedUnit) ClearSelection();
        CheckLevelEnd();
        CheckTurnEnd();
    }
    public void HandleUnitExtract(UnitInstance unit)
    {
        playerUnits.Remove(unit);
        if (unit == selectedUnit) ClearSelection();
        CheckLevelEnd();
        CheckTurnEnd();
    }

    private void CheckLevelEnd()
    {
        if (levelEnded) return;
        if (playerUnits.Count == 0)
        {
            levelEnded = true;
            OnLevelLost?.Invoke();
        }
        else if (enemyUnits.Count == 0)
        {
            levelEnded = true;
            OnLevelWon?.Invoke();
        }
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
