using System;
using UnityEngine;

/// <summary>
/// A single unit on the grid (player or enemy). MVP scope: sit on a tile, take
/// damage, attack another unit, die. Movement uses HexPathfinder + PlaceOnTile.
/// </summary>
public class UnitInstance : MonoBehaviour
{
    [Header("Identity")]
    public string unitName = "Unit";
    public bool isEnemy = false;
    public Unit PersistentUnit { get; private set; }

    public UnitFaction Faction;

    [Header("Stats")]
    public int maxHealth = 10;
    public int currentHealth = 10;
    public int attackPower = 2;
    public int attackRange = 1;
    public int defensePower = 1;
    public int movementRange = 3;
    public int healsOthers = 0; // How much this unit heals others (if any)

    [Tooltip("Visual height above the centre of the tile.")]
    public float tileVisualOffset = 0.5f;

    [Header("State")]
    public HexTile currentTile;
    public bool IsDead => currentHealth <= 0;

    public int maxActionsPerTurn = 2;
    public int actionsRemaining = 2;
    public UnitData[] rescuedUnitData = new UnitData[2];
    public event Action<UnitInstance> OnDeath;

    private void Awake()
    {
        //rescuedUnitData[0] = null;//rescuedUnitData[1] = null;
        actionsRemaining = maxActionsPerTurn;
        currentHealth = maxHealth;
    }
    public void Initialize(Unit unit)
    {
        if (unit == null)
        {
            Debug.LogError("UnitInstance: Initialize called with null Unit!");
            return;
        }

        PersistentUnit = unit;
        unitName = unit.UnitName;
        maxHealth = unit.MaxHP;
        currentHealth = maxHealth;
        attackPower = unit.BaseAttack;
        attackRange = unit.AttackRange;
        defensePower = unit.DefensePower;
        movementRange = unit.MoveSpeed;
        healsOthers = unit.HealsOthers;
        Faction = unit.Faction;
    }

    public void Initialize(UnitData archetype)
    {
        if (archetype == null)
        {
            Debug.LogError("UnitInstance: Initialize called with null UnitData!");
            return;
        }

        Initialize(new Unit(archetype));
    }
    // ---------------------------
    // Placement / Movement
    // ---------------------------

    /// Places the unit on a tile without pathing (used for initial spawn).
    public void PlaceOnTile(HexTile tile)
    {
        if (tile == null) return;

        if (currentTile != null)
            currentTile.RemoveUnit();

        currentTile = tile;
        tile.SetUnit(this);
        transform.position = tile.transform.position + Vector3.up * tileVisualOffset;
    }

    /// Moves the unit along a path (e.g. from HexPathfinder.FindPath). MVP: snaps
    /// to the destination tile; swap in movement animation/tweening later.
    public bool MoveTo(HexTile destinationTile, int maxMovementCost = -1)
    {
        if (currentTile == null || destinationTile == null) return false;

        var path = HexPathfinder.FindPath(currentTile, destinationTile, maxMovementCost >= 0 ? maxMovementCost : movementRange);
        if (path == null || path.Count == 0) return false;

        PlaceOnTile(destinationTile);
        return true;
    }

    // ---------------------------
    // Combat
    // ---------------------------

    public bool CanAttack(UnitInstance target)
    {
        if (target == null || target.IsDead || currentTile == null || target.currentTile == null)
            return false;

        int distance = HexCoordinates.GetDistance(currentTile.gridPosition, target.currentTile.gridPosition);
        return distance <= attackRange;
    }

    public void Attack(UnitInstance target)
    {
        if (!CanAttack(target)) return;

        //int attackRoll = UnityEngine.Random.Range(1, 21); // Simulate a d20 roll
        //int defRoll = UnityEngine.Random.Range(1, 21); // Simulate a d20 roll
        int attackRoll = 10;
        int defRoll = 10;

        float terrainBonus = 1f+ (Mathf.Max(currentTile.attackBonus - target.currentTile.defenseBonus, 1f)/5f);
        int flatAttack = Mathf.Max(attackPower - target.defensePower,1);
        
        int damage = flatAttack * Mathf.RoundToInt(terrainBonus * (attackRoll / (float)defRoll));
            //Mathf.RoundToInt(attackRoll + Mathf.Max((attackPower * (1.1f * attackRoll) * (1 + currentTile.attackBonus/5)) - (target.defensePower * (1.05f * defRoll) * (1 + target.currentTile.defenseBonus/5)), 0f));
        Debug.Log($"{unitName} attacks {target.unitName} for {damage} damage! (Attack Roll: {attackRoll}, Defense Roll: {defRoll}, terrainBonus: {terrainBonus})");
        target.TakeDamage(damage);
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);

        if (currentHealth <= 0)
            Die();
    }

    private void Die()
    {
        if (currentTile != null)
        {
            currentTile.RemoveUnit();
            currentTile = null;
        }

        OnDeath?.Invoke(this);
        Destroy(gameObject);
    }

    // ---------------------------
    // Fog hooks (called by HexTile.Reveal / Hide)
    // ---------------------------

    public void OnTileRevealed()
    {
        gameObject.SetActive(true);
    }

    public void OnTileHidden()
    {
        // MVP: leave visible. Hook here later if units should be hidden by fog of war.
    }
    public void OnRescue(UnitData rescuedUnit)
    {
        if (rescuedUnit == null) return;

        // Add the rescued unit to the array if there's space
        for (int i = 0; i < rescuedUnitData.Length; i++)
        {
            if (rescuedUnitData[i] == null)
            {
                rescuedUnitData[i] = rescuedUnit;
                Debug.Log($"{unitName} has rescued {rescuedUnit.UnitName}!");
                return;
            }
        }

        Debug.LogWarning($"{unitName} cannot rescue {rescuedUnit.UnitName}; no space available.");
    }
}
