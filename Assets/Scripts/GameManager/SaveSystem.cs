using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Single-slot save of the battle in progress (JSON via JsonUtility). The state owners write and
/// apply their own slice (GameStateManager, MapManager, CombatManager); this class only handles the
/// file and the "load on next start" handoff from the title screen.
///
/// Flow: TitleScreenController sets LoadOnNextStart and loads the battle scene; MapManager.Awake sees
/// the flag, reads the file and rebuilds the battle instead of generating a new one. CombatManager.Start
/// picks up its counters from <see cref="Loaded"/>.
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 1;

    public static string SavePath => Path.Combine(Application.persistentDataPath, "battlesave.json");

    public static bool HasSave => File.Exists(SavePath);

    /// <summary>Set by the title screen's Continue button, consumed by MapManager.Awake().</summary>
    public static bool LoadOnNextStart;

    /// <summary>The save being restored this scene load; MapManager sets it, CombatManager.Start() consumes it.</summary>
    public static BattleSaveData Loaded;

    public static bool Write(BattleSaveData data)
    {
        try
        {
            data.Version = CurrentVersion;
            data.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: false));
            Debug.Log($"SaveSystem: battle saved to {SavePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveSystem: failed to write save file - {e}");
            return false;
        }
    }

    public static bool TryRead(out BattleSaveData data)
    {
        data = null;
        if (!HasSave) return false;

        try
        {
            data = JsonUtility.FromJson<BattleSaveData>(File.ReadAllText(SavePath));
            if (data == null || data.Version != CurrentVersion || data.Tiles == null || data.Tiles.Count == 0)
            {
                data = null;
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveSystem: failed to read save file - {e}");
            return false;
        }
    }

    /// <summary>Gathers every system's state into one snapshot and writes it. Call only between player actions.</summary>
    public static bool SaveCurrentBattle()
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null || MapManager.Instance == null || CombatManager.Instance == null) return false;

        var data = new BattleSaveData();
        gsm.WriteSave(data);
        MapManager.Instance.WriteSave(data);
        CombatManager.Instance.WriteSave(data);
        return Write(data);
    }

    /// <summary>Short "Round 12 - 2026-10-25 14:03" line for the title screen's Continue button.</summary>
    public static string Describe()
    {
        return TryRead(out BattleSaveData data) ? $"Round {data.Round} - {data.SavedAt}" : null;
    }
}

/// <summary>Everything needed to rebuild the battle scene exactly where the player left it.</summary>
[Serializable]
public class BattleSaveData
{
    public int Version;
    public string SavedAt;

    // GameStateManager
    public int Food;
    public int Materials;
    public int Morale;
    public int EnemiesKilled;
    public int HeroesLost;
    public int VillagersLost;
    public int VillagersEscaped;
    public int RoundsElapsed;
    public List<UnitSaveData> Roster = new List<UnitSaveData>();
    public List<string> CompletedResearch = new List<string>();
    public List<string> ActiveResearchIds = new List<string>();
    public List<int> ActiveResearchKills = new List<int>();

    // MapManager
    public int Seed;
    public int Width;
    public int Height;
    public int RoundsSinceTownCenterVillager;
    public int NextTownCenterIndex;
    public List<TileSave> Tiles = new List<TileSave>();
    public List<UnitStateSave> Units = new List<UnitStateSave>();

    // CombatManager
    public int Round;
    public int LastWaveRound;
    public int LastEvacTurn;
    public int EnemyWaveLevel;
    public int MapExpansions;
    public int BarracksRounds;
}

[Serializable]
public class TileSave
{
    public int X;
    public int Y;
    public int PrefabIndex;
    public bool Revealed;
    public int FortificationLevel;
    public int ExtractionSlots; // > 0 = an open evacuation point
}

[Serializable]
public class CarriedSave
{
    public int Slot;
    public string ArchetypeName;
    public string UnitName;
    public bool IsWorker;
    public bool IsFood;
}

public enum SavedUnitKind { Hero, Enemy, Villager, Worker, Structure }

/// <summary>One unit (or building) on the map. Stats are stored absolute so wave scaling and buffs survive the round trip.</summary>
[Serializable]
public class UnitStateSave
{
    public SavedUnitKind Kind;
    public string ArchetypeName;   // UnitData asset name (not used for structures)
    public string InstanceID;      // heroes: links to the roster record
    public string UnitName;
    public int X;
    public int Y;
    public bool InStructure;       // hero standing inside a Town Center / Church / Barracks / Library

    public int MaxHealth;
    public int CurrentHealth;
    public int AttackPower;
    public int AttackRange;
    public int DefensePower;
    public int MovementRange;
    public int VisibilityRange;
    public int MaxActions;
    public int ActionsRemaining;

    // Enemies / villagers: the rolled look and loadout.
    public UnitColorScheme ColorScheme;
    public string WeaponPrefabName;
    public string EquipmentPrefabName;

    public BuildingType Building;  // structures

    // Workers
    public bool IsFood;
    public WorkerState State;
    public int Carried;
    public bool HasSite;
    public int SiteX;
    public int SiteY;

    public List<CarriedSave> CarriedVillagers = new List<CarriedSave>();
}

/// <summary>
/// Plain-data copy of a Unit. Unit itself references UnitData/prefab assets, which JsonUtility can't
/// persist between sessions, so the archetype is stored by asset name and looked up again.
/// </summary>
[Serializable]
public class UnitSaveData
{
    public string ArchetypeName;
    public string InstanceID;
    public string UnitName;
    public int Level;
    public int Experience;
    public UnitRarity Rarity;
    public UnitMastery Mastery;
    public UnitPersonality Personality;
    public int Wounds;
    public int DamageTowardNextWound;
    public int MaxHP;
    public int CurrentHP;
    public int BaseAttack;
    public int AttackRange;
    public int MoveRange;
    public int DefensePower;
    public int VisibilityRange;
    public int MaxMovementPoints;
    public UnitColorScheme ColorScheme;
    public UnitFaction Faction;
    public string WeaponPrefabName;
    public string EquipmentPrefabName;

    public static UnitSaveData From(Unit unit) => new UnitSaveData
    {
        WeaponPrefabName = unit.WeaponPrefab != null ? unit.WeaponPrefab.name : null,
        EquipmentPrefabName = unit.EquipmentPrefab != null ? unit.EquipmentPrefab.name : null,
        ArchetypeName = unit.Archetype != null ? unit.Archetype.name : null,
        InstanceID = unit.InstanceID,
        UnitName = unit.UnitName,
        Level = unit.Level,
        Experience = unit.Experience,
        Rarity = unit.Rarity,
        Mastery = unit.Mastery,
        Personality = unit.Personality,
        Wounds = unit.Wounds,
        DamageTowardNextWound = unit.DamageTowardNextWound,
        MaxHP = unit.MaxHP,
        CurrentHP = unit.CurrentHP,
        BaseAttack = unit.BaseAttack,
        AttackRange = unit.AttackRange,
        MoveRange = unit.MoveRange,
        DefensePower = unit.DefensePower,
        VisibilityRange = unit.VisibilityRange,
        MaxMovementPoints = unit.MaxMovementPoints,
        ColorScheme = unit.ColorScheme,
        Faction = unit.Faction
    };

    /// <summary>Rebuilds the Unit on top of its archetype, then restores the rolled loadout.</summary>
    public Unit ToUnit(UnitData archetype)
    {
        var unit = new Unit(archetype, Rarity)
        {
            InstanceID = InstanceID,
            UnitName = UnitName,
            Level = Level,
            Experience = Experience,
            Mastery = Mastery,
            Personality = Personality,
            Wounds = Wounds,
            DamageTowardNextWound = DamageTowardNextWound,
            MaxHP = MaxHP,
            CurrentHP = CurrentHP,
            BaseAttack = BaseAttack,
            AttackRange = AttackRange,
            MoveRange = MoveRange,
            DefensePower = DefensePower,
            VisibilityRange = VisibilityRange,
            MaxMovementPoints = MaxMovementPoints,
            ColorScheme = ColorScheme,
            Faction = Faction
        };
        unit.WeaponPrefab = Unit.FindPrefab(archetype.weaponPrefabs, WeaponPrefabName);
        unit.EquipmentPrefab = Unit.FindPrefab(archetype.equipmentPrefabs, EquipmentPrefabName);
        return unit;
    }
}
