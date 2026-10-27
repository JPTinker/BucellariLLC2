using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameStateManager : MonoBehaviour
{
    /// <summary>The settlement's economy. Morale is the lose condition: it starts at <see cref="CycleBalanceConfig.StartingMorale"/> and the run ends at 0.</summary>
    [System.Serializable]
    public class SettlementResources
    {
        public int Food = 200;
        public int Materials = 185;
        [Range(0, 100)] public int Morale = 50;
    }

    /// <summary>Tunable rates for the run. Exposed so designers can rebalance from the Inspector instead of editing code.</summary>
    [System.Serializable]
    public class CycleBalanceConfig
    {
        [Header("Worker villagers (in battle)")]
        public int WorkerFoodPerGather = 30;       // food carried per gather turn at a farm
        public int WorkerMaterialsPerGather = 40;  // materials carried per gather turn at a lumber mill
        public int WorkerCarryCap = 120;           // max a worker carries before it must deposit
        public float WorkerHpBonusPerLevel = 0.10f;    // +HP fraction per enemy wave level
        public float WorkerYieldBonusPerLevel = 0.10f; // +gather yield fraction per enemy wave level

        [Header("Zone of control")]
        public bool ZocEnabled = true;
        [Tooltip("Extra movement spent to step onto a tile next to a hostile melee unit.")]
        public int ZocPenalty = 2;

        [Header("Leveling")]
        public int FoodCostPerLevelUp = 5;        // Food the settlement spends each time a unit levels up

        [Header("Morale (lose at 0)")]
        public int StartingMorale = 50;
        public int HeroDeathMoralePenalty = 8;
        public int VillagerDeathMoralePenalty = 5;
        [Tooltip("Gained for each enemy unit killed.")]
        public int EnemyKillMoraleBonus = 3;
        [Tooltip("Lost when a wall is destroyed.")]
        public int WallDestroyedMoralePenalty = 1;
        [Tooltip("Lost each time a villager takes damage.")]
        public int VillagerHitMoralePenalty = 2;
        [Tooltip("Lost per extraction slot left unused when an extraction point disappears.")]
        public int UnusedExtractionSlotMoralePenalty = 6;

        [Header("Win / score")]
        public int VillagersToWin = 10;
        public int ScoreBase = 10000;
        public int ScorePerRound = -50;
        public int ScorePerEnemyKilled = 100;
        public int ScorePerHeroLost = -250;
        public int ScorePerVillagerLost = -100;
    }

    /// <summary>Everything the Win/Lose panel shows once the run ends.</summary>
    public struct GameResult
    {
        public bool Won;
        public int Score;
        public int Rounds;
        public int EnemiesKilled;
        public int HeroesLost;
        public int VillagersLost;
        public int VillagersEscaped;
    }

    /// <summary>
    /// Snapshot of a single unit's level-up, captured at the moment the level-up
    /// happens. Drained via PendingLevelUps.
    /// </summary>
    [System.Serializable]
    public struct LevelUpInfo
    {
        public Unit Unit;
        public int OldLevel;
        public int NewLevel;
        public int OldMaxHP;
        public int NewMaxHP;
        public int OldAttack;
        public int NewAttack;

        public LevelUpInfo(Unit unit, int oldLevel, int newLevel, int oldMaxHP, int newMaxHP, int oldAttack, int newAttack)
        {
            Unit = unit;
            OldLevel = oldLevel;
            NewLevel = newLevel;
            OldMaxHP = oldMaxHP;
            NewMaxHP = newMaxHP;
            OldAttack = oldAttack;
            NewAttack = newAttack;
        }
    }

    // --- SINGLETON SETUP ---
    public static GameStateManager Instance { get; private set; }

    public const string TitleSceneName = "TitleScreen";
    public const string GameSceneName = "BattlePhase";
    public const string VictorySceneName = "AfterAction";

    [Header("Available Unit Templates")]
    public List<UnitData> AvailablePlayerArchetypes; // Drag the hero UnitData assets here

    [Header("Research")]
    public List<ResearchData> AvailableResearch; // Drag ResearchData assets here (asset name is the save ID)
    public ResearchBaseConfig ResearchBase = new ResearchBaseConfig();

    private ResearchManager _research;
    public ResearchManager Research => _research ??= new ResearchManager(this);

    /// <summary>Hard ceiling on heroes; each standing Barracks raises the cap by <see cref="HeroesPerBarracks"/> up to this.</summary>
    public const int MaxHeroes = 8;
    public const int HeroesPerBarracks = 2;
    public const int StarterHeroCount = 2;

    [Header("Settlement")]
    public SettlementResources Settlement = new SettlementResources();
    public CycleBalanceConfig Balance = new CycleBalanceConfig();

    /// <summary>All heroes owned this run. The battle scene spawns every one of them.</summary>
    public List<Unit> FullRoster = new List<Unit>();

    // Heroes in the battle scene. Kept equal to FullRoster; MapManager reads it when placing the starting team.
    public List<Unit> ActiveTeam = new List<Unit>();

    // Units that leveled up and haven't had their "level up" feedback shown yet.
    [NonSerialized] public Queue<LevelUpInfo> PendingLevelUps = new Queue<LevelUpInfo>();

    /// <summary>Current hero cap: the starting squad plus two per standing Barracks, never above <see cref="MaxHeroes"/>.</summary>
    public int HeroCap
    {
        get
        {
            int barracks = MapManager.Instance != null ? MapManager.Instance.GetStructures(BuildingType.Barracks).Count : 0;
            return Mathf.Min(MaxHeroes, StarterHeroCount + barracks * HeroesPerBarracks);
        }
    }

    public bool HeroesFull => FullRoster.Count >= HeroCap;

    [Header("Run progress")]
    public int EnemiesKilledThisBattle;
    public int HeroesLostThisBattle;
    public int VillagersLostThisBattle;
    public int VillagersEscapedThisBattle;
    public int RoundsElapsed;

    /// <summary>Per-unit record of the run so far.</summary>
    public BattleReport LastBattleReport { get; private set; } = new BattleReport();

    public bool GameEnded { get; private set; }
    public GameResult LastResult { get; private set; }

    /// <summary>Raised once when the run is won or lost, so the HUD can show the result panel.</summary>
    public static event Action<GameResult> OnGameOver;

    /// <summary>Raised when Settlement values change mid-scene (worker deposits, spending, morale) so HUDs can repaint.</summary>
    public static event Action OnSettlementChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Settlement.Morale = Balance.StartingMorale;
        if (FullRoster.Count == 0) GrantStarterUnits(StarterHeroCount);
        ActiveTeam = new List<Unit>(FullRoster);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Gives the player's starting heroes. Starters are always Legendary.</summary>
    private void GrantStarterUnits(int count)
    {
        if (AvailablePlayerArchetypes == null || AvailablePlayerArchetypes.Count == 0)
        {
            Debug.LogError("No UnitData archetypes assigned in GameStateManager!");
            return;
        }

        var starters = new List<UnitData>();
        for (int i = 0; i < count; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, AvailablePlayerArchetypes.Count);
            starters.Add(AvailablePlayerArchetypes[randomIndex]);
        }

        AddUnitsToRoster(starters, UnitRarity.Legendary);
    }

    /// <summary>Adds heroes to the roster. <paramref name="rarity"/> null rolls one per unit.</summary>
    public void AddUnitsToRoster(List<UnitData> archetypes, UnitRarity? rarity = null)
    {
        if (archetypes == null) return;
        foreach (UnitData archetype in archetypes)
        {
            if (archetype == null) continue;
            Unit unit = new Unit(archetype, rarity ?? UnitRarityTable.Roll());
            unit.UnitName = NameGenerator.Random();
            unit.GrantLevels(Research.StartingHeroLevel - 1);
            FullRoster.Add(unit);
        }
    }

    /// <summary>Adds a single hero to the roster and the battle team, returning the new record (null if the hero cap is reached).</summary>
    public Unit AddHero(UnitData archetype, UnitRarity? rarity = null)
    {
        if (archetype == null || HeroesFull) return null;
        Unit unit = new Unit(archetype, rarity ?? UnitRarityTable.Roll());
        unit.UnitName = NameGenerator.Random();
        unit.GrantLevels(Research.StartingHeroLevel - 1); // Training research: recruits arrive already leveled
        FullRoster.Add(unit);
        ActiveTeam.Add(unit);
        return unit;
    }

    /// <summary>Queues a level-up card. Call from wherever XP/leveling is applied.</summary>
    public void QueueLevelUp(Unit unit, int oldLevel, int newLevel, int oldMaxHP, int newMaxHP, int oldAttack, int newAttack)
    {
        if (unit == null) return;
        PendingLevelUps.Enqueue(new LevelUpInfo(unit, oldLevel, newLevel, oldMaxHP, newMaxHP, oldAttack, newAttack));
    }

    public void ClearPendingLevelUps() => PendingLevelUps.Clear();

    // --- SAVE / LOAD ---

    /// <summary>Copies the run's economy, counters, roster and research into the snapshot.</summary>
    public void WriteSave(BattleSaveData data)
    {
        data.Food = Settlement.Food;
        data.Materials = Settlement.Materials;
        data.Morale = Settlement.Morale;
        data.EnemiesKilled = EnemiesKilledThisBattle;
        data.HeroesLost = HeroesLostThisBattle;
        data.VillagersLost = VillagersLostThisBattle;
        data.VillagersEscaped = VillagersEscapedThisBattle;
        data.RoundsElapsed = RoundsElapsed;
        data.Roster = new List<UnitSaveData>();
        foreach (Unit unit in FullRoster) data.Roster.Add(UnitSaveData.From(unit));
        Research.WriteTo(data);
    }

    /// <summary>Replaces the fresh-run state this manager booted with by the saved one.</summary>
    public void ApplySave(BattleSaveData data)
    {
        Settlement.Food = data.Food;
        Settlement.Materials = data.Materials;
        Settlement.Morale = data.Morale;
        EnemiesKilledThisBattle = data.EnemiesKilled;
        HeroesLostThisBattle = data.HeroesLost;
        VillagersLostThisBattle = data.VillagersLost;
        VillagersEscapedThisBattle = data.VillagersEscaped;
        RoundsElapsed = data.RoundsElapsed;

        FullRoster.Clear();
        foreach (UnitSaveData saved in data.Roster)
        {
            UnitData archetype = AvailablePlayerArchetypes != null
                ? AvailablePlayerArchetypes.Find(a => a != null && a.name == saved.ArchetypeName) : null;
            if (archetype == null)
            {
                Debug.LogWarning($"GameStateManager: saved hero '{saved.UnitName}' has unknown archetype '{saved.ArchetypeName}'; skipped.");
                continue;
            }
            FullRoster.Add(saved.ToUnit(archetype));
        }
        ActiveTeam = new List<Unit>(FullRoster);
        Research.ReadFrom(data);
        NotifySettlementChanged();
    }

    // --- RUN END ---

    /// <summary>Ends the run and raises <see cref="OnGameOver"/> with the final score. Only the first call counts.</summary>
    public void EndGame(bool won)
    {
        if (GameEnded) return;
        GameEnded = true;

        LastBattleReport.EnemiesKilled = EnemiesKilledThisBattle;
        LastBattleReport.VillagersSaved = VillagersEscapedThisBattle;
        LastBattleReport.Won = won;

        LastResult = new GameResult
        {
            Won = won,
            Rounds = RoundsElapsed,
            EnemiesKilled = EnemiesKilledThisBattle,
            HeroesLost = HeroesLostThisBattle,
            VillagersLost = VillagersLostThisBattle,
            VillagersEscaped = VillagersEscapedThisBattle,
            Score = ComputeScore()
        };
        OnGameOver?.Invoke(LastResult);
    }

    /// <summary>Live score shown in the top bar (same formula as the final score).</summary>
    public int CurrentScore => ComputeScore();

    private int ComputeScore()
    {
        var b = Balance;
        int score = b.ScoreBase
            + b.ScorePerRound * RoundsElapsed
            + b.ScorePerEnemyKilled * EnemiesKilledThisBattle
            + b.ScorePerHeroLost * HeroesLostThisBattle
            + b.ScorePerVillagerLost * VillagersLostThisBattle;
        return Mathf.Max(0, score);
    }

    /// <summary>Starts a fresh run. This manager is discarded so the reloaded scene builds a clean one.</summary>
    public void RestartGame() => LoadFresh(GameSceneName);

    public void ReturnToTitle() => LoadFresh(TitleSceneName);

    private void LoadFresh(string sceneName)
    {
        Instance = null;
        Destroy(gameObject);
        SceneManager.LoadScene(sceneName);
    }

    // --- EXTRACTION / RESCUE ---

    /// <summary>A hero drops up to <paramref name="maxVillagers"/> carried villagers at an evacuation point. Returns how many were saved.</summary>
    public int ProcessExtraction(UnitInstance unitInstance, int maxVillagers)
    {
        if (unitInstance == null) return 0;

        int rescued = unitInstance.DropOffRescued(maxVillagers);
        for (int i = 0; i < rescued; i++) RegisterVillagerEscaped();
        return rescued;
    }

    /// <summary>Called when a villager reaches an extraction point under its own power.</summary>
    public void ProcessVillagerEvacuation(UnitInstance villager)
    {
        if (villager == null || villager.IsExtracted) return;

        villager.IsExtracted = true;
        CombatManager.Instance?.HandleVillagerEvacuated(villager);

        if (villager.currentTile != null)
        {
            villager.currentTile.RemoveUnit();
            villager.currentTile = null;
        }

        RegisterVillagerEscaped();
        Destroy(villager.gameObject);
    }

    private void RegisterVillagerEscaped()
    {
        VillagersEscapedThisBattle++;
        OnSettlementChanged?.Invoke();
        if (VillagersEscapedThisBattle >= Balance.VillagersToWin) EndGame(true);
    }

    /// <summary>Called when a worker drops its load. Credits Settlement immediately.</summary>
    public void ProcessWorkerDeposit(bool isFood, int amount)
    {
        if (amount <= 0) return;
        if (isFood) AddFood(amount);
        else AddMaterials(amount);

        if (isFood) LastBattleReport.FoodGathered += amount;
        else LastBattleReport.MaterialsGathered += amount;
        OnSettlementChanged?.Invoke();
    }

    // --- RESOURCES ---

    /// <summary>Adds (or removes) Food, never below 0. Gains stop at the Storehouses cap; stock already above it is left alone.</summary>
    public void AddFood(int amount)
    {
        Settlement.Food = ApplyCap(Settlement.Food, amount, Research.MaxFood);
        OnSettlementChanged?.Invoke();
    }

    /// <summary>Adds (or removes) Materials with the same floor / cap rules as <see cref="AddFood"/>.</summary>
    public void AddMaterials(int amount)
    {
        Settlement.Materials = ApplyCap(Settlement.Materials, amount, Research.MaxMaterials);
        OnSettlementChanged?.Invoke();
    }

    private static int ApplyCap(int current, int amount, int cap)
    {
        int result = current + amount;
        if (amount > 0) result = Mathf.Min(result, Mathf.Max(cap, current));
        return Mathf.Max(0, result);
    }

    /// <summary>Lets ResearchManager refresh settlement readouts after it spends resources.</summary>
    public void NotifySettlementChanged() => OnSettlementChanged?.Invoke();

    /// <summary>Spends settlement Materials. Returns false, spending nothing, if short.</summary>
    public bool TrySpendMaterials(int amount) => TrySpend(0, amount);

    /// <summary>Spends settlement Food. Returns false, spending nothing, if short.</summary>
    public bool TrySpendFood(int amount) => TrySpend(amount, 0);

    /// <summary>Spends Food and Materials together, all or nothing.</summary>
    public bool TrySpend(int food, int materials)
    {
        if (food < 0 || materials < 0 || Settlement.Food < food || Settlement.Materials < materials) return false;
        Settlement.Food -= food;
        Settlement.Materials -= materials;
        OnSettlementChanged?.Invoke();
        return true;
    }

    // --- MORALE ---

    /// <summary>Changes morale (clamped 0..100) and ends the run in defeat when it reaches 0.</summary>
    public void AdjustMorale(int delta)
    {
        if (GameEnded) return;
        Settlement.Morale = Mathf.Clamp(Settlement.Morale + delta, 0, 100);
        OnSettlementChanged?.Invoke();
        if (Settlement.Morale <= 0) EndGame(false);
    }

    /// <summary>An extraction point vanished with <paramref name="unusedSlots"/> places still open.</summary>
    public void RegisterExtractionWasted(int unusedSlots)
    {
        if (unusedSlots > 0) AdjustMorale(-unusedSlots * Balance.UnusedExtractionSlotMoralePenalty);
    }

    // --- CASUALTIES / KILLS ---

    /// <summary>Call from CombatManager when a hero dies.</summary>
    public void RegisterCasualty(Unit unit)
    {
        if (unit != null)
        {
            FullRoster.Remove(unit);
            ActiveTeam.Remove(unit);

            UnitReportEntry entry = LastBattleReport.GetOrCreate(unit);
            if (entry != null) entry.Outcome = BattleOutcome.Died;
        }
        HeroesLostThisBattle++;
        AdjustMorale(-Balance.HeroDeathMoralePenalty);
    }

    /// <summary>Call whenever a villager takes a hit.</summary>
    public void RegisterVillagerHit() => AdjustMorale(-Balance.VillagerHitMoralePenalty);

    /// <summary>Call from CombatManager when a villager dies.</summary>
    public void RegisterVillagerDeath()
    {
        VillagersLostThisBattle++;
        LastBattleReport.WorkersLost++;
        AdjustMorale(-Balance.VillagerDeathMoralePenalty);
    }

    /// <summary>Call from CombatManager when an enemy unit is killed.</summary>
    public void RegisterEnemyKilled()
    {
        EnemiesKilledThisBattle++;
        AdjustMorale(Balance.EnemyKillMoraleBonus);
    }

    /// <summary>Call when a wall is destroyed.</summary>
    public void RegisterWallDestroyed() => AdjustMorale(-Balance.WallDestroyedMoralePenalty);
}
