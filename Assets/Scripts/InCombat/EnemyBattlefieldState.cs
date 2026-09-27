using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EnemyBattlefieldState
{
    public List<UnitInstance> EnemyUnits { get; private set; } = new();
    public List<UnitInstance> PlayerUnits { get; private set; } = new();
    public List<UnitInstance> Villagers { get; private set; } = new();

    public HexTile EscapeTile { get; private set; }

    public Dictionary<UnitInstance, List<HexTile>> ReachableTiles = new();
    public Dictionary<UnitInstance, List<UnitInstance>> AttackableTargets = new();

    //---------------------------------------
    // Build battlefield snapshot
    //---------------------------------------
    public static EnemyBattlefieldState Build()
    {
        EnemyBattlefieldState state = new EnemyBattlefieldState();

        UnitInstance[] allUnits = Object.FindObjectsByType<UnitInstance>();

        foreach (UnitInstance unit in allUnits)
        {
            if (unit == null)
                continue;

            if (unit.IsDead)
                continue;

            if (unit.currentTile == null)
                continue;

            switch (unit.Faction)
            {
                case UnitFaction.Enemy:
                    state.EnemyUnits.Add(unit);
                    break;

                case UnitFaction.Player:
                    state.PlayerUnits.Add(unit);
                    break;

                case UnitFaction.Villager:
                    state.Villagers.Add(unit);
                    break;
            }
        }

        //---------------------------------------
        // Reachable tiles
        //---------------------------------------
        foreach (UnitInstance unit in allUnits)
        {
            if (unit == null || unit.IsDead || unit.currentTile == null)
                continue;

            state.ReachableTiles[unit] =
                HexPathfinder.GetReachableTiles(
                    unit.currentTile,
                    unit.movementRange)
                .ToList();
        }

        //---------------------------------------
        // Attackable targets
        //---------------------------------------
        foreach (UnitInstance enemy in state.EnemyUnits)
        {
            List<UnitInstance> attackable = new();

            foreach (UnitInstance player in state.PlayerUnits)
            {
                int distance =
                    HexCoordinates.GetDistance(
                        enemy.currentTile.gridPosition,
                        player.currentTile.gridPosition);

                if (distance <= enemy.attackRange)
                    attackable.Add(player);
            }

            state.AttackableTargets[enemy] = attackable;
        }

        //---------------------------------------
        // Escape Tile
        //---------------------------------------
        state.EscapeTile = FindEscapeTile();

        return state;
    }

    //---------------------------------------
    // Queries
    //---------------------------------------

    public UnitInstance GetClosestPlayer(UnitInstance enemy)
    {
        UnitInstance best = null;
        int bestDistance = int.MaxValue;

        foreach (UnitInstance player in PlayerUnits)
        {
            int distance =
                HexCoordinates.GetDistance(
                    enemy.currentTile.gridPosition,
                    player.currentTile.gridPosition);

            if (distance < bestDistance)
            {
                best = player;
                bestDistance = distance;
            }
        }

        return best;
    }

    public int DistanceToEscape(UnitInstance unit)
    {
        if (EscapeTile == null)
            return 999;

        return HexCoordinates.GetDistance(
            unit.currentTile.gridPosition,
            EscapeTile.gridPosition);
    }

    public UnitInstance GetClosestEnemyToEscape()
    {
        UnitInstance best = null;
        int bestDistance = int.MaxValue;

        foreach (UnitInstance enemy in EnemyUnits)
        {
            int distance = DistanceToEscape(enemy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = enemy;
            }
        }

        return best;
    }

    public int CountEnemyAttackers(UnitInstance target)
    {
        int attackers = 0;

        foreach (UnitInstance enemy in EnemyUnits)
        {
            int distance =
                HexCoordinates.GetDistance(
                    enemy.currentTile.gridPosition,
                    target.currentTile.gridPosition);

            if (distance <= enemy.attackRange)
                attackers++;
        }

        return attackers;
    }

    //---------------------------------------
    // Replace this later
    //---------------------------------------
    private static HexTile FindEscapeTile()
    {
        HexTile[] tiles = Object.FindObjectsByType<HexTile>();

        foreach (HexTile tile in tiles)
        {
            if (tile.gridPosition.x == 0 &&
                tile.gridPosition.y == 0)
            {
                return tile;
            }
        }

        return null;
    }
}