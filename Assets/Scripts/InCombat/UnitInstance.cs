using System;
using System.Collections;
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
    public int visibilityRange = 3;
    public UnitColorScheme colorScheme = UnitColorScheme.Scheme1;

    [Tooltip("Visual height above the centre of the tile.")]
    public float tileVisualOffset = 0.5f;

    [Header("State")]
    public HexTile currentTile;
    public bool IsDead => currentHealth <= 0;

    public int maxActionsPerTurn = 2;
    public int actionsRemaining = 2;
    public UnitData[] rescuedUnitData = new UnitData[2];
    public event Action<UnitInstance> OnDeath;

    public bool IsExtracted = false;
    public bool IsFortified = false;
    public event Action<UnitInstance> OnStatsChanged;
    private Renderer[] flashRenderers;
    private MaterialPropertyBlock flashPropertyBlock;

    private Coroutine flashRoutine;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    [Header("Damage Feedback")]
    [Tooltip("Damage at or below this percentage of max health is a 'light' hit (flashes once).")]
    [SerializeField] private float lightHitThreshold = 0.15f;
    [Tooltip("Damage at or below this percentage of max health (but above the light threshold) is a 'heavy' hit (flashes twice). Anything above this is 'very heavy' (flashes three times).")]
    [SerializeField] private float heavyHitThreshold = 0.35f;
    [SerializeField] private Color damageFlashColor = Color.red;
    [SerializeField] private float flashOnDuration = 0.08f;
    [SerializeField] private float flashOffDuration = 0.08f;


    private void Awake()
    {
        actionsRemaining = maxActionsPerTurn;
        currentHealth = maxHealth;

        flashRenderers = GetComponentsInChildren<Renderer>(true);
        flashPropertyBlock = new MaterialPropertyBlock();

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
        movementRange = unit.MoveRange;
        healsOthers = unit.HealingPower;
        visibilityRange = unit.VisibilityRange;
        colorScheme = unit.ColorScheme;
        maxActionsPerTurn = Mathf.Max(1, unit.MaxMovementPoints);
        Faction = unit.Faction;
        actionsRemaining = maxActionsPerTurn;
        ApplyColorScheme(unit.Archetype);
    }

    public void NotifyStatsChanged()
    {
        OnStatsChanged?.Invoke(this);
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

    private void ApplyColorScheme(UnitData archetype)
    {
        if (archetype == null) return;

        Material selectedMaterial = colorScheme switch
        {
            UnitColorScheme.Scheme2 => archetype.ColorScheme2,
            UnitColorScheme.Scheme3 => archetype.ColorScheme3,
            _ => archetype.ColorScheme1
        };

        if (selectedMaterial == null) return;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = selectedMaterial;
                continue;
            }

            for (int i = 0; i < materials.Length; i++)
                materials[i] = selectedMaterial;

            renderer.sharedMaterials = materials;
        }
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
        MapManager.Instance?.RevealAroundUnit(this);
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
 
        int healthBefore = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        int damageTaken = healthBefore - currentHealth;
 
        NotifyStatsChanged();
 
        if (damageTaken > 0)
            PlayDamageFlash(damageTaken);
 
        if (currentHealth <= 0)
            Die();
    }

    private void PlayDamageFlash(int damageTaken)
    {
        if (flashRenderers == null || flashRenderers.Length == 0 || !gameObject.activeInHierarchy) return;
 
        float damagePercent = (float)damageTaken / Mathf.Max(1, maxHealth);
        int flashCount = damagePercent <= lightHitThreshold ? 1
                        : damagePercent <= heavyHitThreshold ? 2
                        : 3;
 
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRedRoutine(flashCount));
    }
 
    private IEnumerator FlashRedRoutine(int flashCount)
    {
        for (int i = 0; i < flashCount; i++)
        {
            SetFlashTint(damageFlashColor);
            yield return new WaitForSeconds(flashOnDuration);
 
            SetFlashTint(Color.white);
            yield return new WaitForSeconds(flashOffDuration);
        }
 
        flashRoutine = null;
    }
 
    private void SetFlashTint(Color color)
    {
        foreach (Renderer renderer in flashRenderers)
        {
            if (renderer == null) continue;
 
            // Set both common color property names so this works whether the
            // unit's materials are Built-in (_Color) or URP/HDRP (_BaseColor).
            renderer.GetPropertyBlock(flashPropertyBlock);
            flashPropertyBlock.SetColor(ColorId, color);
            flashPropertyBlock.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(flashPropertyBlock);
        }
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
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = true;
    }

    public void OnTileHidden()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
    }
    public void OnRescue(UnitData rescuedUnit)
    {
        if (rescuedUnit == null || rescuedUnitData == null) return;

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

    /// <summary>
    /// Adds a villager to this unit and parents its visual so it follows the carrier.
    /// </summary>
    public bool Rescue(UnitInstance villager)
    {
        if (villager == null || villager.Faction != UnitFaction.Villager ||
            villager.currentTile == null || rescuedUnitData == null || !CanAttack(villager))
            return false;

        UnitData villagerData = villager.PersistentUnit != null ? villager.PersistentUnit.Archetype : null;
        if (villagerData == null)
        {
            Debug.LogWarning($"{unitName} cannot rescue {villager.unitName}; no UnitData is available.");
            return false;
        }

        int rescueSlot = -1;
        for (int i = 0; i < rescuedUnitData.Length; i++)
        {
            if (rescuedUnitData[i] == null)
            {
                rescueSlot = i;
                break;
            }
        }

        if (rescueSlot < 0)
        {
            Debug.LogWarning($"{unitName} cannot rescue {villager.unitName}; no space available.");
            return false;
        }

        HexTile villagerTile = villager.currentTile;
        villagerTile.RemoveUnit();
        villager.currentTile = null;
        rescuedUnitData[rescueSlot] = villagerData;

        villager.transform.SetParent(transform, false);
        villager.transform.localPosition = Vector3.right * (rescueSlot + 1) * 0.6f;
        villager.transform.localRotation = Quaternion.identity;

        foreach (Collider collider in villager.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        villager.enabled = false;
        Debug.Log($"{unitName} has rescued {villager.unitName}!");
        return true;
    }
}
