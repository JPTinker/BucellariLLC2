using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Moments in a battle a unit can comment on. The names are what the CSV's Trigger column uses.</summary>
public enum UnitSpeechTrigger
{
    Spawn,
    Move,
    Attack,
    Kill,
    Hit,
    LowHealth,
    Flanked,
    Death,
    LevelUp,
    Fortify,
    Scout,
    Rescue,
    Extract,
}

/// <summary>
/// Entry point for unit chatter. Gameplay code calls <c>unit.Say(trigger)</c>; the
/// speech bubble controller listens to <see cref="OnSpeak"/> and decides what (if
/// anything) to show using <see cref="UnitSpeechLibrary"/>. Nothing here touches
/// the UI, so triggers can be added anywhere without caring whether bubbles exist.
/// </summary>
public static class UnitSpeech
{
    /// <summary>unit, trigger, seconds to wait before showing (e.g. until a projectile lands).</summary>
    public static event Action<UnitInstance, UnitSpeechTrigger, float> OnSpeak;

    public static void Raise(UnitInstance unit, UnitSpeechTrigger trigger, float delay = 0f)
    {
        // Lore: enemies are monsters and don't speak the language, so they never get a bubble.
        if (unit == null || unit.Faction == UnitFaction.Enemy) return;
        OnSpeak?.Invoke(unit, trigger, delay);
    }
}

/// <summary>
/// What units say, from Resources/Dialogue/unit_speech.csv. Row format:
///   Trigger,Faction,Unit,Chance,Text
/// Trigger = a <see cref="UnitSpeechTrigger"/> name. Faction = Any | Player | Enemy | Villager.
/// Unit = Any (or *), or an archetype asset name such as Minion. Chance = 0..1.
/// Text = the rest of the line (commas allowed); {name} becomes the unit's name.
/// A header row and blank/# lines are ignored. Rows naming a specific unit are favoured over Any rows.
/// </summary>
public static class UnitSpeechLibrary
{
    private const string ResourcePath = "Dialogue/unit_speech";
    private const int SpecificUnitWeight = 3;

    private sealed class Line
    {
        public UnitSpeechTrigger Trigger;
        public UnitFaction? Faction; // null = any
        public string Unit;          // null = any
        public float Chance;
        public string Text;
    }

    private static readonly Dictionary<UnitSpeechTrigger, List<Line>> Lines = new Dictionary<UnitSpeechTrigger, List<Line>>();
    private static bool loaded;

    /// <summary>Forget the cached CSV so the next lookup re-reads it (handy after editing it in the editor).</summary>
    public static void Reload()
    {
        loaded = false;
        Lines.Clear();
    }

    /// <summary>Picks a line for this unit and trigger, or null if nothing matches or the chance roll fails.</summary>
    public static string Pick(UnitInstance unit, UnitSpeechTrigger trigger)
    {
        EnsureLoaded();
        if (unit == null || !Lines.TryGetValue(trigger, out List<Line> candidates)) return null;

        string archetype = ArchetypeName(unit);
        var pool = new List<Line>();
        foreach (Line line in candidates)
        {
            if (line.Faction.HasValue && line.Faction.Value != unit.Faction) continue;
            if (line.Unit != null && !string.Equals(line.Unit, archetype, StringComparison.OrdinalIgnoreCase)) continue;

            int weight = line.Unit != null ? SpecificUnitWeight : 1;
            for (int i = 0; i < weight; i++) pool.Add(line);
        }
        if (pool.Count == 0) return null;

        Line chosen = pool[UnityEngine.Random.Range(0, pool.Count)];
        if (UnityEngine.Random.value > chosen.Chance) return null;

        return chosen.Text.Replace("{name}", unit.unitName);
    }

    private static string ArchetypeName(UnitInstance unit)
    {
        UnitData archetype = unit.PersistentUnit != null ? unit.PersistentUnit.Archetype : null;
        return archetype != null ? archetype.name : null;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            Debug.LogWarning($"UnitSpeechLibrary: Resources/{ResourcePath}.csv not found, units will stay quiet.");
            return;
        }

        foreach (string raw in csv.text.Split('\n'))
        {
            string row = raw.Trim();
            if (row.Length == 0 || row[0] == '#') continue;

            // Only the first four commas separate columns, so the text may contain commas.
            string[] parts = row.Split(new[] { ',' }, 5);
            if (parts.Length < 5) continue;

            if (!Enum.TryParse(parts[0].Trim(), true, out UnitSpeechTrigger trigger)) continue; // header or typo
            string text = parts[4].Trim().Trim('"');
            if (text.Length == 0) continue;

            float chance = float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? Mathf.Clamp01(parsed) : 1f;

            string factionText = parts[1].Trim();
            UnitFaction? faction = Enum.TryParse(factionText, true, out UnitFaction parsedFaction) ? parsedFaction : (UnitFaction?)null;
            if (faction == null && !IsAny(factionText))
                Debug.LogWarning($"UnitSpeechLibrary: unknown faction '{factionText}' in row '{row}', treating as Any.");

            string unitText = parts[2].Trim();

            if (!Lines.TryGetValue(trigger, out List<Line> list))
                Lines[trigger] = list = new List<Line>();
            list.Add(new Line
            {
                Trigger = trigger,
                Faction = faction,
                Unit = IsAny(unitText) ? null : unitText,
                Chance = chance,
                Text = text,
            });
        }
    }

    private static bool IsAny(string value) =>
        value.Length == 0 || value == "*" || string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase);
}
