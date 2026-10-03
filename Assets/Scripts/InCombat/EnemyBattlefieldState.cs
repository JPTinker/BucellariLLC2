using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Snapshot of the battlefield the enemy AI plans against. It is cheap to build
/// and is rebuilt after every enemy commits its actions, so planning never runs
/// against stale positions or dead units.
/// </summary>
public class EnemyBattlefieldState
{
    public const int Unreachable = int.MaxValue;

    public List<UnitInstance> EnemyUnits { get; } = new();
    public List<UnitInstance> PlayerUnits { get; } = new();
    public List<UnitInstance> Villagers { get; } = new();
    public List<UnitInstance> Structures { get; } = new();

    /// Everything the horde wants dead: player units, villagers and player-built structures.
    public List<UnitInstance> Targets { get; } = new();

    // Extra path cost for walking through a tile an ally currently stands on.
    // Allies will probably move, so it's passable for multi-turn estimates, but
    // the penalty spreads the horde around a target instead of queueing up.
    private readonly int allyPassCost;

    // Movement cost to reach any tile from which a unit with the given attack
    // range could hit the target. Built lazily per (target, range).
    private readonly Dictionary<(UnitInstance, int), Dictionary<HexTile, int>> approachFields = new();

    private EnemyBattlefieldState(int allyPassCost)
    {
        this.allyPassCost = Mathf.Max(0, allyPassCost);
    }

    //---------------------------------------
    // Build battlefield snapshot
    //---------------------------------------
    public static EnemyBattlefieldState Build(int allyPassCost = 2)
    {
        EnemyBattlefieldState state = new EnemyBattlefieldState(allyPassCost);

        foreach (UnitInstance unit in UnityEngine.Object.FindObjectsByType<UnitInstance>())
        {
            if (!IsOnField(unit))
                continue;

            switch (unit.Faction)
            {
                case UnitFaction.Enemy:
                    state.EnemyUnits.Add(unit);
                    break;

                case UnitFaction.Player:
                    state.PlayerUnits.Add(unit);
                    state.Targets.Add(unit);
                    break;

                case UnitFaction.Villager:
                    state.Villagers.Add(unit);
                    state.Targets.Add(unit);
                    break;

                case UnitFaction.Structure:
                    state.Structures.Add(unit);
                    state.Targets.Add(unit);
                    break;
            }
        }

        return state;
    }

    public static bool IsOnField(UnitInstance unit)
    {
        return unit != null && unit.enabled && !unit.IsDead && !unit.IsExtracted && unit.currentTile != null;
    }

    //---------------------------------------
    // Tile helpers
    //---------------------------------------

    /// HexTile.SetUnit clears isWalkable, so an occupied tile is still walkable terrain.
    public static bool IsWalkableTerrain(HexTile tile)
    {
        return tile != null && (tile.isWalkable || tile.IsOccupied);
    }

    public static int EnterCost(HexTile tile)
    {
        return Mathf.Max(1, tile.movementCost);
    }

    private static bool IsEnemyOccupied(HexTile tile)
    {
        return tile.occupyingUnit != null && tile.occupyingUnit.Faction == UnitFaction.Enemy;
    }

    //---------------------------------------
    // Queries
    //---------------------------------------

    /// Tiles reachable from start within movementRange (excluding start), with their
    /// path cost. isBlocked decides which tiles can't be entered or passed through,
    /// so the planner can account for tiles it has vacated or targets it has killed.
    public static Dictionary<HexTile, int> GetReachable(HexTile start, int movementRange, Func<HexTile, bool> isBlocked)
    {
        Dictionary<HexTile, int> reachable = Dijkstra(
            new[] { start },
            movementRange,
            tile => IsWalkableTerrain(tile) && !isBlocked(tile),
            (from, to) => HexPathfinder.StepCost(from == start, to, movementRange));

        reachable.Remove(start);
        return reachable;
    }

    /// Movement cost for a unit standing on 'from' to get within attackRange of target.
    /// Returns 0 if it already is, or Unreachable if no path exists.
    public int GetApproachCost(UnitInstance target, int attackRange, HexTile from)
    {
        if (HexCoordinates.GetDistance(from.gridPosition, target.currentTile.gridPosition) <= attackRange)
            return 0;

        var key = (target, attackRange);
        if (!approachFields.TryGetValue(key, out Dictionary<HexTile, int> field))
        {
            field = BuildApproachField(target, attackRange);
            approachFields[key] = field;
        }

        return field.TryGetValue(from, out int cost) ? cost : Unreachable;
    }

    private Dictionary<HexTile, int> BuildApproachField(UnitInstance target, int attackRange)
    {
        // Tiles a non-enemy stands on are walls (FindPath won't cross them);
        // tiles allies stand on are passable at a premium.
        bool CanStand(HexTile tile) =>
            IsWalkableTerrain(tile) && (tile.occupyingUnit == null || IsEnemyOccupied(tile));

        List<HexTile> attackPositions = new List<HexTile>();
        foreach (HexTile tile in HexPathfinder.GetAttackableTiles(target.currentTile, attackRange))
        {
            if (CanStand(tile))
                attackPositions.Add(tile);
        }

        // Reverse search from the attack positions: stepping from 'to' onto 'from'
        // on the real walk costs whatever it takes to enter 'from'.
        return Dijkstra(
            attackPositions,
            Unreachable,
            CanStand,
            (from, to) => EnterCost(from) + (IsEnemyOccupied(from) ? allyPassCost : 0));
    }

    /// Bucket-queue Dijkstra over HexTile.neighbors. Edge costs are small positive
    /// ints, so buckets beat a sorted open list on a grid this size.
    private static Dictionary<HexTile, int> Dijkstra(
        IEnumerable<HexTile> sources,
        int maxCost,
        Func<HexTile, bool> canEnter,
        Func<HexTile, HexTile, int> edgeCost)
    {
        var cost = new Dictionary<HexTile, int>();
        var buckets = new List<List<HexTile>>();

        void Push(HexTile tile, int c)
        {
            while (buckets.Count <= c)
                buckets.Add(new List<HexTile>());
            buckets[c].Add(tile);
        }

        foreach (HexTile source in sources)
        {
            if (source == null || cost.ContainsKey(source)) continue;
            cost[source] = 0;
            Push(source, 0);
        }

        for (int c = 0; c < buckets.Count; c++)
        {
            foreach (HexTile current in buckets[c])
            {
                if (cost[current] != c) continue; // stale entry

                foreach (HexTile neighbor in current.neighbors)
                {
                    if (neighbor == null || !canEnter(neighbor)) continue;

                    int step = Mathf.Max(1, edgeCost(current, neighbor));
                    long next = (long)c + step;
                    if (next > maxCost) continue;

                    if (!cost.TryGetValue(neighbor, out int existing) || next < existing)
                    {
                        cost[neighbor] = (int)next;
                        Push(neighbor, (int)next);
                    }
                }
            }
        }

        return cost;
    }
}
