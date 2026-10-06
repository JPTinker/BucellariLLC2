using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    public string ItemID;
    public string ItemName;
    [TextArea(2, 4)]
    public string Description;
    public GameObject ModelPrefab;    // World space visual / Sprite Prefab

    [Header("Base Combat Stats")]
    public int damage = 0;
    public int range = 0;       // 1 = Melee, 2+ = Ranged
    public int healing = 0;         // Tiles per turn
    public int defense = 0;         // Damage reduction

    [Header("Weapon VFX")]
    [Tooltip("Played on the target when this weapon hits. Empty = fall back to CombatVfxConfig.hit.")]
    public GameObject weaponHitVfx;
    [Tooltip("Played instead of Weapon Hit VFX on a heavy blow. Empty = use the config's heavy hit, or Weapon Hit VFX.")]
    public GameObject weaponHeavyHitVfx;

}