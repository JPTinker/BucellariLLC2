using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Horde AI. Each enemy searches every move/attack combination for its turn and
/// scores it by the damage dealt now plus the damage its end position sets up over
/// the next few turns. Enemies commit one at a time and the battlefield is re-read
/// after each, so later units react to kills and freed-up tiles.
/// </summary>
public class EnemyAIController : MonoBehaviour
{
    [SerializeField] private float actionDelay = 0.1f;
    [Tooltip("Safety cap on waiting for a move animation to finish.")]
    [SerializeField] private float moveTimeout = 5f;
    [SerializeField] private bool logDecisions = true;

    [Header("Lookahead")]
    [Tooltip("Future enemy turns scored from a unit's end position, on top of the current turn.")]
    [SerializeField, Range(0, 4)] private int lookaheadTurns = 2;
    [Tooltip("Value of each future turn relative to the one before. Lower = more impatient.")]
    [SerializeField, Range(0f, 1f)] private float futureDiscount = 0.6f;
    [Tooltip("Tiles expanded per intermediate move when searching multi-action plans.")]
    [SerializeField, Min(1)] private int moveCandidatesPerStep = 6;

    [Header("Target Priorities")]
    [SerializeField] private float playerWeight = 1f;
    [SerializeField] private float villagerWeight = 1.25f;
    [Tooltip("Value of hitting a wall relative to a player unit. Below 1 = enemies prefer units but chew through walls in their way.")]
    [SerializeField] private float structureWeight = 0.5f;
    [Tooltip("Value for finishing a unit off, on top of the damage dealt.")]
    [SerializeField] private float killBonus = 60f;
    [Tooltip("Extra kill value per point of the victim's attack power (removing threats).")]
    [SerializeField] private float killThreatBonus = 4f;
    [Tooltip("Damage against a wounded target counts up to (1 + this) times more, so the horde focuses fire.")]
    [SerializeField] private float focusFireWeight = 1f;

    [Header("Positioning")]
    [Tooltip("Penalty per movement point between a unit and its nearest target. Keeps distant units marching in.")]
    [SerializeField] private float approachWeight = 0.15f;
    [SerializeField] private float defenseTerrainWeight = 0.5f;
    [Tooltip("Penalty for ranged units standing adjacent to a target (leave those tiles for melee).")]
    [SerializeField] private float rangedAdjacentPenalty = 2f;
    [Tooltip("Extra path cost for routing through a tile an ally stands on. Spreads the horde around targets.")]
    [SerializeField] private int allyPassCost = 2;

    private const float ScoreEpsilon = 0.01f;

    private bool turnInProgress;

    private readonly struct PlannedAction
    {
        public readonly HexTile Destination;
        public readonly UnitInstance Target;

        private PlannedAction(HexTile destination, UnitInstance target)
        {
            Destination = destination;
            Target = target;
        }

        public static PlannedAction Move(HexTile tile) => new PlannedAction(tile, null);
        public static PlannedAction Attack(UnitInstance target) => new PlannedAction(null, target);

        public override string ToString() =>
            Target != null ? $"attack {Target.unitName}" : $"move {Destination.gridPosition}";
    }

    private sealed class TurnPlan
    {
        public UnitInstance Unit;
        public List<PlannedAction> Actions = new List<PlannedAction>();
        public float Value = float.MinValue;  // this turn's damage + end-position value
        public float ImmediateValue;          // this turn's damage only
        public int FrontlineCost;             // how far the unit currently is from any target

        public bool Attacks => ImmediateValue > 0f;
        public bool Acts => Actions.Count > 0;

        /// Attackers strike first (biggest hits first), then movers front-to-back so
        /// the front line clears space for the units behind it, then idle units.
        public bool ExecutesBefore(TurnPlan other)
        {
            if (Attacks != other.Attacks) return Attacks;
            if (Attacks) return ImmediateValue > other.ImmediateValue;
            if (Acts != other.Acts) return Acts;
            if (FrontlineCost != other.FrontlineCost) return FrontlineCost < other.FrontlineCost;
            return Value > other.Value;
        }
    }

    /// Mutable search state for planning a single unit's turn.
    private sealed class PlanSearch
    {
        public UnitInstance Unit;
        public EnemyBattlefieldState Battlefield;
        public readonly Dictionary<UnitInstance, int> DamageDealt = new Dictionary<UnitInstance, int>();
        public readonly List<PlannedAction> Actions = new List<PlannedAction>();
        public TurnPlan Best;

        public int RemainingHealth(UnitInstance target) =>
            target.currentHealth - (DamageDealt.TryGetValue(target, out int dealt) ? dealt : 0);

        /// A tile is blocked unless it is empty, holds this unit, or holds a target killed earlier in this plan.
        public bool IsBlocked(HexTile tile)
        {
            UnitInstance occupant = tile.occupyingUnit;
            if (occupant == null || occupant == Unit) return false;
            return !DamageDealt.ContainsKey(occupant) || RemainingHealth(occupant) > 0;
        }
    }

    public void ExecuteTurn(Action onComplete)
    {
        if (turnInProgress) return;
        StartCoroutine(ExecuteTurnRoutine(onComplete));
    }

    private IEnumerator ExecuteTurnRoutine(Action onComplete)
    {
        turnInProgress = true;

        List<UnitInstance> pending = new List<UnitInstance>();
        foreach (UnitInstance unit in FindObjectsByType<UnitInstance>())
        {
            if (IsActiveEnemy(unit))
            {
                unit.actionsRemaining = unit.maxActionsPerTurn;
                pending.Add(unit);
            }
        }

        // A unit whose plan got interrupted (e.g. a path closed) gets one replan.
        HashSet<UnitInstance> retried = new HashSet<UnitInstance>();

        while (true)
        {
            pending.RemoveAll(unit => !IsActiveEnemy(unit) || unit.actionsRemaining <= 0);
            if (pending.Count == 0) break;

            EnemyBattlefieldState battlefield = EnemyBattlefieldState.Build(allyPassCost);
            if (battlefield.Targets.Count == 0) break;

            TurnPlan chosen = null;
            foreach (UnitInstance enemy in pending)
            {
                TurnPlan plan = PlanTurn(enemy, battlefield);
                if (chosen == null || plan.ExecutesBefore(chosen))
                    chosen = plan;
            }

            pending.Remove(chosen.Unit);

            if (logDecisions)
            {
                string actions = chosen.Acts ? string.Join(", ", chosen.Actions) : "hold";
                Debug.Log($"EnemyAI: {chosen.Unit.unitName} -> {actions} (value {chosen.Value:0.0}, immediate {chosen.ImmediateValue:0.0})");
            }

            bool completed = true;
            yield return StartCoroutine(ExecutePlan(chosen, result => completed = result));

            if (!completed && IsActiveEnemy(chosen.Unit) && chosen.Unit.actionsRemaining > 0 && retried.Add(chosen.Unit))
                pending.Add(chosen.Unit);
        }

        turnInProgress = false;
        onComplete?.Invoke();
    }

    //---------------------------------------
    // Execution
    //---------------------------------------

    private IEnumerator ExecutePlan(TurnPlan plan, Action<bool> onFinished)
    {
        UnitInstance unit = plan.Unit;

        foreach (PlannedAction action in plan.Actions)
        {
            if (!IsActiveEnemy(unit) || unit.actionsRemaining <= 0)
            {
                onFinished(false);
                yield break;
            }

            if (action.Target != null)
            {
                if (!unit.Attack(action.Target))
                {
                    onFinished(false);
                    yield break;
                }

                SpendAction(unit);
                yield return new WaitForSeconds(actionDelay);
            }
            else
            {
                if (!unit.MoveTo(action.Destination, unit.movementRange))
                {
                    onFinished(false);
                    yield break;
                }

                SpendAction(unit);

                // The unit only occupies its destination once it arrives, so the
                // next decision must wait or it plans from a stale position.
                float waited = 0f;
                while (unit != null && unit.IsMoving && waited < moveTimeout)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }
        }

        onFinished(true);
    }

    //---------------------------------------
    // Planning
    //---------------------------------------

    private TurnPlan PlanTurn(UnitInstance enemy, EnemyBattlefieldState battlefield)
    {
        PlanSearch search = new PlanSearch { Unit = enemy, Battlefield = battlefield };
        SearchActions(search, enemy.currentTile, enemy.actionsRemaining, 0f);

        TurnPlan plan = search.Best;
        plan.FrontlineCost = EnemyBattlefieldState.Unreachable;
        foreach (UnitInstance target in battlefield.Targets)
        {
            int cost = battlefield.GetApproachCost(target, enemy.attackRange, enemy.currentTile);
            plan.FrontlineCost = Mathf.Min(plan.FrontlineCost, cost);
        }

        return plan;
    }

    /// Depth-first search over this unit's remaining actions. Every node is a valid
    /// plan (a unit may stop early); shorter plans win ties so units don't fidget.
    private void SearchActions(PlanSearch search, HexTile position, int actionsLeft, float immediateValue)
    {
        UnitInstance unit = search.Unit;

        float value = immediateValue + PositionValue(search, position);
        if (search.Best == null || value > search.Best.Value + ScoreEpsilon)
        {
            search.Best = new TurnPlan
            {
                Unit = unit,
                Actions = new List<PlannedAction>(search.Actions),
                Value = value,
                ImmediateValue = immediateValue,
            };
        }

        if (actionsLeft <= 0) return;

        // --- Attack from here ---
        foreach (UnitInstance target in search.Battlefield.Targets)
        {
            int remaining = search.RemainingHealth(target);
            if (remaining <= 0 || !InRange(unit, position, target)) continue;

            int dealt = Mathf.Min(unit.PredictDamage(target, position), remaining);
            float attackValue = AttackValue(target, dealt, remaining);

            int dealtBefore = search.DamageDealt.TryGetValue(target, out int previous) ? previous : 0;
            search.DamageDealt[target] = dealtBefore + dealt;
            search.Actions.Add(PlannedAction.Attack(target));

            SearchActions(search, position, actionsLeft - 1, immediateValue + attackValue);

            search.Actions.RemoveAt(search.Actions.Count - 1);
            if (dealtBefore == 0)
                search.DamageDealt.Remove(target);
            else
                search.DamageDealt[target] = dealtBefore;
        }

        // --- Move somewhere ---
        Dictionary<HexTile, int> reachable =
            EnemyBattlefieldState.GetReachable(position, unit.movementRange, search.IsBlocked);

        List<HexTile> candidates = actionsLeft == 1
            // Last action: the move ends the turn, so every tile is just a leaf.
            ? reachable.Keys.ToList()
            : PickMoveCandidates(search, reachable.Keys);

        foreach (HexTile tile in candidates)
        {
            if (tile == position || search.IsBlocked(tile)) continue;

            search.Actions.Add(PlannedAction.Move(tile));
            SearchActions(search, tile, actionsLeft - 1, immediateValue);
            search.Actions.RemoveAt(search.Actions.Count - 1);
        }
    }

    /// Picks the most promising intermediate tiles: the best places to attack
    /// from, plus the best places to keep advancing from.
    private List<HexTile> PickMoveCandidates(PlanSearch search, IEnumerable<HexTile> reachable)
    {
        UnitInstance unit = search.Unit;
        List<(HexTile tile, float score)> attackTiles = new List<(HexTile, float)>();
        List<(HexTile tile, float score)> otherTiles = new List<(HexTile, float)>();

        foreach (HexTile tile in reachable)
        {
            float bestAttack = float.MinValue;
            foreach (UnitInstance target in search.Battlefield.Targets)
            {
                int remaining = search.RemainingHealth(target);
                if (remaining <= 0 || !InRange(unit, tile, target)) continue;

                int dealt = Mathf.Min(unit.PredictDamage(target, tile), remaining);
                bestAttack = Mathf.Max(bestAttack, AttackValue(target, dealt, remaining));
            }

            float positionValue = PositionValue(search, tile);
            if (bestAttack > float.MinValue)
                attackTiles.Add((tile, bestAttack + positionValue));
            else
                otherTiles.Add((tile, positionValue));
        }

        return attackTiles.OrderByDescending(entry => entry.score).Take(moveCandidatesPerStep)
            .Concat(otherTiles.OrderByDescending(entry => entry.score).Take(moveCandidatesPerStep))
            .Select(entry => entry.tile)
            .ToList();
    }

    //---------------------------------------
    // Scoring
    //---------------------------------------

    /// Value of ending the turn on this tile: the best damage the unit could
    /// pile onto a single target over the lookahead window, plus positioning.
    private float PositionValue(PlanSearch search, HexTile tile)
    {
        UnitInstance unit = search.Unit;
        EnemyBattlefieldState battlefield = search.Battlefield;

        float bestThreat = 0f;
        int nearestCost = EnemyBattlefieldState.Unreachable;
        bool adjacentToTarget = false;

        foreach (UnitInstance target in battlefield.Targets)
        {
            int remaining = search.RemainingHealth(target);
            if (remaining <= 0) continue;

            if (HexCoordinates.GetDistance(tile.gridPosition, target.currentTile.gridPosition) <= 1)
                adjacentToTarget = true;

            int approachCost = battlefield.GetApproachCost(target, unit.attackRange, tile);
            if (approachCost == EnemyBattlefieldState.Unreachable) continue;

            nearestCost = Mathf.Min(nearestCost, approachCost);
            bestThreat = Mathf.Max(bestThreat, FutureDamageValue(unit, tile, target, remaining, approachCost));
        }

        float value = bestThreat;
        if (nearestCost != EnemyBattlefieldState.Unreachable)
            value -= approachWeight * nearestCost;

        value += defenseTerrainWeight * tile.defenseBonus;
        if (unit.attackRange > 1 && adjacentToTarget)
            value -= rangedAdjacentPenalty;

        return value;
    }

    /// Simulates the next lookaheadTurns enemy turns against one target, assuming
    /// it stays put: spend actions walking in, then attack until it dies.
    private float FutureDamageValue(UnitInstance unit, HexTile from, UnitInstance target, int remainingHealth, int approachCost)
    {
        int movesNeeded = approachCost == 0 ? 0 : Mathf.CeilToInt(approachCost / (float)Mathf.Max(1, unit.movementRange));
        int damagePerHit = unit.PredictDamage(target, from);
        int actionsPerTurn = Mathf.Max(1, unit.maxActionsPerTurn);

        float value = 0f;
        float turnWeight = 1f;

        for (int turn = 0; turn < lookaheadTurns && remainingHealth > 0; turn++)
        {
            turnWeight *= futureDiscount;

            for (int action = 0; action < actionsPerTurn && remainingHealth > 0; action++)
            {
                if (movesNeeded > 0)
                {
                    movesNeeded--;
                    continue;
                }

                int dealt = Mathf.Min(damagePerHit, remainingHealth);
                value += turnWeight * AttackValue(target, dealt, remainingHealth);
                remainingHealth -= dealt;
            }
        }

        return value;
    }

    private float AttackValue(UnitInstance target, int dealt, int remainingHealth)
    {
        if (dealt <= 0) return 0f;

        float healthRatio = Mathf.Clamp01(remainingHealth / (float)Mathf.Max(1, target.maxHealth));
        float value = dealt * (1f + focusFireWeight * (1f - healthRatio));

        if (dealt >= remainingHealth)
            value += killBonus + killThreatBonus * target.attackPower;

        float weight = target.Faction switch
        {
            UnitFaction.Villager => villagerWeight,
            UnitFaction.Structure => structureWeight,
            _ => playerWeight
        };
        return value * weight;
    }

    //---------------------------------------
    // Helpers
    //---------------------------------------

    private static bool InRange(UnitInstance unit, HexTile from, UnitInstance target)
    {
        return HexCoordinates.GetDistance(from.gridPosition, target.currentTile.gridPosition) <= unit.attackRange;
    }

    private static bool IsActiveEnemy(UnitInstance unit)
    {
        return EnemyBattlefieldState.IsOnField(unit) && unit.Faction == UnitFaction.Enemy;
    }

    private static void SpendAction(UnitInstance unit)
    {
        unit.actionsRemaining = Mathf.Max(0, unit.actionsRemaining - 1);
    }
}
