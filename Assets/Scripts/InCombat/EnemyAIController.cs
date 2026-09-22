using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            if (enemy != null && enemy.Faction == UnitFaction.Enemy && !enemy.IsDead && enemy.currentTile != null)
            {
                enemy.actionsRemaining = enemy.maxActionsPerTurn;
                activeEnemies.Add(enemy);
            }
        }

        // Process the most boxed-in units first, so they can untangle
        // themselves before the group commits to positions.
        activeEnemies = activeEnemies
            .OrderBy(e => HexPathfinder.GetReachableTiles(e.currentTile, e.movementRange).Count())
            .ToList();

        // Units that had a target but literally couldn't reach any tile
        // (fully boxed in by allies) get a second chance at the end of
        // the turn, once everyone else has moved out of the way.
        List<UnitInstance> deferred = new List<UnitInstance>();

        foreach (UnitInstance enemy in activeEnemies)
        {
            bool wasDeferred = false;

            while (enemy != null && !enemy.IsDead && enemy.actionsRemaining > 0)
            {
                List<UnitInstance> targets = GetActiveTargets();
                UnitInstance target = FindBestTarget(enemy, targets);
                if (target == null) break; // nothing to fight, not a movement problem

                if (enemy.CanAttack(target))
                {
                    enemy.Attack(target);
                    SpendAction(enemy);
                    yield return new WaitForSeconds(actionDelay);
                    continue;
                }

                bool anyTileReachable = HexPathfinder.GetReachableTiles(enemy.currentTile, enemy.movementRange).Any();
                HexTile destination = FindBestDestination(enemy, targets, activeEnemies, enemy.movementRange);

                if (destination == null)
                {
                    if (!anyTileReachable)
                    {
                        // Truly stuck (boxed in), not just "no good tile" —
                        // hold onto remaining actions and retry after others move.
                        deferred.Add(enemy);
                        wasDeferred = true;
                    }
                    break;
                }

                if (!enemy.MoveTo(destination, enemy.movementRange)) break;

                SpendAction(enemy);
                yield return new WaitForSeconds(actionDelay);
            }

            if (wasDeferred)
                yield return null; // let the rest of the turn play out before retrying
        }

        // --- Retry pass for units that were boxed in ---
        foreach (UnitInstance enemy in deferred)
        {
            if (enemy == null || enemy.IsDead || enemy.actionsRemaining <= 0) continue;

            List<UnitInstance> targets = GetActiveTargets();
            UnitInstance target = FindBestTarget(enemy, targets);
            if (target == null) continue;

            if (enemy.CanAttack(target))
            {
                enemy.Attack(target);
                SpendAction(enemy);
                yield return new WaitForSeconds(actionDelay);
                continue;
            }

            // First, try again normally — allies may have cleared a path by now.
            HexTile destination = FindBestDestination(enemy, targets, activeEnemies, enemy.movementRange);
            if (destination != null)
            {
                if (enemy.MoveTo(destination, enemy.movementRange))
                {
                    SpendAction(enemy);
                    yield return new WaitForSeconds(actionDelay);
                }
                continue;
            }

            // Still nothing reachable at normal range — check whether doubling
            // movement range would open up a path. If so, spend all remaining
            // actions on one big push to get out of the clump.
            int doubledRange = enemy.movementRange * 2;
            HexTile desperateDestination = FindBestDestination(enemy, targets, activeEnemies, doubledRange);
            if (desperateDestination != null && enemy.MoveTo(desperateDestination, doubledRange))
            {
                enemy.actionsRemaining = 0; // this move consumed the rest of the turn
                yield return new WaitForSeconds(actionDelay);
            }
            // else: genuinely nowhere to go (fully surrounded) — accept it stands still this turn.
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
            if (unit == null || unit.IsDead || unit.currentTile == null)
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
        List<UnitInstance> activeEnemies,
        int movementRange)
    {
        HexTile bestDestination = null;
        float bestScore = float.MinValue;

        HexTile closestFallback = null;
        int closestFallbackDistance = int.MaxValue;

        List<HexTile> reachable = HexPathfinder.GetReachableTiles(enemy.currentTile, movementRange).ToList();

        foreach (HexTile tile in reachable)
        {
            if (!tile.CanEnter(enemy)) continue;

            float terrainScore = tile.defenseBonus * 20f + tile.attackBonus * 10f;
            float bestTargetScore = float.MinValue;

            foreach (UnitInstance target in targets)
            {
                int distance = HexCoordinates.GetDistance(tile.gridPosition, target.currentTile.gridPosition);

                if (distance < closestFallbackDistance)
                {
                    closestFallbackDistance = distance;
                    closestFallback = tile;
                }

                bool canAttackFromTile = distance <= enemy.attackRange;
                float targetScore = GetTargetScore(enemy, target) - distance * 4f;

                if (canAttackFromTile) targetScore += 1000f;
                if (enemy.attackRange > 1)
                    targetScore -= Mathf.Abs(distance - enemy.attackRange) * 3f;

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

        if (bestDestination == null)
            return closestFallback;

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