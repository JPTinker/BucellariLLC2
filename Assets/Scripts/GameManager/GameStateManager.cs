using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameStateManager : MonoBehaviour
{

    [System.Serializable]
    public class PlayerResources
    {
        public int Gold = 100;
        public int CurrentStageIndex = 1;
        // Add unlocked craftables, metadata, progression flags, etc.
    }

    /// <summary>
    /// The settlement's persistent economy. This is what the Decision (planning)
    /// screen reads and writes. Population (Units) isn't stored here - it's
    /// FullRoster.Count. Villagers ARE stored here: unlike Units they aren't
    /// individual Unit records with HP/Attack, just a headcount resource (like
    /// Food/Materials) that gets sent out to labor directives each cycle.
    /// </summary>
    [System.Serializable]
    public class SettlementResources
    {
        public int Food = 200;
        public int Materials = 185;
        [Range(0, 100)] public int Morale = 84;
        public int Villagers = 5;          // current available villager headcount (labor pool)
        public int VillagerCapacity = 10;  // max Villagers the settlement can support
        public int UnitCapacity = 5;       // max Units (FullRoster) the settlement can support
    }

    /// <summary>
    /// Which directive a block of Units/Villagers is assigned to for the upcoming
    /// cycle. Mirrors the four cards on the Decision screen 1:1. Vanguard draws
    /// from Units (Population); Scavenge/Harvest/Expansion draw from Villagers.
    /// </summary>
    public enum Directive { Vanguard, Scavenge, Harvest, Expansion }

    /// <summary>
    /// The player's chosen course for the upcoming cycle, picked on the
    /// Campaign Map tab. Mutually exclusive - one applies per cycle, layered
    /// on top of whatever Directive allocation is chosen on the Decision tab.
    /// Consumed by ComputeForecast()/ExecuteCycle(), then reset to Travel once
    /// the cycle commits so Stay Put/Rest have to be consciously repicked
    /// rather than silently repeating.
    /// </summary>
    public enum CampaignAction { Travel, StayPut, Rest }

    /// <summary>
    /// The player's in-progress, not-yet-committed allocation for this planning
    /// cycle. Adjusted live by the Decision screen via AdjustAllocation(), applied
    /// to Settlement by ExecuteCycle(). ScavengeUnits/HarvestUnits/ExpansionUnits
    /// are headcounts of Villagers despite the field name (kept for UXML/binding
    /// compatibility). ExpansionUnits (Build a Ship) is special: TryAdjustAllocation
    /// only ever lands it on 0 or Balance.ShipVillagerCost - it's a discrete,
    /// fixed-cost directive, not a per-villager scaling one.
    ///
    /// Vanguard has no field here - it's derived from ActiveTeam.Count (the
    /// units picked on the Roster tab), not manually adjustable. See
    /// TryAdjustAllocation() and ComputeForecast()/ExecuteCycle().
    /// </summary>
    [System.Serializable]
    public class CycleAllocation
    {
        public int ScavengeUnits;
        public int HarvestUnits;
        public int ExpansionUnits;

        public int Get(Directive d) => d switch
        {
            Directive.Scavenge => ScavengeUnits,
            Directive.Harvest => HarvestUnits,
            Directive.Expansion => ExpansionUnits,
            _ => 0
        };

        public void Set(Directive d, int value)
        {
            switch (d)
            {
                case Directive.Scavenge: ScavengeUnits = value; break;
                case Directive.Harvest: HarvestUnits = value; break;
                case Directive.Expansion: ExpansionUnits = value; break;
            }
        }

        public void Reset(int scavenge = 0, int harvest = 0, int expansion = 0)
        {
            ScavengeUnits = scavenge;
            HarvestUnits = harvest;
            ExpansionUnits = expansion;
        }
    }

    /// <summary>
    /// Tunable rates for the planning economy. Exposed so designers can
    /// rebalance from the Inspector instead of editing code.
    /// </summary>
    [System.Serializable]
    public class CycleBalanceConfig
    {
        [Header("Food costs (paid when the cycle executes)")]
        public int FoodCostPerVanguardUnit = 20;  // Food spent per Unit sent to battle (Vanguard)
        public int FoodCostPerVillagerSent = 10;  // Food spent per Villager sent to ANY labor directive

        [Header("Hunt and Gather (Food reward)")]
        public float FoodPerHarvestVillager = 30f;

        [Header("Search for Materials (Scrap reward)")]
        public float MaterialsPerScavengeVillager = 10f;

        [Header("Build a Ship - discrete, fixed-cost directive (not per-villager)")]
        public int ShipVillagerCost = 8;         // villagers consumed/crewed when the ship completes
        public int ShipMaterialsCost = 150;
        public int ShipUnitCapacityGain = 5;
        public int ShipVillagerCapacityGain = 10;

        [Header("Morale - per-cycle allocation costs")]
        public float MoralePenaltyPerVanguardUnit = 1f;   // -1% per Unit sent to battle
        public float MoralePenaltyPerVillagerSent = 3f;   // -3% per Villager sent to any labor directive

        [Header("Morale - post-battle bonuses/penalties")]
        public float EnemyKilledMoraleBonus = 0.25f;      // per confirmed kill, applied post-battle
        public int VillagerSavedMoraleBonus = 5;          // per rescued villager extracted, applied post-battle
        public int UnitLostMoralePenalty = 10;            // per casualty, applied post-battle
    }

    /// <summary>
    /// Read-only projection of what ExecuteCycle() would do if called right now,
    /// with the current CurrentAllocation. The Decision screen renders this
    /// every time the player taps a stepper - call ComputeForecast(), don't
    /// duplicate this math in the UI layer.
    /// </summary>
    public struct CycleForecast
    {
        public int IdleUnits;
        public int VillagersSent;
        public int IdleVillagers;
        public int FoodDelta;
        public int MaterialsDelta;
        public int MoraleDelta;
        public bool WillBuildShip;
        public bool IsOverBudget; // a ship is queued but Settlement.Materials can't cover its cost
    }

    /// <summary>
    /// Snapshot of a single unit's level-up, captured at the moment the level-up
    /// happens (in combat, on the results screen, wherever your XP logic lives).
    /// The planning screen drains PendingLevelUps and reads this struct to know
    /// what to show on the level-up card - it doesn't recompute anything itself.
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

    public int testing =1;

    [Header("Available Unit Templates")]
    public List<UnitData> AvailablePlayerArchetypes; // Drag 'Knight' and 'Warrior' assets here

    // Maximum number of units the player can bring into a single battle.
    public const int MaxTeamSize = 5;

    [Header("Persistent Data")]
    public PlayerResources Resources = new PlayerResources();

    [Header("Settlement / Planning Phase")]
    public SettlementResources Settlement = new SettlementResources();
    public CycleAllocation CurrentAllocation = new CycleAllocation();
    public CycleBalanceConfig Balance = new CycleBalanceConfig();

    [Header("Campaign Map")]
    public CampaignAction SelectedCampaignAction = CampaignAction.Travel;
    public bool HasSelectedCampaignAction { get; private set; }
    /// <summary>Cycles of travel from the start to the Evacuation Zone (used to place the boat on the Voyage screen).</summary>
    public const int EvacuationCyclesTotal = 10;
    public int EvacuationCyclesRemaining = EvacuationCyclesTotal;

    /// <summary>Set by ExecuteCycle when the course was Travel; consumed by the Voyage scene.</summary>
    public bool HasPendingVoyage { get; private set; }
    public int VoyageFromRemaining { get; private set; }
    public int VoyageToRemaining { get; private set; }
    public const string VoyageSceneName = "Voyage";
    public string BattleSceneAfterVoyage { get; private set; } = "BattlePhase";

    /// <summary>Called by the Voyage scene once its sailing animation is done.</summary>
    public void ClearPendingVoyage() => HasPendingVoyage = false;

    // Roster of all owned units (the pool shown on the team-selection screen).
    public List<Unit> FullRoster = new List<Unit>();

    // Units the player has picked to bring into the current battle (max MaxTeamSize).
    public List<Unit> ActiveTeam = new List<Unit>();

    // Units added to FullRoster that haven't been shown to the player yet via the
    // "new unit" reveal animation on the team-selection screen. The UI drains this
    // queue and calls ClearPendingReveal() once it has shown them all.
    public List<Unit> PendingReveal = new List<Unit>();

    // Units that leveled up (e.g. during the last battle) and haven't had their
    // "level up" animation shown yet. The UI drains this queue with QueueLevelUp
    // and calls ClearPendingLevelUps() once it has shown them all. Populate it by
    // calling QueueLevelUp() from wherever your XP/leveling logic lives.
    public Queue<LevelUpInfo> PendingLevelUps = new Queue<LevelUpInfo>();

    // When populated, the team-selection screen shows two unit candidates. The
    // player can also choose to add the saved person as a Villager instead.
    public List<UnitData> PendingDraftOptions = new List<UnitData>();
    // Rarity pre-rolled for each draft option (same index) so the card can show
    // it before the pick, and the recruited unit matches what was shown.
    public List<UnitRarity> PendingDraftRarities = new List<UnitRarity>();
    public int PendingDraftsToOffer { get; private set; }
    public bool CanDraftSavedVillager { get; private set; }

    public bool UnitsFull => FullRoster.Count >= Settlement.UnitCapacity;
    public bool VillagersFull => Settlement.Villagers >= Settlement.VillagerCapacity;
    /// <summary>True when a reward draft (unit cards and/or a villager card) is waiting to be shown.</summary>
    public bool HasPendingDraft => PendingDraftOptions.Count > 0 || CanDraftSavedVillager;

    [Header("Extraction & Combat Progress")]
    // Tracks units that successfully extracted during the current battle
    public List<Unit> ExtractedUnitsThisBattle = new List<Unit>();
    // Tracks villagers saved during the current battle
    public int SavedVillagersThisBattle = 0;
    // Tracks confirmed enemy kills during the current battle
    public int EnemiesKilledThisBattle = 0;
    // Tracks player units lost (died, not extracted) during the current battle
    public int CasualtiesThisBattle = 0;

    /// <summary>Per-unit record of the battle that just ended; read by the AfterAction scene.</summary>
    public BattleReport LastBattleReport { get; private set; } = new BattleReport();
    private bool _battleEnding;
    public const string AfterActionScene = "AfterAction";

    [Header("Last Cycle Debrief (read-only snapshot for UI)")]

    // Snapshot of the *This Battle counters, taken by ApplyPostBattleResults()

    // right before it clears them. The Decision screen reads these to show

    // "what just happened" - the *ThisBattle fields themselves are already

    // zero by the time any UI script's Start()/OnEnable() runs, since

    // ApplyPostBattleResults() fires from OnSceneLoaded ahead of the UI.

    public int LastCycleKills { get; private set; }

    public int LastCycleVillagersSaved { get; private set; }

    public int LastCycleCasualties { get; private set; }

    public int LastCycleMoraleDelta { get; private set; }



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Title screen's Continue button: restore the saved campaign instead
        // of rolling a fresh starter squad + draft.
        if (SaveSystem.LoadOnNextStart)
        {
            SaveSystem.LoadOnNextStart = false;
            if (SaveSystem.TryRead(out SaveData save) && ApplySaveData(save))
                return;
            Debug.LogWarning("GameStateManager: couldn't load save file, starting a new game instead.");
        }

        EnsurePlayerHasTeam();
    }
    private void OnEnable()
    {
        // Subscribe to the sceneLoaded event
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    // This method runs automatically when any scene loads
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"Scene loaded: {scene.name}");
        if (scene.name == "BattlePhase")
        {
            // Fresh report for the new battle; units add their entry when MapManager spawns them in Start().
            LastBattleReport = new BattleReport();
            ExtractedUnitsThisBattle.Clear();
            _battleEnding = false;
        }
        // Add your custom logic here (e.g., spawn player, update UI)
        if (scene.name == "DecisionPhase")
        {
            Debug.Log(" Scene has been confirmed");
            PlanningPhaseController planningController =
                GameObject.FindAnyObjectByType<PlanningPhaseController>();

            // Resolve morale from whatever happened in the battle just finished
            // (kills, casualties, villagers saved) BEFORE the player sees the
            // ledger, so the Morale value on screen already reflects last cycle.
            ApplyPostBattleResults();

            if (SavedVillagersThisBattle > 0){
                PendingDraftsToOffer = Mathf.Max(1, (SavedVillagersThisBattle + 2) / 3);
                OfferNextUnitDraft();
                planningController?.LevelChangeDraftOffer();
            }
            SavedVillagersThisBattle = 0;
        }   
    }
    /// <summary>
    /// Sets up whatever the current scene needs on startup:
    ///  - "DecisionPhase": make sure a roster exists, then offer a unit draft
    ///    so the player picks their recruit + team on the planning screen.
    ///  - "BattlePhase": the game was launched straight into combat (e.g. for
    ///    testing), so skip the planning screen and auto-pick a random team.
    ///  - anything else: just make sure the player has a roster.
    /// </summary>
    public void EnsurePlayerHasTeam(int startingCount = 2)
    {
        // Remove invalid entries left by older saves that stored archetype assets
        // directly instead of owned Unit records.
        FullRoster.RemoveAll(unit => unit == null || unit.Archetype == null);
        PendingReveal.RemoveAll(unit => unit == null || unit.Archetype == null);
        for (int i = PendingDraftOptions.Count - 1; i >= 0; i--)
        {
            if (PendingDraftOptions[i] != null) continue;
            PendingDraftOptions.RemoveAt(i);
            if (i < PendingDraftRarities.Count) PendingDraftRarities.RemoveAt(i);
        }

        string sceneName = SceneManager.GetActiveScene().name;

        if (sceneName == "DecisionPhase")
        {
            if (FullRoster.Count == 0) GrantStarterUnits(startingCount);
            OfferUnitDraft(2, allowVillagerChoice: true);
            return;
        }

        if (sceneName == "BattlePhase")
        {
            if (FullRoster.Count == 0) GrantStarterUnits(startingCount);
            // Preserve the team chosen on the planning screen. Only auto-pick
            // when the battle scene was entered without an active team.
            if (ActiveTeam.Count == 0)
                AutoSelectRandomTeam(startingCount);
            return;
        }

        // Any other scene (menu, bootstrap, etc.): just make sure a roster exists.
        if (FullRoster.Count == 0) GrantStarterUnits(startingCount);
    }

    /// <summary>
    /// Grants `count` random starter units into FullRoster. These skip the
    /// "new unit" reveal animation since they're the player's default squad,
    /// not something they just earned. Starter units are always Legendary.
    /// </summary>
    private void GrantStarterUnits(int count)
    {
        if (AvailablePlayerArchetypes == null || AvailablePlayerArchetypes.Count == 0)
        {
            Debug.LogError("No UnitData archetypes assigned in GameStateManager!");
            return;
        }

        List<UnitData> starters = new List<UnitData>();
        for (int i = 0; i < count; i++)
        {
            // Pick randomly between Knight and Warrior
            int randomIndex = UnityEngine.Random.Range(0, AvailablePlayerArchetypes.Count);
            starters.Add(AvailablePlayerArchetypes[randomIndex]);
        }

        AddUnitsToRoster(starters, flagAsNew: false, rarity: UnitRarity.Legendary);
    }

    /// <summary>
    /// Picks up to `count` random units already in FullRoster and sets them as
    /// the ActiveTeam. Used when a scene skips the planning screen entirely
    /// (e.g. launching straight into BattlePhase for testing).
    /// </summary>
    private void AutoSelectRandomTeam(int count)
    {
        if (FullRoster.Count == 0)
        {
            Debug.LogWarning("AutoSelectRandomTeam: FullRoster is empty, nothing to select.");
            return;
        }

        List<Unit> pool = new List<Unit>(FullRoster);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int take = Mathf.Min(count, pool.Count);
        SetActiveTeam(pool.GetRange(0, take));
    }

    public void OfferUnitDraft(int optionCount = 2, bool allowVillagerChoice = false)
    {
        Debug.Log("Offering draft");
        // Capacity gating: full roster -> villager card only; full villagers ->
        // unit cards only; both full -> nothing is offered.
        CanDraftSavedVillager = allowVillagerChoice && !VillagersFull;
        if (UnitsFull)
        {
            PendingDraftOptions.Clear();
            PendingDraftRarities.Clear();
            return;
        }
        if (AvailablePlayerArchetypes == null || AvailablePlayerArchetypes.Count == 0)
        {
            Debug.LogError("No UnitData archetypes assigned in GameStateManager!");
            return;
        }

        // Filter out any unassigned (null) slots in the archetype list before
        // shuffling — an empty Inspector slot here would otherwise ride through
        // into PendingDraftOptions and NRE when the UI reads its stats.
        List<UnitData> pool = new List<UnitData>();
        foreach (var archetype in AvailablePlayerArchetypes)
        {
            if (archetype != null) pool.Add(archetype);
        }

        if (pool.Count == 0)
        {
            Debug.LogError("AvailablePlayerArchetypes only contains null/unassigned entries!");
            return;
        }

        // Shuffle (Fisher-Yates) and take the first N so options are distinct
        // rather than possibly offering duplicates.
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int count = Mathf.Min(optionCount, pool.Count);
        PendingDraftOptions = pool.GetRange(0, count);
        PendingDraftRarities.Clear();
        for (int i = 0; i < count; i++) PendingDraftRarities.Add(UnitRarityTable.Roll());
    }

    /// <summary>The pre-rolled rarity for the draft option at <paramref name="index"/>.</summary>
    public UnitRarity GetDraftRarity(int index)
    {
        return index >= 0 && index < PendingDraftRarities.Count ? PendingDraftRarities[index] : UnitRarity.Common;
    }

    public void OfferNextUnitDraft()
    {
        // Keep consuming offers until one actually has something to show; if the
        // settlement is full on both counts the remaining rewards are forfeited.
        while (PendingDraftsToOffer > 0)
        {
            OfferUnitDraft(2, allowVillagerChoice: true);
            PendingDraftsToOffer--;
            if (HasPendingDraft) return;
        }
    }

    /// <summary>Why <paramref name="unit"/> can't be dismissed, or null when it can.</summary>
    public bool CanDismissUnit(Unit unit, out string reason)
    {
        reason = null;
        if (unit == null || !FullRoster.Contains(unit)) reason = "Unit not found.";
        else if (FullRoster.Count <= 1) reason = "You must keep at least one unit.";
        else if (VillagersFull) reason = "Villager capacity is full.";
        return reason == null;
    }

    /// <summary>Removes a unit from the roster permanently and converts them to a Villager.</summary>
    public bool DismissUnitForVillager(Unit unit)
    {
        if (!CanDismissUnit(unit, out _)) return false;
        FullRoster.Remove(unit);
        ActiveTeam.Remove(unit);
        PendingReveal.Remove(unit);
        Settlement.Villagers++;
        return true;
    }

    public void ResolveUnitDraft(UnitData chosen)
    {
        ResolveUnitDraft(chosen, GetDraftRarity(PendingDraftOptions.IndexOf(chosen)));
    }

    public void ResolveUnitDraft(UnitData chosen, UnitRarity rarity)
    {
        PendingDraftOptions.Clear();
        PendingDraftRarities.Clear();
        CanDraftSavedVillager = false;
        if (chosen == null) return;
        AddUnitToRoster(chosen, flagAsNew: true, rarity: rarity);
    }

    public void ResolveVillagerDraft()
    {
        if (!CanDraftSavedVillager)
        {
            Debug.LogWarning("ResolveVillagerDraft called when no saved-villager draft choice is active.");
            return;
        }

        PendingDraftOptions.Clear();
        PendingDraftRarities.Clear();
        CanDraftSavedVillager = false;
        Settlement.Villagers++;
    }

    /// <summary>
    /// Adds one or more units to the player's roster.
    /// </summary>
    /// <param name="flagAsNew">If true, the units are queued for the reveal animation
    /// the next time the team-selection screen is shown.</param>
    /// <param name="rarity">Rarity for every added unit; null rolls one per unit.</param>
    public void AddUnitsToRoster(List<UnitData> archetypes, bool flagAsNew = true, UnitRarity? rarity = null)
    {
        if (archetypes == null) return;
        foreach (UnitData archetype in archetypes)
        {
            if (archetype == null) continue;
            Unit unit = new Unit(archetype, rarity ?? UnitRarityTable.Roll());
            FullRoster.Add(unit);
            if (flagAsNew) PendingReveal.Add(unit);
        }
    }

    public void AddUnitToRoster(UnitData archetype, bool flagAsNew = true, UnitRarity? rarity = null)
    {
        AddUnitsToRoster(new List<UnitData> { archetype }, flagAsNew, rarity);
    }

    /// <summary>
    /// Called by the team-selection screen once it has finished animating in
    /// all pending new units, so they aren't shown again next visit.
    /// </summary>
    public void ClearPendingReveal()
    {
        PendingReveal.Clear();
    }

    /// <summary>
    /// Queues a level-up card to be shown on the planning screen the next time
    /// it's visited. Call this from wherever your XP/leveling logic actually
    /// applies the stat increase (e.g. end-of-combat XP tally, or the moment a
    /// unit crosses an XP threshold mid-battle) - pass the stats from just
    /// before and just after the level-up so the UI can show the delta without
    /// needing to know how leveling math works.
    /// </summary>
    public void QueueLevelUp(Unit unit, int oldLevel, int newLevel, int oldMaxHP, int newMaxHP, int oldAttack, int newAttack)
    {
        if (unit == null) return;
        PendingLevelUps.Enqueue(new LevelUpInfo(unit, oldLevel, newLevel, oldMaxHP, newMaxHP, oldAttack, newAttack));
    }

    /// <summary>
    /// Called by the team-selection screen once it has finished animating
    /// through all pending level-ups, so they aren't shown again next visit.
    /// </summary>
    public void ClearPendingLevelUps()
    {
        PendingLevelUps.Clear();
    }

    /// <summary>
    /// Validates and commits the player's chosen battle team.
    /// </summary>
    public bool SetActiveTeam(List<Unit> selected)
    {
        if (selected == null || selected.Count == 0)
        {
            Debug.LogWarning("Cannot set an empty battle team.");
            return false;
        }

        if (selected.Count > MaxTeamSize)
        {
            Debug.LogWarning($"Cannot select more than {MaxTeamSize} units for battle.");
            return false;
        }

        ActiveTeam = new List<Unit>(selected);
        return true;
    }

    // --- GAME / SCENE STATE ---
    public enum GameState
    {
        TeamManagement,
        InCombat,
        GameOver,
        StartMenu
    }

    public GameState CurrentState { get; private set; } = GameState.TeamManagement;

    // Events for other systems to listen to
    public static event Action<GameState> OnGameStateChanged;

    // --- SCENE TRANSITION METHODS ---

    /// <summary>
    /// Loads the combat scene and initializes battle state.
    /// </summary>
    public void LoadCombatMap(string sceneName = "BattlePhase")
    {
        if (ActiveTeam.Count == 0)
        {
            Debug.LogWarning("Cannot start battle without units in ActiveTeam!");
            return;
        }

        SetState(GameState.InCombat);
        SceneManager.LoadScene(sceneName);
    }

    private void SetState(GameState newState)
    {
        CurrentState = newState;
        OnGameStateChanged?.Invoke(newState);
    }

    public void ProcessExtraction(UnitInstance unitInstance)
    {
        if (unitInstance == null) return;

        // 1. Set the unit as saved / extracted
        unitInstance.IsExtracted = true;

        // Write the battle's damage back onto the persistent roster record
        // before the UnitInstance is destroyed, so it carries over into the
        // next battle instead of resetting to full - only Rest heals it back up.
        Unit persistent = unitInstance.PersistentUnit;
        if (persistent != null)
        {
            persistent.CurrentHP = unitInstance.currentHealth;
            if (!ExtractedUnitsThisBattle.Contains(persistent))
                ExtractedUnitsThisBattle.Add(persistent);
        }

        // 2. If the unit was carrying / saved any villagers, tally them up
        int rescued = 0;
        if (unitInstance.rescuedUnitData[0] != null)
        {
            Debug.Log("Villager has been saved");
            rescued++;
        }
        if (unitInstance.rescuedUnitData[1] != null)
        {
            rescued++;
        }
        SavedVillagersThisBattle += rescued;

        UnitReportEntry entry = LastBattleReport.GetOrCreate(persistent);
        if (entry != null)
        {
            entry.Outcome = BattleOutcome.Extracted;
            entry.RescuedVillagers = rescued;
            entry.EndXP = unitInstance.Experience;
        }

        // 3. Remove the unit from the battlefield. HandleUnitExtract may end the
        // battle once the last player unit is gone (see EndBattle).
        CombatManager.Instance.HandleUnitExtract(unitInstance);
        Destroy(unitInstance.gameObject);

        Debug.Log($"GameStateManager: Unit {unitInstance.unitName} successfully extracted!");
    }

    /// <summary>
    /// Finishes the battle: seals the report and hands off to the AfterAction
    /// scene. Called by CombatManager when no player units remain on the map.
    /// </summary>
    public void EndBattle(float delaySeconds = 0.8f)
    {
        if (_battleEnding) return;
        _battleEnding = true;

        LastBattleReport.EnemiesKilled = EnemiesKilledThisBattle;
        LastBattleReport.VillagersSaved = SavedVillagersThisBattle;
        LastBattleReport.Won = ExtractedUnitsThisBattle.Count > 0;

        // A wiped squad would leave the campaign with nobody to field.
        if (FullRoster.Count == 0) GrantStarterUnits(2);

        SetState(GameState.InCombat);
        StartCoroutine(LoadSceneAfterDelay(AfterActionScene, delaySeconds));
    }

    private System.Collections.IEnumerator LoadSceneAfterDelay(string sceneName, float delay)
    {
        // Lets the last hit / extract play out before the scene swap.
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>Called by VillagerAIController when a villager reaches the evacuation zone under its own power.</summary>
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

        SavedVillagersThisBattle++;
        Debug.Log($"GameStateManager: {villager.unitName} reached the evacuation zone and was saved!");

        Destroy(villager.gameObject);
    }

    // ============================================================
    // SETTLEMENT / DECISION SCREEN
    // ============================================================
    // Everything below backs the Decision (planning) tab: the player sends
    // Units (Vanguard) into battle and Villagers into three labor directives
    // (Scavenge/Search for Materials, Harvest/Hunt and Gather, Expansion/Build
    // a Ship), previews the effect on Food, Materials and Morale, then commits
    // with ExecuteCycle(). The UI should call TryAdjustAllocation() and
    // ComputeForecast() on every stepper tap and never do this math itself.

    /// <summary>Total Units currently owned (population), independent of housing.</summary>
    public int Population => FullRoster.Count;

    /// <summary>Units not currently picked for the Vanguard team (ActiveTeam, chosen on the Roster tab).</summary>
    public int IdleUnits => Mathf.Max(0, Population - ActiveTeam.Count);

    /// <summary>Villagers not currently assigned to a labor directive this cycle.</summary>
    public int IdleVillagers => Mathf.Max(0, Settlement.Villagers - VillagersSent);

    /// <summary>Total Villagers committed across Scavenge/Harvest/Expansion this cycle.</summary>
    public int VillagersSent => CurrentAllocation.ScavengeUnits + CurrentAllocation.HarvestUnits + CurrentAllocation.ExpansionUnits;

    public void SelectCampaignAction(CampaignAction action)
    {
        SelectedCampaignAction = action;
        HasSelectedCampaignAction = true;
    }

    /// <summary>
    /// Attempts to move Villagers (Scavenge/Harvest) into/out of a directive, one
    /// at a time. Fails silently (returns false) rather than throwing, so the UI
    /// can just no-op a stepper tap that would go negative or exceed the idle
    /// pool - mirrors the mock's adjustUnits(). Expansion (Build a Ship) is
    /// routed to TryToggleShipBuild instead, since it's an all-or-nothing
    /// fixed-cost directive rather than a per-villager one. Vanguard is rejected
    /// outright - it's derived from ActiveTeam (picked on the Roster tab), not a
    /// steppable headcount.
    /// </summary>
    public bool TryAdjustAllocation(Directive directive, int delta)
    {
        if (directive == Directive.Vanguard) return false;
        if (directive == Directive.Expansion) return TryToggleShipBuild(delta);

        int idle = IdleVillagers;
        if (delta > 0 && idle < delta) return false;

        int newValue = CurrentAllocation.Get(directive) + delta;
        if (newValue < 0) return false;

        CurrentAllocation.Set(directive, newValue);
        return true;
    }

    /// <summary>
    /// Build a Ship only ever costs exactly Balance.ShipVillagerCost Villagers -
    /// there's no partial commitment. A positive delta queues it (if not already
    /// queued and enough Villagers are idle); a non-positive delta cancels it.
    /// </summary>
    private bool TryToggleShipBuild(int delta)
    {
        int current = CurrentAllocation.ExpansionUnits;
        int cost = Balance.ShipVillagerCost;

        if (delta > 0)
        {
            if (current != 0) return false;
            if (IdleVillagers < cost) return false;
            CurrentAllocation.ExpansionUnits = cost;
            return true;
        }

        if (current == 0) return false;
        CurrentAllocation.ExpansionUnits = 0;
        return true;
    }

    public void ResetAllocation() => CurrentAllocation.Reset();

    /// <summary>
    /// Pure projection - does not mutate Settlement. Call this after every
    /// allocation change to refresh the "Projected Cycle Outcomes" panel.
    /// </summary>
    public CycleForecast ComputeForecast()
    {
        var a = CurrentAllocation;
        var b = Balance;

        // Vanguard headcount is derived from ActiveTeam (units picked on the
        // Roster tab), not a manually steppable allocation field.
        int vanguardUnits = ActiveTeam.Count;
        int villagersSent = VillagersSent;
        bool willBuildShip = a.ExpansionUnits >= b.ShipVillagerCost;

        // Stay Put (Campaign Map): the settlement isn't moving this cycle, so
        // labor directives work the site twice as hard.
        float yieldMultiplier = SelectedCampaignAction == CampaignAction.StayPut ? 2f : 1f;

        int foodDelta = Mathf.RoundToInt(
            a.HarvestUnits * b.FoodPerHarvestVillager * yieldMultiplier
            - vanguardUnits * b.FoodCostPerVanguardUnit
            - villagersSent * b.FoodCostPerVillagerSent);

        int materialsDelta = Mathf.RoundToInt(
            a.ScavengeUnits * b.MaterialsPerScavengeVillager * yieldMultiplier
            - (willBuildShip ? b.ShipMaterialsCost : 0));

        float moraleDelta =
            - vanguardUnits * b.MoralePenaltyPerVanguardUnit
            - villagersSent * b.MoralePenaltyPerVillagerSent;

        bool isOverBudget = willBuildShip && Settlement.Materials < b.ShipMaterialsCost;

        return new CycleForecast
        {
            IdleUnits = IdleUnits,
            VillagersSent = villagersSent,
            IdleVillagers = IdleVillagers,
            FoodDelta = foodDelta,
            MaterialsDelta = materialsDelta,
            MoraleDelta = Mathf.RoundToInt(moraleDelta),
            WillBuildShip = willBuildShip,
            IsOverBudget = isOverBudget
        };
    }

    /// <summary>
    /// Commits the current allocation: applies the forecasted Food/Materials/
    /// Morale deltas to Settlement, completes a queued ship (spending its
    /// Villagers/Materials and growing UnitCapacity/VillagerCapacity), sends the
    /// Vanguard block into ActiveTeam, and hands off to combat. Returns false
    /// (and applies nothing) if a queued ship can't be paid for, so the UI
    /// should disable the "Execute Cycle" button whenever forecast.IsOverBudget
    /// is true rather than relying on this.
    /// </summary>
    public bool ExecuteCycle(string battleSceneName = "BattlePhase")
    {
        var forecast = ComputeForecast();
        if (forecast.IsOverBudget)
        {
            Debug.LogWarning("ExecuteCycle: not enough Materials to complete the queued ship, allocation not applied.");
            return false;
        }

        Settlement.Food = Mathf.Max(0, Settlement.Food + forecast.FoodDelta);
        Settlement.Materials = Mathf.Max(0, Settlement.Materials + forecast.MaterialsDelta);
        Settlement.Morale = Mathf.Clamp(Settlement.Morale + forecast.MoraleDelta, 0, 100);

        if (forecast.WillBuildShip)
        {
            // The 8 villagers crew the ship and leave the settlement's labor pool
            // for good - only the capacity they unlock stays behind.
            Settlement.Villagers = Mathf.Max(0, Settlement.Villagers - Balance.ShipVillagerCost);
            Settlement.UnitCapacity += Balance.ShipUnitCapacityGain;
            Settlement.VillagerCapacity += Balance.ShipVillagerCapacityGain;
        }

        // Apply this cycle's Campaign Map course. Stay Put's yield doubling
        // already happened above (it's baked into the forecast); Rest and
        // Travel have their own effects to commit here.
        // Rest heals everyone and skips this cycle's battle entirely.
        bool skipBattle = false;
        switch (SelectedCampaignAction)
        {
            case CampaignAction.Rest:
                RestAllUnits();
                skipBattle = true;
                break;
            case CampaignAction.Travel:
                VoyageFromRemaining = EvacuationCyclesRemaining;
                EvacuationCyclesRemaining = Mathf.Max(0, EvacuationCyclesRemaining - 1);
                VoyageToRemaining = EvacuationCyclesRemaining;
                HasPendingVoyage = true;
                break;
        }
        SelectedCampaignAction = CampaignAction.Travel;
        HasSelectedCampaignAction = false;

        // Hand the Vanguard block off to combat - ActiveTeam already holds
        // whichever units are picked on the Roster tab (kept live in sync by
        // PlanningPhaseController.ToggleSelection).
        if (HasPendingVoyage)
        {
            // Show the boat sailing first; the Voyage scene then loads combat
            // (BattleSceneAfterVoyage) or plays the victory screen on arrival.
            BattleSceneAfterVoyage = battleSceneName;
            SceneManager.LoadScene(VoyageSceneName);
        }
        else if (!skipBattle && ActiveTeam.Count > 0)
        {
            LoadCombatMap(battleSceneName);
        }

        CurrentAllocation.Reset(0, 0, 0);
        return true;
    }

    /// <summary>Spends settlement Materials (e.g. to build a wall mid-battle). Returns false, spending nothing, if short.</summary>
    public bool TrySpendMaterials(int amount)
    {
        if (amount < 0 || Settlement.Materials < amount) return false;
        Settlement.Materials -= amount;
        return true;
    }

    /// <summary>Heals every roster unit's persistent HP to full - the Campaign Map "Rest" action.</summary>
    private void RestAllUnits()
    {
        foreach (Unit unit in FullRoster)
            unit.CurrentHP = unit.MaxHP;
    }

    public void ApplyPostBattleResults()

    {

        int delta = Mathf.RoundToInt(

                    EnemiesKilledThisBattle * Balance.EnemyKilledMoraleBonus

                  + SavedVillagersThisBattle * Balance.VillagerSavedMoraleBonus

                  - CasualtiesThisBattle * Balance.UnitLostMoralePenalty);



        if (delta != 0)

            Settlement.Morale = Mathf.Clamp(Settlement.Morale + delta, 0, 100);



        // Snapshot before clearing so the Decision screen can still show

        // what happened, even though the *ThisBattle counters reset here.

        LastCycleKills = EnemiesKilledThisBattle;

        LastCycleVillagersSaved = SavedVillagersThisBattle;

        LastCycleCasualties = CasualtiesThisBattle;

        LastCycleMoraleDelta = delta;



        EnemiesKilledThisBattle = 0;

        CasualtiesThisBattle = 0;

        // Note: SavedVillagersThisBattle is intentionally left for the existing

        // draft-offer logic just below this call, and is cleared there.

    }



    // ============================================================
    // SAVE / LOAD
    // ============================================================

    /// <summary>Writes the current campaign to the single save slot. Called from the Decision tab's Save button.</summary>
    public bool SaveGame()
    {
        var data = new SaveData
        {
            Resources = Resources,
            Settlement = Settlement,
            CurrentAllocation = CurrentAllocation,
            SelectedCampaignAction = SelectedCampaignAction,
            HasSelectedCampaignAction = HasSelectedCampaignAction,
            EvacuationCyclesRemaining = EvacuationCyclesRemaining,
            PendingDraftRarities = new List<UnitRarity>(PendingDraftRarities),
            PendingDraftsToOffer = PendingDraftsToOffer,
            CanDraftSavedVillager = CanDraftSavedVillager,
            LastCycleKills = LastCycleKills,
            LastCycleVillagersSaved = LastCycleVillagersSaved,
            LastCycleCasualties = LastCycleCasualties,
            LastCycleMoraleDelta = LastCycleMoraleDelta
        };

        foreach (Unit unit in FullRoster)
        {
            if (unit != null && unit.Archetype != null) data.Roster.Add(UnitSaveData.From(unit));
        }
        foreach (Unit unit in PendingReveal)
        {
            if (unit != null) data.PendingRevealIds.Add(unit.InstanceID);
        }
        foreach (UnitData archetype in PendingDraftOptions)
        {
            data.PendingDraftArchetypes.Add(archetype != null ? archetype.name : null);
        }

        return SaveSystem.Write(data);
    }

    /// <summary>
    /// Restores a campaign from a SaveData. ActiveTeam is left empty on
    /// purpose: the Roster tab always starts with no units selected, so a
    /// restored team would be out of sync with what the player sees.
    /// </summary>
    private bool ApplySaveData(SaveData d)
    {
        if (d == null) return false;

        if (d.Resources != null) Resources = d.Resources;
        if (d.Settlement != null) Settlement = d.Settlement;
        CurrentAllocation = d.CurrentAllocation ?? new CycleAllocation();
        SelectedCampaignAction = d.SelectedCampaignAction;
        HasSelectedCampaignAction = d.HasSelectedCampaignAction;
        EvacuationCyclesRemaining = d.EvacuationCyclesRemaining;

        FullRoster.Clear();
        ActiveTeam.Clear();
        PendingReveal.Clear();
        PendingLevelUps.Clear();

        var byId = new Dictionary<string, Unit>();
        foreach (UnitSaveData saved in d.Roster)
        {
            UnitData archetype = FindArchetype(saved.ArchetypeName);
            if (archetype == null)
            {
                Debug.LogWarning($"GameStateManager: saved unit '{saved.UnitName}' uses unknown archetype '{saved.ArchetypeName}', skipping.");
                continue;
            }
            Unit unit = saved.ToUnit(archetype);
            FullRoster.Add(unit);
            if (!string.IsNullOrEmpty(unit.InstanceID)) byId[unit.InstanceID] = unit;
        }

        foreach (string id in d.PendingRevealIds)
        {
            if (id != null && byId.TryGetValue(id, out Unit unit)) PendingReveal.Add(unit);
        }

        PendingDraftOptions.Clear();
        PendingDraftRarities.Clear();
        for (int i = 0; i < d.PendingDraftArchetypes.Count; i++)
        {
            UnitData archetype = FindArchetype(d.PendingDraftArchetypes[i]);
            if (archetype == null) continue;
            PendingDraftOptions.Add(archetype);
            PendingDraftRarities.Add(i < d.PendingDraftRarities.Count ? d.PendingDraftRarities[i] : UnitRarity.Common);
        }
        PendingDraftsToOffer = d.PendingDraftsToOffer;
        CanDraftSavedVillager = d.CanDraftSavedVillager;

        LastCycleKills = d.LastCycleKills;
        LastCycleVillagersSaved = d.LastCycleVillagersSaved;
        LastCycleCasualties = d.LastCycleCasualties;
        LastCycleMoraleDelta = d.LastCycleMoraleDelta;

        // A save with an empty roster (e.g. every unit fell) still needs a squad to play.
        if (FullRoster.Count == 0) GrantStarterUnits(2);
        return true;
    }

    private UnitData FindArchetype(string archetypeName)
    {
        if (string.IsNullOrEmpty(archetypeName) || AvailablePlayerArchetypes == null) return null;
        foreach (UnitData archetype in AvailablePlayerArchetypes)
        {
            if (archetype != null && archetype.name == archetypeName) return archetype;
        }
        return null;
    }

    /// <summary>Call from CombatManager when a player unit dies (not extracted).</summary>
    public void RegisterCasualty(Unit unit)
    {
        if (unit != null)
        {
            FullRoster.Remove(unit);
            ActiveTeam.Remove(unit);
            PendingReveal.Remove(unit);

            UnitReportEntry entry = LastBattleReport.GetOrCreate(unit);
            if (entry != null) entry.Outcome = BattleOutcome.Died;
        }
        CasualtiesThisBattle++;
    }

    /// <summary>Call from CombatManager when an enemy unit is killed.</summary>
    public void RegisterEnemyKilled()
    {
        EnemiesKilledThisBattle++;
    }
}