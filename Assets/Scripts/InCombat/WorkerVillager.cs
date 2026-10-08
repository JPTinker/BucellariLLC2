using UnityEngine;

public enum WorkerState
{
    Seeking,     // walking to / looking for a farm or lumber site
    Building,    // standing on the site, raising the farm / lumber mill
    Gathering,   // working the site for a turn
    Depositing,  // carrying the load to the boat
    Returning    // back from the boat to the site
}

/// <summary>
/// Per-worker state for the player's autonomous villagers (food = farmer on flat
/// grass, materials = lumberjack in forest). Sits next to a UnitInstance flagged
/// IsWorker; WorkerVillagerAIController reads and advances it each villager turn.
/// </summary>
[RequireComponent(typeof(UnitInstance))]
public class WorkerVillager : MonoBehaviour
{
    public bool IsFood { get; private set; }
    public WorkerState State = WorkerState.Seeking;
    public int Carried;
    public HexTile Site;
    public GameObject Structure;

    private UnitInstance unit;
    private int baseMaxHealth;
    private int appliedLevel;

    public UnitInstance Unit => unit != null ? unit : (unit = GetComponent<UnitInstance>());

    public void Setup(bool isFood)
    {
        IsFood = isFood;
        baseMaxHealth = Unit.maxHealth;
    }

    /// <summary>Small per-wave HP buff. Idempotent: always recomputed from the base stat, so it can be called every turn.</summary>
    public void ApplyLevelBuff(int level, float hpBonusPerLevel)
    {
        if (level == appliedLevel) return;
        appliedLevel = level;

        int newMax = Mathf.Max(1, Mathf.RoundToInt(baseMaxHealth * (1f + level * hpBonusPerLevel)));
        int gained = newMax - Unit.maxHealth;
        Unit.maxHealth = newMax;
        if (gained > 0) Unit.currentHealth = Mathf.Min(newMax, Unit.currentHealth + gained);
        Unit.NotifyStatsChanged();
    }

    /// <summary>Adds one turn of work to the carried load, capped at carryCap.</summary>
    public void Gather(int baseYield, int level, float yieldBonusPerLevel, int carryCap)
    {
        int amount = Mathf.RoundToInt(baseYield * (1f + level * yieldBonusPerLevel));
        Carried = Mathf.Min(carryCap, Carried + amount);
    }

    /// <summary>Frees the claimed site if the worker dies or leaves for good.</summary>
    private void OnDestroy()
    {
        if (Site != null) Site.workerSiteClaimed = false;
        if (Structure != null) Destroy(Structure);
    }
}
