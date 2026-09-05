using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Executes a tactical enemy turn against the player units.</summary>
public class EnemyAIController : MonoBehaviour
{
    [SerializeField] private float actionDelay = 0.1f;

    private bool turnInProgress;

    public void ExecuteTurn(Action onComplete)
    {
        if (turnInProgress) return;
        StartCoroutine(ExecuteTurnRoutine(onComplete));
    }

    private IEnumerator ExecuteTurnRoutine(Action onComplete)
    {
        turnInProgress = true;

        UnitInstance[] enemies = FindObjectsByType<UnitInstance>(FindObjectsSortMode.None);
        List<UnitInstance> activeEnemies = new List<UnitInstance>();
        foreach (UnitInstance enemy in enemies)
        {
            if (enemy != null && enemy.isEnemy && !enemy.IsDead && enemy.currentTile != null)
            {
                enemy.actionsRemaining = enemy.maxActionsPerTurn;
                activeEnemies.Add(enemy);
            }
        }

        foreach (UnitInstance enemy in activeEnemies)
        {
            while (enemy != null && !enemy.IsDead && enemy.actionsRemaining > 0)
            {
                List<UnitInstance> players = GetActivePlayers();
                UnitInstance target = FindBestTarget(enemy, players);
                if (target == null) break;

                if (enemy.CanAttack(target))
                {
                    enemy.Attack(target);
                    SpendAction(enemy);
                    yield return new WaitForSeconds(actionDelay);
                    continue;
                }

                HexTile destination = FindBestDestination(enemy, players);
                if (destination == null || !enemy.MoveTo(destination, enemy.movementRange)) break;

                SpendAction(enemy);
                yield return new WaitForSeconds(actionDelay);
            }
        }

        turnInProgress = false;
        onComplete?.Invoke();
    }

    private static List<UnitInstance> GetActivePlayers()
    {
        UnitInstance[] units = FindObjectsByType<UnitInstance>(FindObjectsSortMode.None);
        List<UnitInstance> players = new List<UnitInstance>();
        foreach (UnitInstance unit in units)
        {
            if (unit != null && !unit.isEnemy && !unit.IsDead && unit.currentTile != null)
                players.Add(unit);
        }

        return players;
    }

    private static UnitInstance FindBestTarget(UnitInstance enemy, List<UnitInstance> players)
    {
        UnitInstance bestTarget = null;
        float bestScore = float.MinValue;

        foreach (UnitInstance player in players)
        {
            float score = GetTargetScore(enemy, player);
            if (score > bestScore)
            {
                bestTarget = player;
                bestScore = score;
            }
        }

        return bestTarget;
    }

    private static HexTile FindBestDestination(UnitInstance enemy, List<UnitInstance> players)
    {
        HexTile bestDestination = null;
        float bestScore = float.MinValue;

        foreach (HexTile tile in HexPathfinder.GetReachableTiles(enemy.currentTile, enemy.movementRange))
        {
            if (!tile.CanEnter(enemy)) continue;

            float terrainScore = tile.defenseBonus * 20f + tile.attackBonus * 10f;
            float bestTargetScore = float.MinValue;
            foreach (UnitInstance player in players)
            {
                int distance = HexCoordinates.GetDistance(tile.gridPosition, player.currentTile.gridPosition);
                bool canAttackFromTile = distance <= enemy.attackRange;
                float targetScore = GetTargetScore(enemy, player) - distance * 4f;

                if (canAttackFromTile) targetScore += 1000f;
                if (enemy.attackRange > 1)
                    targetScore -= Mathf.Abs(distance - enemy.attackRange) * 3f;

                bestTargetScore = Mathf.Max(bestTargetScore, targetScore);
            }

            float score = terrainScore + bestTargetScore;
            if (score > bestScore)
            {
                bestDestination = tile;
                bestScore = score;
            }
        }

        return bestDestination;
    }

    private static float GetTargetScore(UnitInstance enemy, UnitInstance player)
    {
        int distance = HexCoordinates.GetDistance(enemy.currentTile.gridPosition, player.currentTile.gridPosition);
        float healthRatio = player.maxHealth > 0 ? (float)player.currentHealth / player.maxHealth : 1f;
        return (1f - healthRatio) * 200f + player.attackPower * 3f - distance * 2f;
    }

    private static void SpendAction(UnitInstance unit)
    {
        unit.actionsRemaining = Mathf.Max(0, unit.actionsRemaining - 1);
    }
}
