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
    /// screen reads and writes. Population isn't stored here - it's FullRoster.Count,
    /// since "units" are just the roster you already track.
    /// </summary>
    [System.Serializable]
    public class SettlementResources
    {
        public int Food = 340;
        public int Materials = 185;
        [Range(0, 100)] public int Morale = 84;
        public int LaborCapacity = 50;   // total Labor AP available to spend each cycle
        public int HousingCapacity = 24; // max units the settlement can support before overcrowding
    }

    /// <summary>
    /// Which directive a block of units is assigned to for the upcoming cycle.
    /// Mirrors the four cards on the Decision screen 1:1.
    /// </summary>
    public enum Directive { Vanguard, Scavenge, Harvest, Expansion }

    /// <summary>
    /// The player's in-progress, not-yet-committed allocation for this planning
    /// cycle. Adjusted live by the Decision screen via AdjustAllocation(), applied
    /// to Settlement by ExecuteCycle().
    /// </summary>
    [System.Serializable]
    public class CycleAllocation
    {
        public int VanguardUnits;
        public int ScavengeUnits;
        public int HarvestUnits;
        public int ExpansionUnits;

        public int Assigned => VanguardUnits + ScavengeUnits + HarvestUnits + ExpansionUnits;

        public int Get(Directive d) => d switch
        {
            Directive.Vanguard => VanguardUnits,
            Directive.Scavenge => ScavengeUnits,
            Directive.Harvest => HarvestUnits,
            Directive.Expansion => ExpansionUnits,
            _ => 0
        };

        public void Set(Directive d, int value)
        {
            switch (d)
            {
                case Directive.Vanguard: VanguardUnits = value; break;
                case Directive.Scavenge: ScavengeUnits = value; break;
                case Directive.Harvest: HarvestUnits = value; break;
                case Directive.Expansion: ExpansionUnits = value; break;
            }
        }

        public void Reset(int vanguard = 4, int scavenge = 5, int harvest = 6, int expansion = 3)
        {
            VanguardUnits = vanguard;
            ScavengeUnits = scavenge;
            HarvestUnits = harvest;
            ExpansionUnits = expansion;
        }
    }

    /// <summary>
    /// Tunable per-unit rates for the planning economy. Exposed so designers can
    /// rebalance from the Inspector instead of editing code. Values default to
    /// whatever the Decision screen mock was using.
    /// </summary>
    [System.Serializable]
    public class CycleBalanceConfig
    {
        [Header("Food")]
        public float FoodPerHarvestUnit = 14.16f;
        public float FoodUpkeepBase = 42f;          // flat settlement upkeep per cycle
        public float FoodPerVanguardUnit = 3.75f;    // rations consumed by deployed units

        [Header("Materials")]
        public float MaterialsPerScavengeUnit = 12f;
        public float MaterialsPerExpansionUnit = 26.66f;

        [Header("Labor (AP)")]
        public int LaborPerScavengeUnit = 2;
        public int LaborPerHarvestUnit = 2;
        public int LaborPerExpansionUnit = 4;

        [Header("Housing")]
        public int HousingGainPerExpansionUnit = 4; // capacity added once a Hab-Pod cycle completes

        [Header("Morale - activity bonuses (met at end of cycle)")]
        public int VanguardMoraleThreshold = 4;
        public int VanguardMoraleBonus = 5;
        public int HarvestMoraleThreshold = 5;
        public int HarvestMoraleBonus = 2;
        public int ExpansionMoraleThreshold = 2;
        public int ExpansionMoraleBonus = 3;

        [Header("Morale - shortfall penalties")]
        public int FoodDeficitMoralePenalty = 8;         // flat hit if food delta this cycle is negative
        public int OvercrowdingMoralePenaltyPerUnit = 3;  // per unit over HousingCapacity
        public int UnitLostMoralePenalty = 10;            // per casualty, applied post-battle
        public int EnemyKilledMoraleBonus = 2;            // per confirmed kill, applied post-battle
        public int VillagerSavedMoraleBonus = 5;          // per villager extracted, applied post-battle
    }

    /// <summary>
    /// Read-only projection of what ExecuteCycle() would do if called right now,
    /// with the current CurrentAllocation. The Decision screen renders this
    /// every time the player taps a stepper - call ComputeForecast(), don't
    /// duplicate this math in the UI layer.
    /// </summary>
    public struct CycleForecast
    {
        public int AssignedUnits;
        public int IdleUnits;
        public int FoodDelta;
        public int MaterialsDelta;
        public int LaborUsed;
        public int LaborRemaining;
        public int MoraleDelta;
        public bool IsOverLaborBudget;
        public bool WillOvercrowd; // population + would-be idle... see ComputeForecast for definition
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

    // When populated, the team-selection screen shows a "pick 1 of N" draft overlay
    // using these candidates. Call OfferUnitDraft() to populate it; the UI calls
    // ResolveUnitDraft() with the player's choice.
    public List<UnitData> PendingDraftOptions = new List<UnitData>();
    public int PendingDraftsToOffer { get; private set; }

    [Header("Extraction & Combat Progress")]
    // Tracks units that successfully extracted during the current battle
    public List<Unit> ExtractedUnitsThisBattle = new List<Unit>();
    // Tracks villagers saved during the current battle
    public int SavedVillagersThisBattle = 0;
    // Tracks confirmed enemy kills during the current battle
    public int EnemiesKilledThisBattle = 0;
    // Tracks player units lost (died, not extracted) during the current battle
    public int CasualtiesThisBattle = 0;

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
        // Add your custom logic here (e.g., spawn player, update UI)
        if (scene.name == "DecisionPhase")
        {
            Debug.Log(" Scene has been confirmed");
            RosterPhaseController planningController =
                GameObject.FindAnyObjectByType<RosterPhaseController>();

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
        PendingDraftOptions.RemoveAll(archetype => archetype == null);

        string sceneName = SceneManager.GetActiveScene().name;

        if (sceneName == "DecisionPhase")
        {
            if (FullRoster.Count == 0) GrantStarterUnits(startingCount);
            OfferUnitDraft(3);
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
    /// not something they just earned.
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

        AddUnitsToRoster(starters, flagAsNew: false);
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

    public void OfferUnitDraft(int optionCount = 3)
    {
        Debug.Log("Offering draft");
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
    }

    public void OfferNextUnitDraft()
    {
        if (PendingDraftsToOffer <= 0) return;

        OfferUnitDraft(3);
        PendingDraftsToOffer--;
    }

    public void ResolveUnitDraft(UnitData chosen)
    {
        PendingDraftOptions.Clear();
        if (chosen == null) return;
        AddUnitToRoster(chosen, flagAsNew: true);
    }

    /// <summary>
    /// Adds one or more units to the player's roster.
    /// </summary>
    /// <param name="flagAsNew">If true, the units are queued for the reveal animation
    /// the next time the team-selection screen is shown.</param>
    public void AddUnitsToRoster(List<UnitData> archetypes, bool flagAsNew = true)
    {
        if (archetypes == null) return;
        foreach (UnitData archetype in archetypes)
        {
            if (archetype == null) continue;
            Unit unit = new Unit(archetype);
            FullRoster.Add(unit);
            if (flagAsNew) PendingReveal.Add(unit);
        }
    }

    public void AddUnitToRoster(UnitData archetype, bool flagAsNew = true)
    {
        AddUnitsToRoster(new List<UnitData> { archetype }, flagAsNew);
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

    /// <summary>
    /// Call this after combat ends to return to the management screen.
    /// </summary>
    public void ReturnToTeamManager(bool playerWon, int goldEarned = 0, string sceneName = "TeamManager")
    {
        if (playerWon)
        {
            Resources.Gold += goldEarned;
            Resources.CurrentStageIndex++;
        }


        SetState(GameState.TeamManagement);
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
        CombatManager.Instance.HandleUnitExtract(unitInstance);
        // If your UnitInstance maps back to a persistent Unit data model, 
        // store or track it here so it's preserved for the next screen.
        // (Assuming UnitInstance has a reference to its underlying persistent 'Unit' or data)
        // ExtractedUnitsThisBattle.Add(unitInstance.persistentUnitData);

        // 3. If the unit was carrying / saved any villagers, tally them up
        // (Adjust property name if your UnitInstance tracks rescued villagers differently)
        if (unitInstance.rescuedUnitData[0] != null)
        {
            Debug.Log("Villager has been saved");
            SavedVillagersThisBattle++;
        }
        if (unitInstance.rescuedUnitData[1] != null)
        {
            SavedVillagersThisBattle++;
        }


        // 2. Remove the unit from the battlefield (Disable GameObject / destroy)
        // This triggers cleanup in your CombatManager/Roster
        Destroy(unitInstance.gameObject);

        Debug.Log($"GameStateManager: Unit {unitInstance.unitName} successfully extracted!");

        // 4. Check if the player has any living/active units left on the battlefield
        CheckForCombatEnd();
    }

    private void CheckForCombatEnd(String sceneName = "DecisionPhase")
    {
        // Find all remaining active player units on the field
        UnitInstance[] remainingUnits = FindObjectsByType<UnitInstance>();
        
        bool hasActiveUnits = false;

        foreach (var u in remainingUnits)
        {
            if (u != null && u.Faction == UnitFaction.Player && !u.IsDead && !u.IsExtracted)
            {
                hasActiveUnits = true;
                break;
            }
        }
        

        // If no active units left, end the round/combat phase
        if (!hasActiveUnits)
        {
            Debug.Log("GameStateManager: All player units are dead or extracted. Ending round.");
            SetState(GameState.InCombat);
            SceneManager.LoadScene(sceneName);
            // Trigger your round end logic here (e.g., call CombatManager.Instance.EndRound() or invoke an event)
            if (CombatManager.Instance != null)
            {
                // CombatManager.Instance.TriggerRoundEnd();
            }
        }
    }

    // ============================================================
    // SETTLEMENT / DECISION SCREEN
    // ============================================================
    // Everything below backs the Decision (planning) tab: the player splits
    // FullRoster across four directives (Vanguard / Scavenge / Harvest /
    // Expansion), previews the effect on Food, Materials, Labor and Morale,
    // then commits with ExecuteCycle(). The UI should call AdjustAllocation()
    // and ComputeForecast() on every stepper tap and never do this math itself.

    /// <summary>Total units currently owned (population), independent of housing.</summary>
    public int Population => FullRoster.Count;

    /// <summary>Units not currently assigned to a directive this cycle.</summary>
    public int IdleUnits => Mathf.Max(0, Population - CurrentAllocation.Assigned);

    /// <summary>
    /// Attempts to move one unit into/out of a directive. Fails silently (returns
    /// false) rather than throwing, so the UI can just no-op a stepper tap that
    /// would go negative or exceed the idle pool - mirrors the mock's adjustUnits().
    /// </summary>
    public bool TryAdjustAllocation(Directive directive, int delta)
    {
        if (delta > 0 && IdleUnits < delta) return false;
        int newValue = CurrentAllocation.Get(directive) + delta;
        if (newValue < 0) return false;

        CurrentAllocation.Set(directive, newValue);
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

        int laborUsed = a.ScavengeUnits * b.LaborPerScavengeUnit
                       + a.HarvestUnits * b.LaborPerHarvestUnit
                       + a.ExpansionUnits * b.LaborPerExpansionUnit;

        int foodDelta = Mathf.RoundToInt(
            a.HarvestUnits * b.FoodPerHarvestUnit
            - b.FoodUpkeepBase
            - a.VanguardUnits * b.FoodPerVanguardUnit);

        int materialsDelta = Mathf.RoundToInt(
            a.ScavengeUnits * b.MaterialsPerScavengeUnit
            - a.ExpansionUnits * b.MaterialsPerExpansionUnit);

        int moraleDelta = 0;
        if (a.VanguardUnits >= b.VanguardMoraleThreshold) moraleDelta += b.VanguardMoraleBonus;
        if (a.HarvestUnits >= b.HarvestMoraleThreshold) moraleDelta += b.HarvestMoraleBonus;
        if (a.ExpansionUnits >= b.ExpansionMoraleThreshold) moraleDelta += b.ExpansionMoraleBonus;
        if (foodDelta < 0) moraleDelta -= b.FoodDeficitMoralePenalty;

        // Overcrowding uses *current* population vs *current* housing - expanding
        // this cycle doesn't relieve crowding until the pod actually finishes.
        int overCap = Mathf.Max(0, Population - Settlement.HousingCapacity);
        if (overCap > 0) moraleDelta -= overCap * b.OvercrowdingMoralePenaltyPerUnit;

        return new CycleForecast
        {
            AssignedUnits = a.Assigned,
            IdleUnits = IdleUnits,
            FoodDelta = foodDelta,
            MaterialsDelta = materialsDelta,
            LaborUsed = laborUsed,
            LaborRemaining = Settlement.LaborCapacity - laborUsed,
            MoraleDelta = moraleDelta,
            IsOverLaborBudget = laborUsed > Settlement.LaborCapacity,
            WillOvercrowd = overCap > 0
        };
    }

    /// <summary>
    /// Commits the current allocation: applies the forecasted Food/Materials/
    /// Labor/Morale deltas to Settlement, grows HousingCapacity from any
    /// Expansion assignment, sends the Vanguard block into ActiveTeam, and
    /// hands off to combat. Returns false (and applies nothing) if Labor is
    /// over budget, so the UI should disable the "Execute Cycle" button
    /// whenever forecast.IsOverLaborBudget is true rather than relying on this.
    /// </summary>
    public bool ExecuteCycle(string battleSceneName = "BattlePhase")
    {
        var forecast = ComputeForecast();
        if (forecast.IsOverLaborBudget)
        {
            Debug.LogWarning("ExecuteCycle: Labor over budget, allocation not applied.");
            return false;
        }

        Settlement.Food = Mathf.Max(0, Settlement.Food + forecast.FoodDelta);
        Settlement.Materials = Mathf.Max(0, Settlement.Materials + forecast.MaterialsDelta);
        Settlement.Morale = Mathf.Clamp(Settlement.Morale + forecast.MoraleDelta, 0, 100);
        Settlement.HousingCapacity += CurrentAllocation.ExpansionUnits > 0
            ? Balance.HousingGainPerExpansionUnit
            : 0;

        // Hand the Vanguard block off to combat. Which specific units fill that
        // block is a team-composition decision this method doesn't make - wire
        // your roster-selection UI's picks into SetActiveTeam() before calling
        // ExecuteCycle(), or replace this with your own selection logic.
        if (CurrentAllocation.VanguardUnits > 0 && ActiveTeam.Count > 0)
        {
            LoadCombatMap(battleSceneName);
        }

        CurrentAllocation.Reset(0, 0, 0, 0);
        return true;
    }

    public void ApplyPostBattleResults()

    {

        int delta = EnemiesKilledThisBattle * Balance.EnemyKilledMoraleBonus

                  + SavedVillagersThisBattle * Balance.VillagerSavedMoraleBonus

                  - CasualtiesThisBattle * Balance.UnitLostMoralePenalty;



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



    /// <summary>Call from CombatManager when a player unit dies (not extracted).</summary>
    public void RegisterCasualty(Unit unit)
    {
        if (unit != null) FullRoster.Remove(unit);
        CasualtiesThisBattle++;
    }

    /// <summary>Call from CombatManager when an enemy unit is killed.</summary>
    public void RegisterEnemyKilled()
    {
        EnemiesKilledThisBattle++;
    }
}