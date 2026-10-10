using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Create Sample Events: generates a handful of EventData assets in
/// Assets/Data/Events and registers them on the GameStateManager in the open scene.
/// </summary>
public static class EventSampleBuilder
{
    private const string Folder = "Assets/Data/Events";

    [MenuItem("Tools/Create Sample Events")]
    public static void Create()
    {
        EnsureFolder("Assets/Data");
        EnsureFolder(Folder);

        var created = new List<EventData>
        {
            Make("Event_AbandonedCache", "Abandoned Cache",
                "Scouts spot a half-buried supply cache on the road.",
                10, new[]
                {
                    Choice("Dig it out", "The crew hauls out crates of salvage.", Fx(EventEffectType.Materials, 40), Fx(EventEffectType.Morale, 3)),
                    Choice("Leave it - could be a trap", "You move on, a little wiser and a little poorer.")
                }),
            Make("Event_FeverInCamp", "Fever in Camp",
                "A fever is spreading through the tents.",
                8, new[]
                {
                    Choice("Burn rations on broth and rest", "The sick recover, but the stores are thinner.", food(40), Fx(EventEffectType.Food, -40), Fx(EventEffectType.HealAll)),
                    Choice("Push on regardless", "Spirits sink as the sick are carried along.", Fx(EventEffectType.Morale, -10), Fx(EventEffectType.DamageAllUnits, 10))
                }),
            Make("Event_StrandedStranger", "Stranded Stranger",
                "A lone fighter begs to join your party.",
                6, new[]
                {
                    Choice("Welcome them", "They take up arms beside you.", Fx(EventEffectType.GrantUnit, 0), Fx(EventEffectType.Food, -15)),
                    Choice("Turn them away", "They watch you leave in silence.", Fx(EventEffectType.Morale, -3))
                }),
            Make("Event_FairWinds", "Fair Winds",
                "The weather breaks and the whole camp breathes easier.",
                10, new[]
                {
                    Choice("Hold a small feast", "Songs carry late into the night.", Fx(EventEffectType.Food, -20), Fx(EventEffectType.Morale, 12)),
                    Choice("Save the supplies", "Sensible, if a little grim.", Fx(EventEffectType.Morale, 2))
                }),
            MakeScripted("Event_HalfwayThere", "Halfway There",
                "The coast is closer than anyone dared hope. The party gathers to mark the moment.",
                5, new[]
                {
                    Choice("Rally the party", "Resolve hardens. Onward.", Fx(EventEffectType.Morale, 15), Fx(EventEffectType.HealAll))
                })
        };

        AssetDatabase.SaveAssets();

        var gsm = Object.FindAnyObjectByType<GameStateManager>();
        if (gsm != null)
        {
            gsm.AvailableEvents ??= new List<EventData>();
            foreach (var e in created) if (!gsm.AvailableEvents.Contains(e)) gsm.AvailableEvents.Add(e);
            EditorUtility.SetDirty(gsm);
            Debug.Log($"EventSampleBuilder: created {created.Count} events and registered them on '{gsm.name}'. Save the scene.");
        }
        else
        {
            Debug.Log($"EventSampleBuilder: created {created.Count} events in {Folder}. Open the scene with GameStateManager and drag them into Available Events.");
        }
    }

    private static (int f, int m, int g) food(int amount) => (amount, 0, 0);

    private static EventData Make(string id, string title, string body, int weight, EventChoice[] choices)
    {
        string path = $"{Folder}/{id}.asset";
        var e = AssetDatabase.LoadAssetAtPath<EventData>(path);
        if (e == null)
        {
            e = ScriptableObject.CreateInstance<EventData>();
            AssetDatabase.CreateAsset(e, path);
        }
        e.Title = title;
        e.Body = body;
        e.Weight = weight;
        e.Choices = new List<EventChoice>(choices);
        EditorUtility.SetDirty(e);
        return e;
    }

    private static EventData MakeScripted(string id, string title, string body, int cyclesRemaining, EventChoice[] choices)
    {
        var e = Make(id, title, body, 0, choices);
        e.Mode = EventData.TriggerMode.Scripted;
        e.ScriptedCyclesRemaining = cyclesRemaining;
        e.OneShot = true;
        return e;
    }

    private static EventChoice Choice(string label, string result, params EventEffect[] effects)
        => Choice(label, result, default, effects);

    private static EventChoice Choice(string label, string result, (int f, int m, int g) req, params EventEffect[] effects)
        => new EventChoice
        {
            Label = label,
            ResultText = result,
            Effects = new List<EventEffect>(effects),
            RequiresFood = req.f,
            RequiresMaterials = req.m,
            RequiresGold = req.g
        };

    private static EventEffect Fx(EventEffectType type, int amount = 0) => new EventEffect { Type = type, Amount = amount };

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
