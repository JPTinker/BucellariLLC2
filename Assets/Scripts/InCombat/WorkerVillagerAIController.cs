using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Worker turn: runs right after the stray-villager turn. The player never
/// controls these villagers. Each worker cycles
///   Seeking  -> walk to a flat grass tile (farmer) / forested tile (lumberjack)
///   Building -> spend a turn raising the farm / lumber mill
///   Gathering-> spend a turn working it
///   Depositing -> carry the load to the boat (exfil)
///   Returning  -> walk back to the site, then gather again
/// Workers can be picked up by heroes (UnitInstance.Rescue) and taken to an evacuation point.
/// </summary>
public class WorkerVillagerAIController : MonoBehaviour
{
    [SerializeField] private float actionDelay = 0.1f;
    [Tooltip("Safety cap on waiting for a move animation to finish.")]
    [SerializeField] private float moveTimeout = 5f;

    private bool turnInProgress;

    public void ExecuteTurn(Action onComplete)
    {
        if (turnInProgress) return;
        StartCoroutine(ExecuteTurnRoutine(onComplete));
    }

    private IEnumerator ExecuteTurnRoutine(Action onComplete)
    {
        turnInProgress = true;
        try
        {
            HexTile exfil = FindExfilTile();
            List<UnitInstance> workers = new List<UnitInstance>();
            foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
            {
                if (IsActiveWorker(unit)) workers.Add(unit);
            }

            if (exfil != null)
            {
                foreach (UnitInstance unit in workers)
                {
                    if (IsActiveWorker(unit))
                        yield return StartCoroutine(RunWorkerTurn(unit, exfil));
                }
            }
        }
        finally
        {
            // Always hand control back, or one exception freezes the whole non-player phase.
            turnInProgress = false;
            onComplete?.Invoke();
        }
    }

    private IEnumerator RunWorkerTurn(UnitInstance unit, HexTile exfil)
    {
        WorkerVillager worker = unit.GetComponent<WorkerVillager>();
        if (worker == null) yield break;

        GameStateManager gsm = GameStateManager.Instance;
        CombatManager combat = CombatManager.Instance;
        int level = combat != null ? combat.EnemyWaveLevel : 0;
        float hpBonus = gsm != null ? gsm.Balance.WorkerHpBonusPerLevel : 0f;
        worker.ApplyLevelBuff(level, hpBonus);

        switch (worker.State)
        {
            case WorkerState.Seeking:
                yield return StartCoroutine(DoSeeking(unit, worker, exfil));
                break;

            case WorkerState.Building:
                DoBuilding(worker);
                break;

            case WorkerState.Gathering:
                DoGathering(worker, level);
                break;

            case WorkerState.Depositing:
                yield return StartCoroutine(DoDepositing(unit, worker));
                break;

            case WorkerState.Returning:
                yield return StartCoroutine(DoReturning(unit, worker));
                break;
        }
    }

    // ---------------- states ----------------

    private IEnumerator DoSeeking(UnitInstance unit, WorkerVillager worker, HexTile exfil)
    {
        if (worker.Site == null)
        {
            worker.Site = ClaimSite(unit, worker, exfil);
            if (worker.Site == null) yield break; // nothing suitable on the map yet; try again next turn
        }

        if (unit.currentTile != worker.Site)
            yield return StartCoroutine(MoveToward(unit, worker.Site));

        // Arrived: the build happens next turn.
        if (IsAlive(unit) && unit.currentTile == worker.Site)
            worker.State = WorkerState.Building;
    }

    private static void DoBuilding(WorkerVillager worker)
    {
        if (worker.Structure == null && MapManager.Instance != null)
        {
            worker.Structure = MapManager.Instance.BuildWorkerStructure(worker.Site, worker.IsFood);
        }
        worker.State = WorkerState.Gathering;
    }

    private static void DoGathering(WorkerVillager worker, int level)
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm != null)
        {
            var b = gsm.Balance;
            // Gathering research scales both the haul per turn and how much a worker can carry.
            float rate = ResearchManager.Current.GatherMultiplier;
            int perGather = Mathf.RoundToInt((worker.IsFood ? b.WorkerFoodPerGather : b.WorkerMaterialsPerGather) * rate);
            worker.Gather(perGather, level, b.WorkerYieldBonusPerLevel, Mathf.RoundToInt(b.WorkerCarryCap * rate));
        }
        worker.State = WorkerState.Depositing;
    }

    private IEnumerator DoDepositing(UnitInstance unit, WorkerVillager worker)
    {
        if (!IsAdjacentToBoat(unit))
        {
            HexTile approach = MapManager.Instance.FindTownCenterApproach(unit);
            if (approach != null) yield return StartCoroutine(MoveToward(unit, approach));
            else Debug.LogWarning($"WorkerAI: {unit.unitName} has no reachable Town Center approach tile.");
        }

        if (!IsAlive(unit) || !IsAdjacentToBoat(unit)) yield break;

        GameStateManager.Instance?.ProcessWorkerDeposit(worker.IsFood, worker.Carried);
        worker.Carried = 0;

        worker.State = worker.Site != null ? WorkerState.Returning : WorkerState.Seeking;
    }

    private IEnumerator DoReturning(UnitInstance unit, WorkerVillager worker)
    {
        if (worker.Site == null) { worker.State = WorkerState.Seeking; yield break; }

        if (unit.currentTile != worker.Site)
        {
            // Someone else is standing on the site (or it is cut off): give it up and pick a new one.
            if (!worker.Site.isWalkable || HexPathfinder.FindPath(unit.currentTile, worker.Site, -1, unit) == null)
            {
                worker.Site.workerSiteClaimed = false;
                worker.Site = null;
                worker.State = WorkerState.Seeking;
                yield break;
            }
            yield return StartCoroutine(MoveToward(unit, worker.Site));
        }

        if (IsAlive(unit) && unit.currentTile == worker.Site)
            worker.State = WorkerState.Gathering;
    }

    // ---------------- helpers ----------------

    /// Picks the best unclaimed site (scored by walking distance + distance to the boat, so farms stay near it) and claims it.
    private static HexTile ClaimSite(UnitInstance unit, WorkerVillager worker, HexTile exfil)
    {
        if (MapManager.Instance == null || unit.currentTile == null) return null;

        List<HexTile> candidates = new List<HexTile>();
        foreach (HexTile tile in MapManager.Instance.Tiles.Values)
        {
            if (tile == null) continue;
            // Never build on the exfil or the tiles around it: heroes extract from those.
            if (tile.IsExtractionPoint || tile.IsExtractable ||
                HexCoordinates.GetDistance(tile.gridPosition, exfil.gridPosition) <= 1) continue;
            bool ok = worker.IsFood ? tile.IsFarmable : tile.IsLumberable;
            // The worker's own tile is unwalkable (it is standing on it) but still a fine site.
            if (!ok && tile == unit.currentTile)
                ok = !tile.workerSiteClaimed && (worker.IsFood
                    ? tile.terrainType == TerrainType.Grass && !tile.isForest && !tile.isHill
                    : tile.isForest);
            if (ok) candidates.Add(tile);
        }

        candidates.Sort((a, b) => Score(a, unit, exfil).CompareTo(Score(b, unit, exfil)));

        // Test the most promising few for a real route.
        int tested = 0;
        foreach (HexTile tile in candidates)
        {
            if (tested++ >= 30) break;
            if (tile != unit.currentTile)
            {
                var path = HexPathfinder.FindPath(unit.currentTile, tile, -1, unit);
                if (path == null) continue;
            }
            tile.workerSiteClaimed = true;
            return tile;
        }
        return null;
    }

    private static int Score(HexTile tile, UnitInstance unit, HexTile exfil)
    {
        return HexCoordinates.GetDistance(tile.gridPosition, unit.currentTile.gridPosition)
             + HexCoordinates.GetDistance(tile.gridPosition, exfil.gridPosition);
    }

    /// One walk toward the destination: as far as the worker's movement range allows this turn.
    private IEnumerator MoveToward(UnitInstance unit, HexTile destination)
    {
        if (!IsAlive(unit) || destination == null) yield break;

        List<HexTile> path = HexPathfinder.FindPath(unit.currentTile, destination, -1, unit);
        if (path == null || path.Count < 2) yield break;

        HexTile step = FarthestReachablePoint(path, unit.movementRange, unit);
        if (step == unit.currentTile) yield break;
        if (!unit.MoveTo(step, unit.movementRange)) yield break;

        yield return new WaitForSeconds(actionDelay);

        float waited = 0f;
        while (unit != null && unit.IsMoving && waited < moveTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }
    }

    private static HexTile FarthestReachablePoint(List<HexTile> path, int movementRange, UnitInstance mover)
    {
        HexTile result = path[0];
        int cost = 0;
        for (int i = 1; i < path.Count; i++)
        {
            cost += HexPathfinder.StepCost(i == 1, path[i], movementRange, mover);
            if (cost > movementRange) break;
            result = path[i];
        }
        return result;
    }

    private static HexTile FindExfilTile() => MapManager.Instance != null ? MapManager.Instance.GetTownCenterTile() : null;

    private static bool IsAdjacentToBoat(UnitInstance unit)
    {
        return unit.currentTile != null && MapManager.Instance != null &&
               MapManager.Instance.IsAdjacentToTownCenter(unit.currentTile);
    }

    private static bool IsAlive(UnitInstance unit) => EnemyBattlefieldState.IsOnField(unit);

    private static bool IsActiveWorker(UnitInstance unit)
    {
        return EnemyBattlefieldState.IsOnField(unit) && unit.Faction == UnitFaction.Villager && unit.IsWorker;
    }
}
