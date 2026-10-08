using UnityEngine;

/// <summary>
/// Item stats live directly on the item prefab (Assets/Objects/Items/WeaponPrefabs).
/// UnitData.weaponPrefabs / equipmentPrefabs point at these prefabs, so the prefab
/// is both the visual and the data.
/// </summary>
public class ItemData : MonoBehaviour
{
    [Header("Identity")]
    public string ItemID;
    public string ItemName;
    [TextArea(2, 4)]
    public string Description;

    [Header("Base Combat Stats")]
    public int damage = 0;
    public int range = 0;       // 1 = Melee, 2+ = Ranged
    public int healing = 0;
    public int defense = 0;     // Damage reduction

    [Header("Weapon VFX")]
    [Tooltip("Played on the target when this weapon hits. Empty = fall back to CombatVfxConfig.hit.")]
    public GameObject weaponHitVfx;
    [Tooltip("Played instead of Weapon Hit VFX on a heavy blow. Empty = use the config's heavy hit, or Weapon Hit VFX.")]
    public GameObject weaponHeavyHitVfx;
}
