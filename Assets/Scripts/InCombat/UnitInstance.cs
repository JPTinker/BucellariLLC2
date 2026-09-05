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

    [Header("Stats")]
    public int maxHealth = 10;
    public int currentHealth = 10;
    public int attackPower = 2;
    public int attackRange = 1;
    public int defensePower = 1;
    public int movementRange = 3;
    [Tooltip("Visual height above the centre of the tile.")]
    public float tileVisualOffset = 0.5f;

    [Header("State")]
    public HexTile currentTile;
    public bool IsDead => currentHealth <= 0;

    public int maxActionsPerTurn = 2;
    public int actionsRemaining = 2;

    public event Action<UnitInstance> OnDeath;

    private void Awake()
    {
        actionsRemaining = maxActionsPerTurn;
        currentHealth = maxHealth;
    }
    public void Initialize(UnitData unitData)
    {
        if (unitData == null)
        {
            Debug.LogError("UnitInstance: Initialize called with null UnitData!");
            return;
        }

        unitName = unitData.UnitName;
        maxHealth = unitData.MaxHP;
        currentHealth = maxHealth;
        attackPower = unitData.BaseAttack;
        attackRange = unitData.AttackRange;
        movementRange = unitData.MoveSpeed;
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

        int attackRoll = UnityEngine.Random.Range(1, 21); // Simulate a d20 roll
        int defRoll = UnityEngine.Random.Range(1, 21); // Simulate a d20 roll


        int damage = 
            Mathf.RoundToInt(attackRoll + Mathf.Max((attackPower * (1.1f * attackRoll) * currentTile.attackBonus) - (target.defensePower * (1.05f * defRoll) * target.currentTile.defenseBonus), 0f));
        Debug.Log($"{unitName} attacks {target.unitName} for {damage} damage! (Attack Roll: {attackRoll}, Defense Roll: {defRoll})");
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
}
