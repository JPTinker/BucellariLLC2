using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Basic A* pathfinding across HexTile.neighbors, using isWalkable and movementCost.
/// This is what AI (and player) units call to move around the grid.
/// </summary>
public static class HexPathfinder
{
    /// Returns the tile path from start to goal (inclusive), or null if no path exists.
    /// Pass maxMovementCost >= 0 to cap how far a unit can path in one call.
    public static List<HexTile> FindPath(HexTile start, HexTile goal, int maxMovementCost = -1)
    {
        if (start == null || goal == null) return null;
        if (!goal.isWalkable) return null;

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
                if (!neighbor.isWalkable && neighbor != goal) continue;

                int tentativeG = gScore[current] + Mathf.Max(1, neighbor.movementCost);
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
    public static List<HexTile> GetReachableTiles(HexTile start, int movementRange)
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

                int newCost = currentCost + Mathf.Max(1, neighbor.movementCost);
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