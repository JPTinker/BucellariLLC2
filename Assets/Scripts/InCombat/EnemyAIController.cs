using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Executes a tactical enemy turn against non-enemy units.</summary>
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

        UnitInstance[] enemies = FindObjectsByType<UnitInstance>();
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
                Debug.Log($"{enemy.name} actions={enemy.actionsRemaining} atkRange={enemy.attackRange} moveRange={enemy.movementRange}");
                List<UnitInstance> targets = GetActiveTargets();
                UnitInstance target = FindBestTarget(enemy, targets);
                if (target == null) break;

                if (enemy.CanAttack(target))
                {
                    enemy.Attack(target);
                    SpendAction(enemy);
                    yield return new WaitForSeconds(actionDelay);
                    continue;
                }

                HexTile destination = FindBestDestination(enemy, targets, activeEnemies);
                if (destination == null || !enemy.MoveTo(destination, enemy.movementRange)) break;
                Debug.Log($"{enemy.name} moved to {destination.gridPosition}");
                SpendAction(enemy);
                yield return new WaitForSeconds(actionDelay);
            }
        }

        turnInProgress = false;
        onComplete?.Invoke();
    }

    private static List<UnitInstance> GetActiveTargets()
    {
        UnitInstance[] units = FindObjectsByType<UnitInstance>();
        List<UnitInstance> targets = new List<UnitInstance>();
        foreach (UnitInstance unit in units)
        {
            if (unit == null || unit.isEnemy || unit.IsDead || unit.currentTile == null)
                continue;

            if (unit.Faction != UnitFaction.Enemy)
                targets.Add(unit);
        }

        return targets;
    }

    private static UnitInstance FindBestTarget(UnitInstance enemy, List<UnitInstance> targets)
    {
        UnitInstance bestTarget = null;
        float bestScore = float.MinValue;

        foreach (UnitInstance target in targets)
        {
            float score = GetTargetScore(enemy, target);
            if (score > bestScore)
            {
                bestTarget = target;
                bestScore = score;
            }
        }

        return bestTarget;
    }

    private static HexTile FindBestDestination(
        UnitInstance enemy,
        List<UnitInstance> targets,
        List<UnitInstance> activeEnemies)
    {
        HexTile bestDestination = null;
        float bestScore = float.MinValue;

        foreach (HexTile tile in HexPathfinder.GetReachableTiles(enemy.currentTile, enemy.movementRange))
        {
            if (!tile.CanEnter(enemy)) continue;

            float terrainScore = tile.defenseBonus * 20f + tile.attackBonus * 10f;
            float bestTargetScore = float.MinValue;
            foreach (UnitInstance target in targets)
            {
                int distance = HexCoordinates.GetDistance(tile.gridPosition, target.currentTile.gridPosition);
                bool canAttackFromTile = distance <= enemy.attackRange;
                float targetScore = GetTargetScore(enemy, target) - distance * 4f;

                if (canAttackFromTile) targetScore += 1000f;
                if (enemy.attackRange > 1)
                    targetScore -= Mathf.Abs(distance - enemy.attackRange) * 3f;

                // Prefer positions that create a follow-up attack for other enemies.
                targetScore += CountAttackersAfterMove(target, enemy, tile, activeEnemies) * 35f;
                bestTargetScore = Mathf.Max(bestTargetScore, targetScore);
            }

            if (bestTargetScore == float.MinValue) continue;
            float score = terrainScore + bestTargetScore;
            if (score > bestScore)
            {
                bestDestination = tile;
                bestScore = score;
            }
        }

        return bestDestination;
    }

    private static int CountAttackersAfterMove(
        UnitInstance target,
        UnitInstance movingEnemy,
        HexTile destination,
        List<UnitInstance> activeEnemies)
    {
        int attackers = 0;
        foreach (UnitInstance enemy in activeEnemies)
        {
            if (enemy == null || enemy.IsDead || enemy.currentTile == null)
                continue;

            HexTile attackerTile = enemy == movingEnemy ? destination : enemy.currentTile;
            int distance = HexCoordinates.GetDistance(attackerTile.gridPosition, target.currentTile.gridPosition);
            if (distance <= enemy.attackRange)
                attackers++;
        }

        return attackers;
    }

    private static float GetTargetScore(UnitInstance enemy, UnitInstance target)
    {
        int distance = HexCoordinates.GetDistance(enemy.currentTile.gridPosition, target.currentTile.gridPosition);
        float healthRatio = target.maxHealth > 0 ? (float)target.currentHealth / target.maxHealth : 1f;
        return (1f - healthRatio) * 200f + target.attackPower * 3f - distance * 2f;
    }

    private static void SpendAction(UnitInstance unit)
    {
        unit.actionsRemaining = Mathf.Max(0, unit.actionsRemaining - 1);
    }
}
