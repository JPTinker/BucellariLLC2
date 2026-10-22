using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Create Default Research: writes a starter set of ResearchData assets
/// to Assets/Data/Research and assigns them to the GameStateManager in the open
/// scene. Safe to re-run: missing assets are created and existing ones get
/// the standard Food/Materials prices re-applied.
/// </summary>
public static class ResearchAssetSeeder
{
    private const string Folder = "Assets/Data/Research";

    // track, tier, display name, kills, value, secondary value (costs come from the tier)
    private struct Node
    {
        public ResearchTrack Track; public int Tier; public string Name;
        public int Food, Materials, Kills; public float Value, Secondary;
    }

    private static readonly int[] FoodByTier = { 50, 100, 200 };
    private static readonly int[] MaterialsByTier = { 100, 200, 300 };

    private static Node N(ResearchTrack track, int tier, string name, int kills, float value, float secondary = 0f) =>
        new Node { Track = track, Tier = tier, Name = name, Food = FoodByTier[tier - 1], Materials = MaterialsByTier[tier - 1], Kills = kills, Value = value, Secondary = secondary };

    [MenuItem("Tools/Create Default Research")]
    public static void CreateDefaults()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Data", "Research");

        // Every track has three tiers at the same price: Food 50/100/200, Materials 100/200/300.
        var nodes = new List<Node>
        {
            N(ResearchTrack.Recruiting, 1, "Lookout Posts I", 8, 1),
            N(ResearchTrack.Recruiting, 2, "Lookout Posts II", 14, 1),
            N(ResearchTrack.Recruiting, 3, "Lookout Posts III", 20, 1),

            N(ResearchTrack.Gathering, 1, "Better Tools I", 8, 0.25f),
            N(ResearchTrack.Gathering, 2, "Better Tools II", 14, 0.25f),
            N(ResearchTrack.Gathering, 3, "Better Tools III", 20, 0.5f),

            N(ResearchTrack.Training, 1, "Drill Yard I", 8, 5),
            N(ResearchTrack.Training, 2, "Drill Yard II", 14, 5),
            N(ResearchTrack.Training, 3, "Drill Yard III", 20, 5),

            N(ResearchTrack.Masonry, 1, "Stonecutting I", 8, 6),
            N(ResearchTrack.Masonry, 2, "Stonecutting II", 14, 6),
            N(ResearchTrack.Masonry, 3, "Stonecutting III", 20, 12),

            // Fortification level is capped at 3 (base 1 + 2), so tier III currently adds nothing.
            N(ResearchTrack.Engineering, 1, "Field Works I", 8, 1),
            N(ResearchTrack.Engineering, 2, "Field Works II", 14, 1),
            N(ResearchTrack.Engineering, 3, "Field Works III", 20, 1),

            N(ResearchTrack.Storehouses, 1, "Granaries I", 8, 200),
            N(ResearchTrack.Storehouses, 2, "Granaries II", 14, 300),
            N(ResearchTrack.Storehouses, 3, "Granaries III", 20, 500),

            N(ResearchTrack.Church, 1, "Chapel I", 8, 1, 0.025f),
            N(ResearchTrack.Church, 2, "Chapel II", 14, 1, 0.025f),
            N(ResearchTrack.Church, 3, "Chapel III", 20, 1, 0.025f),

            N(ResearchTrack.Shipwright, 1, "Shipwright I", 12, 0),
            N(ResearchTrack.Shipwright, 2, "Shipwright II", 18, 0),
            N(ResearchTrack.Shipwright, 3, "Shipwright III", 24, 0),

            N(ResearchTrack.BattleLength, 1, "Steadfast Resolve I", 8, 3),
            N(ResearchTrack.BattleLength, 2, "Steadfast Resolve II", 14, 3),
            N(ResearchTrack.BattleLength, 3, "Steadfast Resolve III", 20, 4),
        };

        var created = new List<ResearchData>();
        ResearchData previous = null;
        ResearchTrack previousTrack = nodes[0].Track;
        foreach (Node n in nodes)
        {
            if (n.Track != previousTrack) previous = null;
            previousTrack = n.Track;

            string path = $"{Folder}/Research_{n.Track}_{n.Tier}.asset";
            ResearchData asset = AssetDatabase.LoadAssetAtPath<ResearchData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ResearchData>();
                asset.Track = n.Track;
                asset.Tier = n.Tier;
                asset.DisplayName = n.Name;
                asset.FoodCost = n.Food;
                asset.MaterialsCost = n.Materials;
                asset.KillsToComplete = n.Kills;
                asset.Value = n.Value;
                asset.SecondaryValue = n.Secondary;
                asset.Prerequisite = previous;
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                // Re-running re-applies the price list; other tuning on existing assets is left alone.
                asset.FoodCost = n.Food;
                asset.MaterialsCost = n.Materials;
                EditorUtility.SetDirty(asset);
            }
            created.Add(asset);
            previous = asset;
        }

        AssetDatabase.SaveAssets();
        AssignToGameStateManager(created);
        Debug.Log($"Research: {created.Count} nodes ready in {Folder}.");
    }

    private static void AssignToGameStateManager(List<ResearchData> assets)
    {
        GameStateManager gsm = Object.FindFirstObjectByType<GameStateManager>();
        if (gsm == null)
        {
            Debug.LogWarning("Research: no GameStateManager in the open scene. Open the scene that holds it (likely TitleScreen) and run this again to assign the list.");
            return;
        }

        var serialized = new SerializedObject(gsm);
        SerializedProperty list = serialized.FindProperty("AvailableResearch");
        list.ClearArray();
        for (int i = 0; i < assets.Count; i++)
        {
            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
        }
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(gsm);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gsm.gameObject.scene);
        Debug.Log("Research: assigned to GameStateManager. Save the scene.");
    }
}
