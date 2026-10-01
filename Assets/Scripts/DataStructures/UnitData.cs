using UnityEngine;
using System;

[CreateAssetMenu(fileName = "NewUnitData", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("Identity")]
    public string UnitID;
    public string UnitName;
    [TextArea(2, 4)]
    public string Description;
    public Sprite UnitIcon;          // For UI / Roster list
    public GameObject ModelPrefab;    // World space visual / Sprite Prefab
    public GameObject[] weaponPrefabs; // Array of equipment prefabs for the unit
    public GameObject[] equipmentPrefabs; // Array of equipment prefabs for the unit

    [Header("Visual Style")]
    public UnitColorScheme ColorScheme = UnitColorScheme.Scheme1;
    public Material ColorScheme1;
    public Material ColorScheme2;
    public Material ColorScheme3;

    [Header("Base Combat Stats")]
    public int MaxHP = 100;
    public int BaseAttack = 15;
    public int AttackRange = 1;       // 1 = Melee, 2+ = Ranged
    public int MoveSpeed = 3;         // Tiles per turn
    public int MaxMovementPoints = 2;
    public int VisibilityRange = 3;
    public int defensePower = 1;
    [Header("Faction & Classification")]
    public UnitFaction Faction;
    [Header("Animation")]
    [Tooltip("Selects the matching weapon-style branch in the unit animator.")]
    public UnitAnimationStyle AnimationStyle;
    [Header("Ranged Attack")]
    [Tooltip("Spawned and flown at the target on every attack (arrow, magic missile). Leave empty for melee units.")]
    public Projectile ProjectilePrefab;
    [Tooltip("Seconds after the attack animation starts before the projectile is released - tune to the bow/cast release frame.")]
    public float ProjectileLaunchDelay = 0.3f;
}

public enum UnitFaction
{
    Player,
    Enemy,
    Neutral,
    Villager,
    /// <summary>Player-built obstacles (walls). Enemies can attack them; they never count toward win/loss.</summary>
    Structure
}

public enum UnitAnimationStyle
{
    None,
    Bow,
    Crossbow,
    MeleeOneHanded,
    MeleeTwoHanded,
    Mage
}

public enum UnitColorScheme
{
    Scheme1,
    Scheme2,
    Scheme3
}

public enum UnitRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Mythical
}

/// <summary>
/// Per-tier recruit odds, level-up gains and UI colors. Rarity is rolled once
/// per recruit; the rarer the unit, the more it gains every level.
/// </summary>
public static class UnitRarityTable
{
    public struct Tier
    {
        public int RollWeight;
        public int AttackGain;
        public int DefenseGain;
        public int HpGain;
        public float HpGainChance;
        public Color Color;

        public Tier(int rollWeight, int attackGain, int defenseGain, int hpGain, float hpGainChance, Color color)
        {
            RollWeight = rollWeight;
            AttackGain = attackGain;
            DefenseGain = defenseGain;
            HpGain = hpGain;
            HpGainChance = hpGainChance;
            Color = color;
        }
    }

    // Indexed by (int)UnitRarity.
    private static readonly Tier[] Tiers =
    {
        new Tier(50, 1, 1, 5,  0.5f, new Color(0.75f, 0.78f, 0.82f)), // Common
        new Tier(25, 1, 1, 5,  1f,   new Color(0.40f, 0.85f, 0.45f)), // Uncommon
        new Tier(13, 2, 1, 5,  1f,   new Color(0.30f, 0.60f, 1.00f)), // Rare
        new Tier(7,  2, 2, 10, 1f,   new Color(0.70f, 0.40f, 0.95f)), // Epic
        new Tier(4,  3, 2, 10, 1f,   new Color(1.00f, 0.60f, 0.15f)), // Legendary
        new Tier(1,  3, 3, 15, 1f,   new Color(1.00f, 0.85f, 0.30f)), // Mythical
    };

    public static Tier Get(UnitRarity rarity) => Tiers[(int)rarity];

    public static Color GetColor(UnitRarity rarity) => Get(rarity).Color;

    public static string GetDisplayName(UnitRarity rarity) => rarity.ToString().ToUpperInvariant();

    /// <summary>USS modifier class for this tier, e.g. "rarity-epic".</summary>
    public static string GetUssClass(UnitRarity rarity) => "rarity-" + rarity.ToString().ToLowerInvariant();

    public static UnitRarity Roll()
    {
        int total = 0;
        foreach (var tier in Tiers) total += tier.RollWeight;

        int roll = UnityEngine.Random.Range(0, total);
        for (int i = 0; i < Tiers.Length; i++)
        {
            roll -= Tiers[i].RollWeight;
            if (roll < 0) return (UnitRarity)i;
        }
        return UnitRarity.Common;
    }
}

/// <summary>
/// A unit owned by the player. UnitData is the immutable archetype/template;
/// this class stores the individual's progression and persistent stats.
/// </summary>
[Serializable]
public class Unit
{
    [Header("Identity")]
    public string InstanceID;
    public string UnitID;
    public string UnitName;
    public int Level = 1;
    public int Experience;
    [Tooltip("Rolled once on recruitment. Higher tiers gain more per level; Mythical units never take wounds.")]
    public UnitRarity Rarity;

    [Header("Scars of Battle")]
    [Tooltip("Permanent negative levels - one is gained each time this unit takes WoundDamageFraction of its max HP in damage.")]
    public int Wounds;
    [Tooltip("Damage taken since the last wound. Carries over between battles.")]
    public int DamageTowardNextWound;

    [Header("Persistent Stats")]
    public int MaxHP;
    public int CurrentHP;
    public int BaseAttack;
    public int AttackRange;
    public int MoveRange;
    public int DefensePower;
    public int VisibilityRange;
    public int MaxMovementPoints;
    public UnitColorScheme ColorScheme;
    public UnitFaction Faction;

    public GameObject WeaponPrefab;
    public GameObject EquipmentPrefab;

    [Header("Archetype Reference")]
    public UnitData Archetype;

    public Sprite UnitIcon => Archetype != null ? Archetype.UnitIcon : null;

    public Unit(UnitData archetype) : this(archetype, UnitRarity.Common) { }

    public Unit(UnitData archetype, UnitRarity rarity)
    {
        if (archetype == null)
            throw new ArgumentNullException(nameof(archetype));

        InstanceID = Guid.NewGuid().ToString("N");
        Rarity = rarity;
        Archetype = archetype;
        UnitID = archetype.UnitID;
        UnitName = archetype.UnitName;
        MaxHP = archetype.MaxHP;
        CurrentHP = archetype.MaxHP;
        BaseAttack = archetype.BaseAttack;
        AttackRange = archetype.AttackRange;
        MoveRange = archetype.MoveSpeed;
        MaxMovementPoints = archetype.MaxMovementPoints;
        VisibilityRange = archetype.VisibilityRange;
        DefensePower = archetype.defensePower;
        ColorScheme = archetype.ColorScheme;
        Faction = archetype.Faction;
        WeaponPrefab = archetype.weaponPrefabs != null && archetype.weaponPrefabs.Length > 0 ? archetype.weaponPrefabs[0] : null;
        EquipmentPrefab = archetype.equipmentPrefabs != null && archetype.equipmentPrefabs.Length > 0 ? archetype.equipmentPrefabs[0] : null;
        
    }

    /// <summary>Share of max HP a unit must lose (cumulatively) to gain one wound.</summary>
    public const float WoundDamageFraction = 0.10f;

    public int WoundThreshold => Mathf.Max(1, Mathf.CeilToInt(MaxHP * WoundDamageFraction));

    /// <summary>Mythical units shrug off the scars of battle entirely.</summary>
    public bool IgnoresWounds => Rarity == UnitRarity.Mythical;

    public void ApplyLevelUp()
    {
        var tier = UnitRarityTable.Get(Rarity);
        Level++;
        BaseAttack += tier.AttackGain;
        DefensePower += tier.DefenseGain;

        if (UnityEngine.Random.value < tier.HpGainChance)
            MaxHP += tier.HpGain;
    }

    /// <summary>
    /// Tallies damage toward the next wound and applies every wound it earns.
    /// Returns how many wounds were gained.
    /// </summary>
    public int RecordDamage(int damage)
    {
        if (damage <= 0 || IgnoresWounds) return 0;

        DamageTowardNextWound += damage;
        int woundsGained = 0;
        while (DamageTowardNextWound >= WoundThreshold)
        {
            DamageTowardNextWound -= WoundThreshold;
            ApplyWound();
            woundsGained++;
        }
        return woundsGained;
    }

    /// <summary>A wound is a negative level - the mirror image of ApplyLevelUp().</summary>
    public void ApplyWound()
    {
        Wounds++;
        BaseAttack = Mathf.Max(1, BaseAttack - 1);
        DefensePower = Mathf.Max(0, DefensePower - 1);

        if (UnityEngine.Random.value < 0.5f)
            MaxHP = Mathf.Max(1, MaxHP - 5);

        CurrentHP = Mathf.Min(CurrentHP, MaxHP);
    }
}