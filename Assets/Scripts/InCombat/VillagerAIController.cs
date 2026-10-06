using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Villager turn: runs between the enemy turn and the player's next turn. Every
/// villager still loose on the field (not rescued, not already evacuated) spends
/// its actions walking toward the exfil tile. One that ends its move adjacent to
/// it evacuates itself, just like a player unit using Extract.
/// </summary>
public class VillagerAIController : MonoBehaviour
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

        HexTile exfilTile = FindExfilTile();
        List<UnitInstance> villagers = new List<UnitInstance>();
        foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
        {
            if (IsActiveVillager(unit))
            {
                unit.actionsRemaining = unit.maxActionsPerTurn;
                unit.NotifyStatsChanged();
                villagers.Add(unit);
            }
        }

        if (exfilTile != null)
        {
            foreach (UnitInstance villager in villagers)
            {
                if (IsActiveVillager(villager))
                    yield return StartCoroutine(RunVillagerTurn(villager, exfilTile));
            }
        }

        turnInProgress = false;
        onComplete?.Invoke();
    }

    private IEnumerator RunVillagerTurn(UnitInstance villager, HexTile exfilTile)
    {
        while (IsActiveVillager(villager) && villager.actionsRemaining > 0)
        {
            if (IsAdjacentToExfil(villager, exfilTile))
            {
                EvacuateVillager(villager);
                yield break;
            }

            HexTile approachTile = FindBestApproachTile(villager, exfilTile);
            if (approachTile == null) yield break;

            List<HexTile> path = HexPathfinder.FindPath(villager.currentTile, approachTile, -1);
            if (path == null || path.Count < 2) yield break;

            HexTile stepDestination = FarthestReachablePoint(path, villager.movementRange);
            if (stepDestination == villager.currentTile) yield break;

            if (!villager.MoveTo(stepDestination, villager.movementRange)) yield break;

            villager.actionsRemaining = Mathf.Max(0, villager.actionsRemaining - 1);
            villager.NotifyStatsChanged();
            yield return new WaitForSeconds(actionDelay);

            float waited = 0f;
            while (villager != null && villager.IsMoving && waited < moveTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        if (IsActiveVillager(villager) && IsAdjacentToExfil(villager, exfilTile))
            EvacuateVillager(villager);
    }

    /// Picks the closest (by path length) walkable, unoccupied tile next to the
    /// exfil tile for the villager to head toward.
    private static HexTile FindBestApproachTile(UnitInstance villager, HexTile exfilTile)
    {
        HexTile best = null;
        int bestLength = int.MaxValue;

        foreach (HexTile neighbor in exfilTile.neighbors)
        {
            if (neighbor == null || !neighbor.isWalkable) continue;

            List<HexTile> path = HexPathfinder.FindPath(villager.currentTile, neighbor, -1);
            if (path == null) continue;

            if (path.Count < bestLength)
            {
                bestLength = path.Count;
                best = neighbor;
            }
        }

        return best;
    }

    /// The furthest tile along path the villager can afford to walk to in one action.
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

    private static HexTile FindExfilTile()
    {
        if (MapManager.Instance == null) return null;
        foreach (HexTile tile in MapManager.Instance.Tiles.Values)
        {
            if (tile != null && tile.IsExtractionPoint) return tile;
        }
        return null;
    }

    private static bool IsAdjacentToExfil(UnitInstance villager, HexTile exfilTile)
    {
        return villager.currentTile != null &&
               HexCoordinates.GetDistance(villager.currentTile.gridPosition, exfilTile.gridPosition) <= 1;
    }

    private static void EvacuateVillager(UnitInstance villager)
    {
        GameStateManager.Instance?.ProcessVillagerEvacuation(villager);
    }

    private static bool IsActiveVillager(UnitInstance unit)
    {
        return EnemyBattlefieldState.IsOnField(unit) && unit.Faction == UnitFaction.Villager;
    }
}
