using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Single-slot save file on disk (JSON via JsonUtility). GameStateManager
/// builds/applies the SaveData itself since it owns all the state - this
/// class only handles the file and the "load on next start" handoff from the
/// title screen.
///
/// Flow: TitleScreenController sets LoadOnNextStart and loads DecisionPhase;
/// that scene's GameStateManager.Awake() sees the flag and restores from the
/// file instead of running the fresh-game setup.
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 2;

    public static string SavePath => Path.Combine(Application.persistentDataPath, "savegame.json");

    public static bool HasSave => File.Exists(SavePath);

    /// <summary>Set by the title screen's Continue button, consumed by GameStateManager.Awake().</summary>
    public static bool LoadOnNextStart;

    public static bool Write(SaveData data)
    {
        try
        {
            data.Version = CurrentVersion;
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"SaveSystem: game saved to {SavePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveSystem: failed to write save file - {e}");
            return false;
        }
    }

    public static bool TryRead(out SaveData data)
    {
        data = null;
        if (!HasSave) return false;

        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            return data != null;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveSystem: failed to read save file - {e}");
            return false;
        }
    }

    public static void Delete()
    {
        try
        {
            if (HasSave) File.Delete(SavePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveSystem: failed to delete save file - {e}");
        }
    }
}

/// <summary>
/// Everything GameStateManager needs to rebuild a campaign between Decision
/// cycles. Mid-battle state isn't saved - saving only happens on the Decision tab.
/// </summary>
[Serializable]
public class SaveData
{
    public int Version;

    public GameStateManager.PlayerResources Resources;
    public GameStateManager.SettlementResources Settlement;
    public GameStateManager.CycleAllocation CurrentAllocation;

    public GameStateManager.CampaignAction SelectedCampaignAction;
    public bool HasSelectedCampaignAction;
    public int EvacuationCyclesRemaining;

    public List<UnitSaveData> Roster = new List<UnitSaveData>();
    public List<string> PendingRevealIds = new List<string>();

    // Saved so reloading can't reroll an offered draft.
    public List<string> PendingDraftArchetypes = new List<string>();
    public List<UnitRarity> PendingDraftRarities = new List<UnitRarity>();
    public int PendingDraftsToOffer;
    public bool CanDraftSavedVillager;

    public int LastCycleKills;
    public int LastCycleVillagersSaved;
    public int LastCycleCasualties;
    public int LastCycleMoraleDelta;

    // Event system (version 2). Missing in older saves -> empty/zero.
    public int CycleNumber;
    public string PendingEventId;
    public int LastEventRollCycle;
    public List<string> FiredEventIds = new List<string>();
    public List<string> EventCooldownIds = new List<string>();
    public List<int> EventCooldownValues = new List<int>();
}

/// <summary>
/// Plain-data copy of a Unit. Unit itself references UnitData/prefab assets,
/// which JsonUtility can't persist between sessions, so the archetype is
/// stored by asset name and looked up again in AvailablePlayerArchetypes.
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

    public static UnitSaveData From(Unit unit) => new UnitSaveData
    {
        ArchetypeName = unit.Archetype != null ? unit.Archetype.name : null,
        InstanceID = unit.InstanceID,
        UnitName = unit.UnitName,
        Level = unit.Level,
        Experience = unit.Experience,
        Rarity = unit.Rarity,
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

    /// <summary>Rebuilds the Unit on top of its archetype (which re-derives weapon/equipment prefabs).</summary>
    public Unit ToUnit(UnitData archetype)
    {
        var unit = new Unit(archetype, Rarity)
        {
            InstanceID = InstanceID,
            UnitName = UnitName,
            Level = Level,
            Experience = Experience,
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
        return unit;
    }
}
