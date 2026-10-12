using System;

public enum LossCause { Battle, Event }

/// <summary>
/// Plain-data snapshot of a unit that was lost for good, shown on the
/// Decision phase's Memorial tab. Stores strings/ints only so JsonUtility
/// can persist it without asset references.
/// </summary>
[Serializable]
public class FallenRecord
{
    public string UnitName;
    public string ClassName;
    public int Level;
    public UnitRarity Rarity;
    public int CycleNumber;
    public LossCause Cause;
    public string Detail;

    public static FallenRecord From(Unit unit, int cycle, LossCause cause, string detail = null) => new FallenRecord
    {
        UnitName = unit.UnitName,
        ClassName = unit.Archetype != null ? unit.Archetype.UnitName : null,
        Level = unit.Level,
        Rarity = unit.Rarity,
        CycleNumber = cycle,
        Cause = cause,
        Detail = detail
    };
}
