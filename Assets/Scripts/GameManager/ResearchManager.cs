using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Base values the research tree builds on. Inspector-tunable on GameStateManager.</summary>
[Serializable]
public class ResearchBaseConfig
{
    [Header("Base values (before any research)")]
    public int VillagerSpawnInterval = 6;      // rounds between stray villagers; Recruiting subtracts
    public float GatherMultiplier = 1f;        // worker yield / carry multiplier; Gathering adds
    public int UnitLevelCap = 10;              // LevelCap adds (10 per node)
    public int StartingHeroLevel = 1;          // level new heroes are recruited at; Training adds
    public int WallMaxHealth = 12;             // Masonry adds
    public int MaxFortificationLevel = 1;      // Engineering adds (the map only ships 3 fortification prefabs)
    public int StorageFoodPerTownCenter = 300;       // Storehouses adds (per Town Center)
    public int StorageMaterialsPerTownCenter = 300;  // Storehouses adds (per Town Center)
    public int ChurchHealAmount = 5;           // HP a hero heals for ending the round in a Church; Church adds
    public int ChurchHealCount = 2;            // benched units healed per cycle; Church adds
    public float ChurchHealFraction = 0.05f;   // of max HP; Church adds (SecondaryValue)

    [Header("Floors / ceilings")]
    public int MinVillagerSpawnInterval = 2;
    public int AbsoluteMaxFortificationLevel = 3;
}

[Serializable]
public class ResearchProgress
{
    public string Id;
    public int KillsRemaining;
}

public enum ResearchStatus { Locked, Available, Active, Completed }

/// <summary>
/// Owns research state (completed nodes + the active slots) and exposes the
/// derived stats the rest of the game reads. Projects are paid for in battle:
/// each kill a player unit lands counts toward the oldest active project only. Plain class owned by
/// GameStateManager. Consumers should read <see cref="Current"/> so the BattlePhase still works when it is
/// opened directly without a GameStateManager (falls back to base values).
/// </summary>
public class ResearchManager
{
    private static ResearchManager _fallback;

    /// <summary>The live manager, or a base-values-only one when no GameStateManager exists.</summary>
    public static ResearchManager Current
    {
        get
        {
            GameStateManager gsm = GameStateManager.Instance;
            if (gsm != null) return gsm.Research;
            return _fallback ??= new ResearchManager(null);
        }
    }

    private readonly GameStateManager _gsm;
    private readonly HashSet<string> _completed = new HashSet<string>();
    private readonly List<ResearchProgress> _active = new List<ResearchProgress>();

    /// <summary>Nodes finished since the UI last looked (the tab toasts these, then clears the list).</summary>
    public readonly List<ResearchData> RecentlyCompleted = new List<ResearchData>();

    public static event Action<ResearchData> OnResearchCompleted;

    /// <summary>Raised whenever active-project progress changes (a kill landed, or a project started/finished).</summary>
    public static event Action OnProgressChanged;

    public ResearchManager(GameStateManager gsm) { _gsm = gsm; }

    private ResearchBaseConfig Config => _gsm != null && _gsm.ResearchBase != null ? _gsm.ResearchBase : (_defaultConfig ??= new ResearchBaseConfig());
    private static ResearchBaseConfig _defaultConfig;

    public IReadOnlyList<ResearchProgress> Active => _active;
    public const int MaxActiveResearch = 3;
    public int SlotCount => MaxActiveResearch;
    public int FreeSlots => Mathf.Max(0, SlotCount - _active.Count);

    // ------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------

    public bool IsCompleted(ResearchData data) => data != null && _completed.Contains(data.name);

    public ResearchProgress GetActive(ResearchData data) => data == null ? null : _active.Find(p => p.Id == data.name);

    public ResearchStatus GetStatus(ResearchData data)
    {
        if (IsCompleted(data)) return ResearchStatus.Completed;
        if (GetActive(data) != null) return ResearchStatus.Active;
        if (data.Prerequisite != null && !IsCompleted(data.Prerequisite)) return ResearchStatus.Locked;
        return ResearchStatus.Available;
    }

    public bool CanStart(ResearchData data, out string reason)
    {
        reason = null;
        if (data == null || _gsm == null) { reason = "Unavailable"; return false; }

        ResearchStatus status = GetStatus(data);
        if (status == ResearchStatus.Completed) { reason = "Already researched"; return false; }
        if (status == ResearchStatus.Active) { reason = "Already in progress"; return false; }
        if (status == ResearchStatus.Locked) { reason = $"Requires {data.Prerequisite.DisplayName}"; return false; }
        if (FreeSlots <= 0) { reason = "No free research slot"; return false; }
        if (_gsm.Settlement.Food < data.FoodCost) { reason = $"Need {data.FoodCost} Food"; return false; }
        if (_gsm.Settlement.Materials < data.MaterialsCost) { reason = $"Need {data.MaterialsCost} Materials"; return false; }
        return true;
    }

    // ------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------

    /// <summary>Pays the cost and fills a slot. Returns false (spending nothing) if it can't start.</summary>
    public bool TryStart(ResearchData data)
    {
        if (!CanStart(data, out _)) return false;

        _gsm.Settlement.Food -= data.FoodCost;
        _gsm.Settlement.Materials -= data.MaterialsCost;
        _active.Add(new ResearchProgress { Id = data.name, KillsRemaining = Mathf.Max(1, data.KillsToComplete) });
        _gsm.NotifySettlementChanged();
        OnProgressChanged?.Invoke();
        return true;
    }

    /// <summary>Credits one kill to the oldest active project only (a kill never advances more than one), completing it if finished. Called when a player unit kills an enemy.</summary>
    public void RegisterKill()
    {
        if (_active.Count == 0) return;

        ResearchProgress progress = _active[0];
        progress.KillsRemaining--;
        if (progress.KillsRemaining <= 0)
        {
            _active.RemoveAt(0);
            ResearchData data = Find(progress.Id);
            if (data != null) Complete(data);
        }
        OnProgressChanged?.Invoke();
    }

    /// <summary>Kills landed so far on an active project, 0..total.</summary>
    public int KillsDone(ResearchProgress progress, ResearchData data) =>
        data == null ? 0 : Mathf.Clamp(data.KillsToComplete - progress.KillsRemaining, 0, data.KillsToComplete);

    public ResearchData FindData(string id) => Find(id);

    private void Complete(ResearchData data)
    {
        _completed.Add(data.name);
        RecentlyCompleted.Add(data);

        OnResearchCompleted?.Invoke(data);
    }

    private ResearchData Find(string id)
    {
        if (_gsm == null || _gsm.AvailableResearch == null) return null;
        return _gsm.AvailableResearch.Find(r => r != null && r.name == id);
    }

    // ------------------------------------------------------------
    // Derived stats (base + completed tiers)
    // ------------------------------------------------------------

    private float Sum(ResearchTrack track, bool secondary = false)
    {
        float total = 0f;
        if (_gsm == null || _gsm.AvailableResearch == null) return total;
        foreach (ResearchData r in _gsm.AvailableResearch)
        {
            if (r == null || r.Track != track || !_completed.Contains(r.name)) continue;
            total += secondary ? r.SecondaryValue : r.Value;
        }
        return total;
    }

    public int VillagerSpawnInterval =>
        Mathf.Max(Config.MinVillagerSpawnInterval, Config.VillagerSpawnInterval - Mathf.RoundToInt(Sum(ResearchTrack.Recruiting)));

    public float GatherMultiplier => Config.GatherMultiplier + Sum(ResearchTrack.Gathering);

    public int MaxUnitLevel => Config.UnitLevelCap + Mathf.RoundToInt(Sum(ResearchTrack.LevelCap));

    public int StartingHeroLevel => Mathf.Max(1, Config.StartingHeroLevel + Mathf.RoundToInt(Sum(ResearchTrack.Training)));

    public int WallMaxHealth => Config.WallMaxHealth + Mathf.RoundToInt(Sum(ResearchTrack.Masonry));

    public int MaxFortificationLevel =>
        Mathf.Min(Config.AbsoluteMaxFortificationLevel, Config.MaxFortificationLevel + Mathf.RoundToInt(Sum(ResearchTrack.Engineering)));

    /// <summary>Storage is held by Town Centers: each standing one adds its base amount plus the Storehouses research.</summary>
    private static int TownCenterCount => MapManager.Instance != null ? MapManager.Instance.GetStructures(BuildingType.TownCenter).Count : 0;

    public int MaxFood => TownCenterCount * (Config.StorageFoodPerTownCenter + Mathf.RoundToInt(Sum(ResearchTrack.Storehouses)));

    public int MaxMaterials => TownCenterCount * (Config.StorageMaterialsPerTownCenter + Mathf.RoundToInt(Sum(ResearchTrack.Storehouses)));

    public void WriteTo(BattleSaveData data)
    {
        data.CompletedResearch = new List<string>(_completed);
        data.ActiveResearchIds = new List<string>();
        data.ActiveResearchKills = new List<int>();
        foreach (ResearchProgress p in _active)
        {
            data.ActiveResearchIds.Add(p.Id);
            data.ActiveResearchKills.Add(p.KillsRemaining);
        }
    }

    public void ReadFrom(BattleSaveData data)
    {
        _completed.Clear();
        _active.Clear();
        RecentlyCompleted.Clear();
        foreach (string id in data.CompletedResearch)
            if (!string.IsNullOrEmpty(id)) _completed.Add(id);
        for (int i = 0; i < data.ActiveResearchIds.Count && i < data.ActiveResearchKills.Count; i++)
            if (!string.IsNullOrEmpty(data.ActiveResearchIds[i]))
                _active.Add(new ResearchProgress { Id = data.ActiveResearchIds[i], KillsRemaining = Mathf.Max(1, data.ActiveResearchKills[i]) });
        OnProgressChanged?.Invoke();
    }

    public int ChurchHealAmount => Config.ChurchHealAmount + Mathf.RoundToInt(Sum(ResearchTrack.Church));

    public int ChurchHealCount => Config.ChurchHealCount + Mathf.RoundToInt(Sum(ResearchTrack.Church));

    public float ChurchHealFraction => Config.ChurchHealFraction + Sum(ResearchTrack.Church, true);
}
