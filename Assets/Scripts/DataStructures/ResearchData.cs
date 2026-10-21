using UnityEngine;

/// <summary>The branches of the research tree. Each track drives one tunable value (see ResearchManager).</summary>
public enum ResearchTrack
{
    Recruiting,    // fewer rounds between stray villagers on the battle map
    Gathering,     // workers collect more per gather turn
    Training,      // higher max unit level
    Masonry,       // sturdier walls
    Engineering,   // more fortification levels
    Storehouses,   // higher Food / Materials cap
    Church,        // more benched units healed per cycle, and a higher heal %
    Shipwright     // each node builds a ship (+unit / villager capacity, +1 docked boat)
}

/// <summary>
/// One node of the research tree. The asset name is the save ID. Tiers within a
/// track are applied additively: a completed node adds its Value to the track's
/// base. Church nodes also use SecondaryValue (heal fraction).
/// </summary>
[CreateAssetMenu(fileName = "NewResearch", menuName = "Game/Research")]
public class ResearchData : ScriptableObject
{
    [Header("Identity")]
    public string DisplayName;
    [TextArea] public string Description;
    public Sprite Icon;
    public ResearchTrack Track;
    [Tooltip("1-based position in the track; the tree UI lays nodes out by this.")]
    [Min(1)] public int Tier = 1;
    [Tooltip("Must be completed before this can start. Leave empty for the first tier.")]
    public ResearchData Prerequisite;

    [Header("Cost")]
    [Min(0)] public int FoodCost;
    [Min(0)] public int MaterialsCost;
    [Tooltip("Decision cycles this takes to finish once started.")]
    [Min(1)] public int CyclesToComplete = 2;

    [Header("Effect")]
    [Tooltip("Added to the track's value (see ResearchManager). Rounds, levels, HP, cap or heal count; for Gathering it is a yield multiplier bonus (0.25 = +25%).")]
    public float Value = 1f;
    [Tooltip("Church only: added to the benched-unit heal fraction (0.05 = +5% of max HP).")]
    public float SecondaryValue;
}
