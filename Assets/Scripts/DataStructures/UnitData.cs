using UnityEngine;

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

    [Header("Faction & Classification")]
    public UnitFaction Faction;
}

public enum UnitFaction
{
    Player,
    Enemy,
    Neutral
}