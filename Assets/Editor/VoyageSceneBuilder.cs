using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Creates Assets/Scenes/Voyage.unity (a UIDocument + VoyageController) and slots
/// it into the build list after DecisionPhase. Runs automatically once if the
/// scene is missing; also available as Tools > Create Voyage Scene.
/// </summary>
[InitializeOnLoad]
public static class VoyageSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Voyage.unity";
    private const string UxmlPath = "Assets/UI/UXML/Voyage.uxml";
    private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

    static VoyageSceneBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (!Application.isPlaying && !EditorApplication.isCompiling &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                Build();
        };
    }

    [MenuItem("Tools/Create Voyage Scene")]
    public static void Build()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (uxml == null || panel == null)
        {
            Debug.LogWarning("VoyageSceneBuilder: missing Voyage.uxml or PanelSettings.asset; scene not created.");
            return;
        }

        // Additive so whatever scene the user has open is left untouched.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        var go = new GameObject("Voyage");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        doc.visualTreeAsset = uxml;
        go.AddComponent<VoyageController>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);

        AddToBuildSettings();
        Debug.Log("VoyageSceneBuilder: created " + ScenePath + " and added it to Build Settings.");
    }

    private static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;

        int decision = scenes.FindIndex(s => s.path.EndsWith("/DecisionPhase.unity"));
        var entry = new EditorBuildSettingsScene(ScenePath, true);
        if (decision >= 0) scenes.Insert(decision + 1, entry);
        else scenes.Add(entry);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
