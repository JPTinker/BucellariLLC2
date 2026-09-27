using System;
using System.Collections;
using UnityEngine;
 using System.Collections.Generic;

/// <summary>
/// A single unit on the grid (player or enemy). MVP scope: sit on a tile, take
/// damage, attack another unit, die. Movement uses HexPathfinder + PlaceOnTile.
/// </summary>
public class UnitInstance : MonoBehaviour
{
    [Header("Identity")]
    public string unitName = "Unit";
    //public bool isEnemy = false;
    public Unit PersistentUnit { get; private set; }
    public int Experience { get; private set; }
    public int Level { get; private set; } = 1;

    private const int ExperiencePerLevel = 100;
    private const int AttackExperience = 10;
    private const int KillExperience = 20;
    private const int RescueExperience = 25;

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

    public GameObject leftHand;
    public GameObject rightHand;

    public Texture2D ColorScheme1;
    public Texture2D ColorScheme2;
    public Texture2D ColorScheme3;
    public Texture2D ColorScheme4;


    [Header("State")]
    public HexTile currentTile;
    public bool IsDead => currentHealth <= 0;

    public int maxActionsPerTurn = 2;
    public int actionsRemaining = 2;
    public UnitData[] rescuedUnitData = new UnitData[2];
    public event Action<UnitInstance> OnDeath;

    public bool IsExtracted = false;
    public bool IsFortified = false;
    public bool IsRevealed = false;
    public event Action<UnitInstance> OnStatsChanged;
    private Animator _animator;
    private Renderer[] flashRenderers;
    private MaterialPropertyBlock flashPropertyBlock;

    private Coroutine flashRoutine;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int WalkParameter = Animator.StringToHash("Walk");
    private static readonly int AttackParameter = Animator.StringToHash("Attack");
    private static readonly int TakeDamageParameter = Animator.StringToHash("TakeDamage");
    private static readonly int BowParameter = Animator.StringToHash("Bow");
    private static readonly int CrossbowParameter = Animator.StringToHash("Crossbow");
    private static readonly int MageParameter = Animator.StringToHash("Mage");
    private static readonly int MeleHandsParameter = Animator.StringToHash("MeleHands");
    private static readonly int Spawn = Animator.StringToHash("Spawn");
    [Header("Damage Feedback")]
    [Tooltip("Damage at or below this percentage of max health is a 'light' hit (flashes once).")]
    [SerializeField] private float lightHitThreshold = 0.15f;
    [Tooltip("Damage at or below this percentage of max health (but above the light threshold) is a 'heavy' hit (flashes twice). Anything above this is 'very heavy' (flashes three times).")]
    [SerializeField] private float heavyHitThreshold = 0.35f;
    [SerializeField] private Color damageFlashColor = Color.red;
    [SerializeField] private float flashOnDuration = 0.08f;
    [SerializeField] private float flashOffDuration = 0.08f;
    private Coroutine movementCoroutine;

    private void Awake()
    {
        actionsRemaining = maxActionsPerTurn;
        currentHealth = maxHealth;

        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>(true);

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
        Experience = unit.Experience;
        Level = unit.Level;
        unitName = unit.UnitName;
        maxHealth = unit.MaxHP;
        // Carry damage over between battles rather than always spawning at full
        // HP - Rest (Campaign Map) is what heals this back up, not a fresh spawn.
        currentHealth = unit.CurrentHP > 0 ? Mathf.Min(unit.CurrentHP, maxHealth) : maxHealth;
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
        ConfigureAnimatorStyle(unit.Archetype);
        if (unit.WeaponPrefab != null && rightHand != null){
            GameObject.Instantiate(unit.WeaponPrefab, rightHand.transform, false);
        }

        if (unit.EquipmentPrefab != null && leftHand != null)
        {
            GameObject.Instantiate(unit.EquipmentPrefab, leftHand.transform, false);
        }
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

    public void AddExperience(int amount)
    {
        if (amount <= 0 || PersistentUnit == null) return;

        Experience += amount;
        PersistentUnit.Experience = Experience;

        while (Experience >= ExperiencePerLevel)
        {
            Experience -= ExperiencePerLevel;
            PersistentUnit.Experience = Experience;
            LevelUp();
        }
    }

    private void LevelUp()
    {
        PersistentUnit.ApplyLevelUp();
        Level = PersistentUnit.Level;
        maxHealth = PersistentUnit.MaxHP;
        attackPower = PersistentUnit.BaseAttack;
        defensePower = PersistentUnit.DefensePower;
        healsOthers = PersistentUnit.HealingPower;
        currentHealth = Mathf.Min(maxHealth, currentHealth + 5);
        NotifyStatsChanged();
        Debug.Log($"{unitName} reached level {Level}.");
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
        transform.position = tile.transform.position + Vector3.up * tile.heightOffset;
        if (!IsRevealed && tile.isRevealed)
            OnTileRevealed();
        else if (IsRevealed && !tile.isRevealed)
            OnTileHidden();
    }

    private IEnumerator MoveAlongPathRoutine(List<HexTile> path)
    {
        // Free up the starting tile immediately so other units/pathfinding don't treat it as blocked
        if (currentTile != null)
        {
            currentTile.RemoveUnit();
        }

        // Traverse each tile node in the path sequentially
        for (int i = 1; i < path.Count; i++)
        {
            HexTile nextTile = path[i];
            Vector3 startPos = transform.position;
            Vector3 targetPos = nextTile.transform.position + Vector3.up * nextTile.heightOffset;

            // Rotate smoothly or instantly towards the next waypoint
            Vector3 moveDirection = targetPos - startPos;
            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(moveDirection);
            }

            // Interpolate position over time (adjust speed multiplier as needed, e.g., 6f)
            float moveSpeed = 6f;
            float journeyLength = Vector3.Distance(startPos, targetPos);
            float startTime = Time.time;

            if (journeyLength > 0.001f)
            {
                float fractionTraveled = 0f;
                while (fractionTraveled < 1f)
                {
                    float distCovered = (Time.time - startTime) * moveSpeed;
                    fractionTraveled = distCovered / journeyLength;
                    transform.position = Vector3.Lerp(startPos, targetPos, Mathf.Clamp01(fractionTraveled));
                    yield return null;
                }
            }

            transform.position = targetPos;
        }

        // Finalize arrival on the destination tile
        HexTile destinationTile = path[path.Count - 1];
        currentTile = destinationTile;
        destinationTile.SetUnit(this);

        // Update map visibility and fog of war
        MapManager.Instance?.RevealAroundUnit(this);

        if (!IsRevealed && destinationTile.isRevealed)
            OnTileRevealed();
        else if (IsRevealed && !destinationTile.isRevealed)
            OnTileHidden();

        movementCoroutine = null;
    }

    /// Moves the unit along a path (e.g. from HexPathfinder.FindPath). MVP: snaps
    /// to the destination tile; swap in movement animation/tweening later.
    public bool MoveTo(HexTile destinationTile, int maxMovementCost = -1)
    {
        if (currentTile == null || destinationTile == null) return false;

        var path = HexPathfinder.FindPath(currentTile, destinationTile, maxMovementCost >= 0 ? maxMovementCost : movementRange);
        if (path == null || path.Count < 2) return false;

        Debug.Log($"{unitName} moving from {currentTile.gridPosition} to {destinationTile.gridPosition} via path of length {path.Count}");

        // Stop any active movement coroutine if a new move is triggered abruptly
        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
        }

        PlayAnimatorAction(WalkParameter, "Walking_A");

        // Start the smooth movement coroutine
        movementCoroutine = StartCoroutine(MoveAlongPathRoutine(path));

        IsFortified = false;
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

    public bool Attack(UnitInstance target)
    {
        if (!CanAttack(target)) return false;
        PlayAnimatorAction(AttackParameter, "Attack");

        int damage = CalculateAttackDamage(target, currentTile);
        Debug.Log($"{unitName} attacks {target.unitName} for {damage} damage!");
        target.TakeDamage(damage);
        if (Faction == UnitFaction.Player && target.Faction == UnitFaction.Enemy)
        {
            AddExperience(AttackExperience);
            if (target.IsDead)
                AddExperience(KillExperience);
        }
        return true;
    }
    /// Raw damage an attack on target would deal from attackFrom, before the
    /// target's own mitigation. Shared by Attack() and the enemy AI's planning.
    public int CalculateAttackDamage(UnitInstance target, HexTile attackFrom)
    {
        //int attackRoll = UnityEngine.Random.Range(1, 21); // Simulate a d20 roll
        //int defRoll = UnityEngine.Random.Range(1, 21); // Simulate a d20 roll
        int attackRoll = 10;
        int defRoll = 10;

        int attackBonus = attackFrom != null ? attackFrom.attackBonus : 0;
        int defenseBonus = target.currentTile != null ? target.currentTile.defenseBonus : 0;
        float terrainBonus = 1f + (Mathf.Max(attackBonus - defenseBonus, 1f) / 5f);
        int flatAttack = Mathf.Max(attackPower - target.defensePower, 1);

        if (IsFortified == true)
        {
            flatAttack += 3;
        }

        return flatAttack * Mathf.RoundToInt(terrainBonus * (attackRoll / (float)defRoll));
            //Mathf.RoundToInt(attackRoll + Mathf.Max((attackPower * (1.1f * attackRoll) * (1 + currentTile.attackBonus/5)) - (target.defensePower * (1.05f * defRoll) * (1 + target.currentTile.defenseBonus/5)), 0f));
    }

    /// Health target would actually lose if attacked from attackFrom (null = current tile).
    public int PredictDamage(UnitInstance target, HexTile attackFrom = null)
    {
        return target.MitigateDamage(CalculateAttackDamage(target, attackFrom != null ? attackFrom : currentTile));
    }

    private int MitigateDamage(int amount)
    {
        // Fortified units shrug off 3 damage, but always take at least 1.
        return IsFortified ? Mathf.Max(1, amount - 3) : amount;
    }

    public bool IsMoving => movementCoroutine != null;

    public void playSpawnAnimation()
    {
        PlayAnimatorAction(Spawn, "Spawn");
    }
    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        amount = MitigateDamage(amount);

        int healthBefore = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        int damageTaken = healthBefore - currentHealth;
 
        NotifyStatsChanged();

        if (damageTaken > 0)
        {
            PlayAnimatorAction(TakeDamageParameter, "Damaged");
            PlayDamageFlash(damageTaken);
        }
 
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

    private void ConfigureAnimatorStyle(UnitData archetype)
    {
        if (_animator == null || archetype == null) return;

        UnitAnimationStyle style = archetype.AnimationStyle;
        _animator.SetBool(BowParameter, style == UnitAnimationStyle.Bow);
        _animator.SetBool(CrossbowParameter, style == UnitAnimationStyle.Crossbow);
        _animator.SetBool(MageParameter, style == UnitAnimationStyle.Mage);
        _animator.SetInteger(MeleHandsParameter, style switch
        {
            UnitAnimationStyle.MeleeOneHanded => 1,
            UnitAnimationStyle.MeleeTwoHanded => 2,
            _ => 0
        });
    }

    private void PlayAnimatorAction(int parameter, string stateName)
    {
        if (_animator == null || !_animator.isActiveAndEnabled) return;

        _animator.SetBool(parameter, true);
        StartCoroutine(ResetAnimatorActionAfterStateStarts(parameter, stateName));
    }

    private IEnumerator ResetAnimatorActionAfterStateStarts(int parameter, string stateName)
    {
        const float stateEntryTimeout = 5f;
        float elapsed = 0f;

        while (_animator != null && _animator.isActiveAndEnabled && elapsed < stateEntryTimeout)
        {
            if (_animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
            {
                _animator.SetBool(parameter, false);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_animator != null)
            _animator.SetBool(parameter, false);
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
        IsRevealed = true;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = true;
    }

    public void OnTileHidden()
    {
        IsRevealed = false;
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
        if (Faction == UnitFaction.Player)
            AddExperience(RescueExperience);
        Debug.Log($"{unitName} has rescued {villager.unitName}!");
        return true;
    }
    public void onHeal()
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + healsOthers);
        NotifyStatsChanged();
        CombatManager.Instance?.TakeAction(this);
    }
    public void onFortify()
    {
        IsFortified = true;
        NotifyStatsChanged();
        CombatManager.Instance?.TakeAction(this);
    }
    public void onScout()
    {
        int temp = visibilityRange;
        visibilityRange = visibilityRange + 2;
        MapManager.Instance?.RevealAroundUnit(this);
        visibilityRange = temp;
        CombatManager.Instance?.TakeAction(this);
    }
}
