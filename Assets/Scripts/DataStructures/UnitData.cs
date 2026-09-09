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

    [Header("Base Combat Stats")]
    public int MaxHP = 100;
    public int BaseAttack = 15;
    public int AttackRange = 1;       // 1 = Melee, 2+ = Ranged
    public int MoveSpeed = 3;         // Tiles per turn
    public int defensePower = 1;
    public int healsOthers = 0;          // How much this unit heals others (if any)
    [Header("Faction & Classification")]
    public UnitFaction Faction;
}

public enum UnitFaction
{
    Player,
    Enemy,
    Neutral,
    Villager
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

    [Header("Persistent Stats")]
    public int MaxHP;
    public int BaseAttack;
    public int AttackRange;
    public int MoveSpeed;
    public int DefensePower;
    public int HealsOthers;
    public UnitFaction Faction;

    [Header("Archetype Reference")]
    public UnitData Archetype;

    public Sprite UnitIcon => Archetype != null ? Archetype.UnitIcon : null;

    public Unit(UnitData archetype)
    {
        if (archetype == null)
            throw new ArgumentNullException(nameof(archetype));

        InstanceID = Guid.NewGuid().ToString("N");
        Archetype = archetype;
        UnitID = archetype.UnitID;
        UnitName = archetype.UnitName;
        MaxHP = archetype.MaxHP;
        BaseAttack = archetype.BaseAttack;
        AttackRange = archetype.AttackRange;
        MoveSpeed = archetype.MoveSpeed;
        DefensePower = archetype.defensePower;
        HealsOthers = archetype.healsOthers;
        Faction = archetype.Faction;
    }
}