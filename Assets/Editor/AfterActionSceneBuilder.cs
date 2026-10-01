using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Creates Assets/Scenes/AfterAction.unity (a UIDocument + AfterActionController)
/// and slots it into the build list after BattlePhase. Runs automatically once
/// if the scene is missing; also available as Tools > Create AfterAction Scene.
/// </summary>
[InitializeOnLoad]
public static class AfterActionSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/AfterAction.unity";
    private const string UxmlPath = "Assets/UI/UXML/AfterAction.uxml";
    private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

    static AfterActionSceneBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (!Application.isPlaying && !EditorApplication.isCompiling &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                Build();
        };
    }

    [MenuItem("Tools/Create AfterAction Scene")]
    public static void Build()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (uxml == null || panel == null)
        {
            Debug.LogWarning("AfterActionSceneBuilder: missing AfterAction.uxml or PanelSettings.asset; scene not created.");
            return;
        }

        // Additive so whatever scene the user has open is left untouched.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        var go = new GameObject("AfterAction");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        doc.visualTreeAsset = uxml;
        go.AddComponent<AfterActionController>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);

        AddToBuildSettings();
        Debug.Log("AfterActionSceneBuilder: created " + ScenePath + " and added it to Build Settings.");
    }

    private static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;

        int battle = scenes.FindIndex(s => s.path.EndsWith("/BattlePhase.unity"));
        var entry = new EditorBuildSettingsScene(ScenePath, true);
        if (battle >= 0) scenes.Insert(battle + 1, entry);
        else scenes.Add(entry);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
