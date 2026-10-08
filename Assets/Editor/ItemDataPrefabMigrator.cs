using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration: adds an ItemData component to every prefab in
/// Assets/Objects/Items/WeaponPrefabs (the old ItemData ScriptableObject assets
/// are gone). Stats carried over from the old assets where one clearly matched the
/// prefab; everything else is left at 0 for you to fill in on the prefab.
/// Safe to re-run: existing ItemData components are updated only if still blank.
/// Run via Tools > Items > Add ItemData To Weapon Prefabs.
/// </summary>
public static class ItemDataPrefabMigrator
{
    private const string Folder = "Assets/Objects/Items/WeaponPrefabs";

    // prefab name -> (damage, range) carried over from the old ScriptableObject assets.
    private static readonly Dictionary<string, (int damage, int range)> LegacyStats = new Dictionary<string, (int, int)>
    {
        { "axe_1handed", (5, 1) },
        { "axe_1handed_Large", (7, 1) },
        { "axe_2handed", (7, 1) },
        { "axe_2handed_Large", (10, 1) },
        { "bow_withString", (10, 2) },
        { "crossbow_1handed", (4, 2) },
        { "crossbow_2handed", (6, 2) },
        { "dagger", (7, 1) },
        { "staff", (7, 1) },
    };

    [MenuItem("Tools/Items/Add ItemData To Weapon Prefabs")]
    public static void Run()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { Folder });
        int added = 0, skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var item = root.GetComponent<ItemData>();
                bool isNew = item == null;
                if (isNew) item = root.AddComponent<ItemData>();

                if (string.IsNullOrEmpty(item.ItemName)) item.ItemName = name;
                if (string.IsNullOrEmpty(item.ItemID)) item.ItemID = name;

                if (isNew && LegacyStats.TryGetValue(name, out var stats))
                {
                    item.damage = stats.damage;
                    item.range = stats.range;
                }

                if (isNew)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    added++;
                }
                else skipped++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"ItemDataPrefabMigrator: added ItemData to {added} prefabs, {skipped} already had it ({guids.Length} total).");
    }
}
