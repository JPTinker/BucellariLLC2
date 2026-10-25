using System;
using UnityEngine;

/// <summary>Structures a hero can raise on a tile next to them from the Build menu.</summary>
public enum BuildingType { Wall, TownCenter, Church, Barracks }

/// <summary>
/// Cost, durability and look of one structure. Tunable from MapManager's Inspector;
/// <see cref="Prefab"/> is optional - without one a coloured cube stands in.
/// The wall's cost, prefab and health come from the older wall fields / research instead.
/// </summary>
[Serializable]
public class BuildingSpec
{
    public BuildingType Type;
    public string DisplayName;
    public int MaterialCost;
    public int FoodCost;
    public int MaxHealth = 20;
    public int Defense;
    [Tooltip("Church and Barracks can only be built while a Town Center is standing.")]
    public bool RequiresTownCenter;
    [Tooltip("Placeholder cube size as a multiple of the hex size (x, height, z).")]
    public Vector3 Size = new Vector3(0.8f, 0.8f, 0.8f);
    public Color Color = Color.white;
    public GameObject Prefab;

    public static BuildingSpec[] CreateDefaults()
    {
        return new[]
        {
            new BuildingSpec { Type = BuildingType.Wall, DisplayName = "Wall", MaterialCost = 5, MaxHealth = 12, Defense = 1,
                Size = new Vector3(0.8f, 0.7f, 0.8f), Color = new Color(0.45f, 0.33f, 0.22f) },
            new BuildingSpec { Type = BuildingType.TownCenter, DisplayName = "Town Center", MaterialCost = 40, FoodCost = 20, MaxHealth = 40, Defense = 2,
                Size = new Vector3(0.95f, 1.3f, 0.95f), Color = new Color(0.95f, 0.78f, 0.25f) },
            new BuildingSpec { Type = BuildingType.Church, DisplayName = "Church", MaterialCost = 25, FoodCost = 15, MaxHealth = 25, Defense = 1, RequiresTownCenter = true,
                Size = new Vector3(0.7f, 1.6f, 0.7f), Color = new Color(0.92f, 0.94f, 1f) },
            new BuildingSpec { Type = BuildingType.Barracks, DisplayName = "Barracks", MaterialCost = 30, FoodCost = 20, MaxHealth = 30, Defense = 1, RequiresTownCenter = true,
                Size = new Vector3(0.95f, 0.9f, 0.7f), Color = new Color(0.7f, 0.22f, 0.2f) },
        };
    }
}
