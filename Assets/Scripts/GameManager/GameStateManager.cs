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

    // --- SINGLETON SETUP ---
    public static GameStateManager Instance { get; private set; }

    public int testing =1;

    [Header("Available Unit Templates")]
    public List<UnitData> AvailablePlayerArchetypes; // Drag 'Knight' and 'Warrior' assets here

    // Maximum number of units the player can bring into a single battle.
    public const int MaxTeamSize = 5;

    [Header("Persistent Data")]
    public PlayerResources Resources = new PlayerResources();

    // Roster of all owned units (the pool shown on the team-selection screen).
    public List<Unit> FullRoster = new List<Unit>();

    // Units the player has picked to bring into the current battle (max MaxTeamSize).
    public List<Unit> ActiveTeam = new List<Unit>();

    // Units added to FullRoster that haven't been shown to the player yet via the
    // "new unit" reveal animation on the team-selection screen. The UI drains this
    // queue and calls ClearPendingReveal() once it has shown them all.
    public List<Unit> PendingReveal = new List<Unit>();

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
            PlanningPhaseController planningController =
                GameObject.FindAnyObjectByType<PlanningPhaseController>();
            
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
}