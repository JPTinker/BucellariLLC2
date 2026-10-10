using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Random hero names from Resources/Names/hero_names.csv ("type,name" rows,
/// type = first | last; a header row and blank/# lines are ignored). Add rows
/// to the CSV to grow the pool; first x last gives thousands of combinations.
/// </summary>
public static class NameGenerator
{
    public const int MaxNameLength = 20;
    private const string ResourcePath = "Names/hero_names";

    private static readonly List<string> First = new List<string>();
    private static readonly List<string> Last = new List<string>();
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            Debug.LogWarning($"NameGenerator: Resources/{ResourcePath}.csv not found, using fallback names.");
        }
        else
        {
            foreach (string raw in csv.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string type = line.Substring(0, comma).Trim().ToLowerInvariant();
                string name = line.Substring(comma + 1).Trim().Trim('"');
                if (name.Length == 0) continue;

                if (type == "first") First.Add(name);
                else if (type == "last") Last.Add(name);
            }
        }

        if (First.Count == 0) First.AddRange(new[] { "Aldric", "Brenna", "Cedric", "Dorian" });
        if (Last.Count == 0) Last.AddRange(new[] { "Ashford", "Hawthorne", "Marlowe" });
    }

    /// <summary>A random "First Last" name, trimmed to <see cref="MaxNameLength"/>.</summary>
    public static string Random()
    {
        EnsureLoaded();
        string name = $"{First[UnityEngine.Random.Range(0, First.Count)]} {Last[UnityEngine.Random.Range(0, Last.Count)]}";
        return name.Length <= MaxNameLength ? name : First[UnityEngine.Random.Range(0, First.Count)];
    }
}
