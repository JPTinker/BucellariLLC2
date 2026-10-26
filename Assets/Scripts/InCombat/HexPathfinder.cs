using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Basic A* pathfinding across HexTile.neighbors, using isWalkable and movementCost.
/// This is what AI (and player) units call to move around the grid.
/// </summary>
public static class HexPathfinder
{
    /// Cost to enter a tile. A tile adjacent to the start is always enterable, even when its
    /// cost exceeds the whole budget (e.g. 2 movement vs. cost-3 terrain); the unit just spends everything.
    public static int StepCost(bool fromStart, HexTile tile, int budget, UnitInstance mover = null)
    {
        int cost = Mathf.Max(1, tile.movementCost);
        if (InEnemyZoC(tile, mover)) cost += ZocPenalty;
        return fromStart && budget >= 0 ? Mathf.Min(cost, budget) : cost;
    }

    private static int ZocPenalty
    {
        get
        {
            if (GameStateManager.Instance == null) return 2;
            GameStateManager.CycleBalanceConfig balance = GameStateManager.Instance.Balance;
            return balance.ZocEnabled ? Mathf.Max(0, balance.ZocPenalty) : 0;
        }
    }

    /// A tile is in a unit's zone of control when a living hostile melee unit stands next to it.
    /// Only the tile being entered is checked, so moving past the back of a melee unit stays free.
    public static bool InEnemyZoC(HexTile tile, UnitInstance mover)
    {
        if (tile == null || mover == null || ZocPenalty <= 0) return false;
        foreach (HexTile neighbor in tile.neighbors)
        {
            if (neighbor == null) continue;
            if (ProjectsZoC(neighbor.occupyingUnit, mover) || ProjectsZoC(neighbor.garrison, mover)) return true;
        }
        return false;
    }

    private static bool ProjectsZoC(UnitInstance unit, UnitInstance mover)
    {
        if (unit == null || unit == mover || unit.IsDead || unit.attackRange > 1) return false;
        if (mover.Faction == UnitFaction.Enemy) return unit.Faction == UnitFaction.Player;
        return unit.Faction == UnitFaction.Enemy && (mover.Faction == UnitFaction.Player || mover.Faction == UnitFaction.Villager);
    }

    /// Same side: Player and Villager units are one team, enemies are another.
    private static bool IsFriendly(UnitInstance a, UnitInstance b)
    {
        if (a == null || b == null || a.Faction == UnitFaction.Structure || b.Faction == UnitFaction.Structure) return false;
        if (a.Faction == b.Faction) return true;
        return (a.Faction == UnitFaction.Player || a.Faction == UnitFaction.Villager) && (b.Faction == UnitFaction.Player || b.Faction == UnitFaction.Villager);
    }

    /// A tile a unit may stop on: empty, or an empty-garrison Town Center / Barracks / Church / Library for a hero.
    private static bool CanStop(HexTile tile, UnitInstance mover)
    {
        if (tile.isWalkable && !tile.IsOccupied) return true;
        return mover != null && mover.Faction == UnitFaction.Player && tile.IsEnterableStructure && tile.garrison == null;
    }

    /// A tile a unit may walk across: anywhere it can stop, plus tiles held by friendly units
    /// and (for heroes) the Town Center, Barracks, Church and Library.
    private static bool CanPass(HexTile tile, UnitInstance mover)
    {
        if (tile.isWalkable && !tile.IsOccupied) return true;
        if (mover == null) return false;
        if (mover.Faction == UnitFaction.Player && tile.IsEnterableStructure) return true;
        return IsFriendly(mover, tile.occupyingUnit) && !tile.occupyingUnit.IsDead;
    }

    /// Returns the tile path from start to goal (inclusive), or null if no path exists.
    /// Pass maxMovementCost >= 0 to cap how far a unit can path in one call.
    public static List<HexTile> FindPath(HexTile start, HexTile goal, int maxMovementCost = -1, UnitInstance mover = null)
    {
        if (start == null || goal == null) return null;
        if (!CanStop(goal, mover)) return null;

        var openSet = new List<HexTile> { start };
        var cameFrom = new Dictionary<HexTile, HexTile>();
        var gScore = new Dictionary<HexTile, int> { [start] = 0 };
        var fScore = new Dictionary<HexTile, int> { [start] = Heuristic(start, goal) };

        while (openSet.Count > 0)
        {
            HexTile current = GetLowestFScore(openSet, fScore);

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            openSet.Remove(current);

            foreach (HexTile neighbor in current.neighbors)
            {
                if (neighbor == null) continue;
                if (!CanPass(neighbor, mover)) continue;

                int tentativeG = gScore[current] + StepCost(current == start, neighbor, maxMovementCost, mover);
                if (maxMovementCost >= 0 && tentativeG > maxMovementCost) continue;

                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);

                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                }
            }
        }

        return null; // no path found
    }

    /// Returns all tiles reachable from start within movementRange (excludes start itself).
    /// Useful for AI to evaluate move options, or to draw a movement-range highlight.
    public static List<HexTile> GetReachableTiles(HexTile start, int movementRange, UnitInstance mover = null)
    {
        var cheapestCost = new Dictionary<HexTile, int> { [start] = 0 };
        var frontier = new Queue<HexTile>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            HexTile current = frontier.Dequeue();
            int currentCost = cheapestCost[current];

            foreach (HexTile neighbor in current.neighbors)
            {
                if (neighbor == null || (!neighbor.isWalkable && !neighbor.IsOccupied)) continue;

                int newCost = currentCost + StepCost(current == start, neighbor, movementRange, mover);
                if (newCost > movementRange) continue;

                if (!cheapestCost.ContainsKey(neighbor) || newCost < cheapestCost[neighbor])
                {
                    cheapestCost[neighbor] = newCost;
                    frontier.Enqueue(neighbor);
                }
            }
        }

        var result = new List<HexTile>(cheapestCost.Keys);
        result.Remove(start);
        return result;
    }

    /// Returns all tiles within attackRange steps of start, excluding start itself.
    /// Attack range ignores walkability so occupied tiles can be valid targets.
    public static List<HexTile> GetAttackableTiles(HexTile start, int attackRange)
    {
        var result = new List<HexTile>();
        if (start == null || attackRange <= 0) return result;

        var distances = new Dictionary<HexTile, int> { [start] = 0 };
        var frontier = new Queue<HexTile>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            HexTile current = frontier.Dequeue();
            int currentDistance = distances[current];
            if (currentDistance >= attackRange) continue;

            foreach (HexTile neighbor in current.neighbors)
            {
                if (neighbor == null || distances.ContainsKey(neighbor)) continue;

                int distance = currentDistance + 1;
                distances[neighbor] = distance;
                result.Add(neighbor);
                frontier.Enqueue(neighbor);
            }
        }

        return result;
    }

    private static int Heuristic(HexTile a, HexTile b)
    {
        return HexCoordinates.GetDistance(a.gridPosition, b.gridPosition);
    }

    private static HexTile GetLowestFScore(List<HexTile> openSet, Dictionary<HexTile, int> fScore)
    {
        HexTile best = openSet[0];
        int bestScore = fScore.TryGetValue(best, out int bs) ? bs : int.MaxValue;

        foreach (HexTile tile in openSet)
        {
            int score = fScore.TryGetValue(tile, out int s) ? s : int.MaxValue;
            if (score < bestScore)
            {
                best = tile;
                bestScore = score;
            }
        }

        return best;
    }

    private static List<HexTile> ReconstructPath(Dictionary<HexTile, HexTile> cameFrom, HexTile current)
    {
        var path = new List<HexTile> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
