using System.Collections.Generic;

public enum BattleOutcome
{
    InField,   // still on the map when the report was read
    Extracted,
    Died
}

/// <summary>One level gained during a battle, with the stats on either side of it.</summary>
public struct LevelStep
{
    public int OldLevel, NewLevel;
    public int OldMaxHP, NewMaxHP;
    public int OldAttack, NewAttack;
    public int OldDefense, NewDefense;
}

/// <summary>What happened to one player unit in the battle that just ended.</summary>
public class UnitReportEntry
{
    public Unit Unit;
    public BattleOutcome Outcome = BattleOutcome.InField;
    public int RescuedVillagers;

    public int StartLevel, StartXP, StartMaxHP, StartAttack, StartDefense;
    public int EndXP;

    public readonly List<LevelStep> LevelSteps = new List<LevelStep>();

    public UnitReportEntry(Unit unit)
    {
        Unit = unit;
        StartLevel = unit.Level;
        StartXP = unit.Experience;
        StartMaxHP = unit.MaxHP;
        StartAttack = unit.BaseAttack;
        StartDefense = unit.DefensePower;
        EndXP = unit.Experience;
    }
}

/// <summary>
/// Transient record of the last battle, read by the AfterAction scene. Not
/// saved: it only has to survive the BattlePhase -> AfterAction -> DecisionPhase hop.
/// </summary>
public class BattleReport
{
    public readonly List<UnitReportEntry> Entries = new List<UnitReportEntry>();
    public int EnemiesKilled;
    public int VillagersSaved;
    public int FoodGathered;
    public int MaterialsGathered;
    public int WorkersLost;
    public bool Won;

    public bool IsEmpty => Entries.Count == 0;

    public UnitReportEntry GetOrCreate(Unit unit)
    {
        if (unit == null) return null;
        foreach (var e in Entries)
            if (e.Unit == unit) return e;

        var entry = new UnitReportEntry(unit);
        Entries.Add(entry);
        return entry;
    }

    public UnitReportEntry Find(Unit unit)
    {
        foreach (var e in Entries)
            if (e.Unit == unit) return e;
        return null;
    }
}
