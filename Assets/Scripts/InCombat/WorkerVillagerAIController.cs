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
/// When the player presses Workers Escape (CombatManager.WorkersEscaping) every
/// worker heads straight to the boat with whatever it holds and leaves.
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

        turnInProgress = false;
        onComplete?.Invoke();
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

        // Escape overrides whatever the worker was doing.
        if (combat != null && combat.WorkersEscaping)
            worker.State = WorkerState.Depositing;

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
                yield return StartCoroutine(DoDepositing(unit, worker, exfil, combat));
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
            worker.Gather(worker.IsFood ? b.WorkerFoodPerGather : b.WorkerMaterialsPerGather,
                          level, b.WorkerYieldBonusPerLevel, b.WorkerCarryCap);
        }
        worker.State = WorkerState.Depositing;
    }

    private IEnumerator DoDepositing(UnitInstance unit, WorkerVillager worker, HexTile exfil, CombatManager combat)
    {
        if (!IsAdjacentTo(unit, exfil))
        {
            HexTile approach = FindBestApproachTile(unit, exfil);
            if (approach != null) yield return StartCoroutine(MoveToward(unit, approach));
        }

        if (!IsAlive(unit) || !IsAdjacentTo(unit, exfil)) yield break;

        GameStateManager.Instance?.ProcessWorkerDeposit(worker.IsFood, worker.Carried);
        worker.Carried = 0;

        if (combat != null && combat.WorkersEscaping)
        {
            LeaveBattlefield(unit, combat);
            yield break;
        }

        worker.State = worker.Site != null ? WorkerState.Returning : WorkerState.Seeking;
    }

    private IEnumerator DoReturning(UnitInstance unit, WorkerVillager worker)
    {
        if (worker.Site == null) { worker.State = WorkerState.Seeking; yield break; }

        if (unit.currentTile != worker.Site)
            yield return StartCoroutine(MoveToward(unit, worker.Site));

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
            if (tested++ >= 8) break;
            if (tile != unit.currentTile)
            {
                var path = HexPathfinder.FindPath(unit.currentTile, tile, -1);
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

        List<HexTile> path = HexPathfinder.FindPath(unit.currentTile, destination, -1);
        if (path == null || path.Count < 2) yield break;

        HexTile step = FarthestReachablePoint(path, unit.movementRange);
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

    private static HexTile FindBestApproachTile(UnitInstance unit, HexTile exfil)
    {
        HexTile best = null;
        int bestLength = int.MaxValue;

        foreach (HexTile neighbor in exfil.neighbors)
        {
            if (neighbor == null || !neighbor.isWalkable) continue;

            List<HexTile> path = HexPathfinder.FindPath(unit.currentTile, neighbor, -1);
            if (path == null) continue;

            if (path.Count < bestLength)
            {
                bestLength = path.Count;
                best = neighbor;
            }
        }
        return best;
    }

    private static HexTile FarthestReachablePoint(List<HexTile> path, int movementRange)
    {
        HexTile result = path[0];
        int cost = 0;
        for (int i = 1; i < path.Count; i++)
        {
            cost += HexPathfinder.StepCost(i == 1, path[i], movementRange);
            if (cost > movementRange) break;
            result = path[i];
        }
        return result;
    }

    private static void LeaveBattlefield(UnitInstance unit, CombatManager combat)
    {
        unit.IsExtracted = true;
        if (unit.currentTile != null)
        {
            unit.currentTile.RemoveUnit();
            unit.currentTile = null;
        }
        combat.HandleWorkerDeposited(unit);
        Destroy(unit.gameObject);
    }

    private static HexTile FindExfilTile()
    {
        if (MapManager.Instance == null) return null;
        foreach (HexTile tile in MapManager.Instance.Tiles.Values)
        {
            if (tile != null && tile.IsExtractionPoint) return tile;
        }
        return null;
    }

    private static bool IsAdjacentTo(UnitInstance unit, HexTile exfil)
    {
        return unit.currentTile != null &&
               HexCoordinates.GetDistance(unit.currentTile.gridPosition, exfil.gridPosition) <= 1;
    }

    private static bool IsAlive(UnitInstance unit) => EnemyBattlefieldState.IsOnField(unit);

    private static bool IsActiveWorker(UnitInstance unit)
    {
        return EnemyBattlefieldState.IsOnField(unit) && unit.Faction == UnitFaction.Villager && unit.IsWorker;
    }
}
