using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameStateManager : MonoBehaviour
{

    // --- SINGLETON SETUP ---
    public static GameStateManager Instance { get; private set; }

    public int testing =1;

    [Header("Available Unit Templates")]
    public List<UnitData> AvailablePlayerArchetypes; // Drag 'Knight' and 'Warrior' assets here

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
    /// <summary>
    /// Checks if player has units; if not, assigns random starting units.
    /// </summary>
    public void EnsurePlayerHasTeam(int startingCount = 2)
    {
        if (ActiveTeam.Count > 0) return;
        if (AvailablePlayerArchetypes == null || AvailablePlayerArchetypes.Count == 0)
        {
            Debug.LogError("No UnitData archetypes assigned in GameStateManager!");
            return;
        }

        for (int i = 0; i < startingCount; i++)
        {
            // Pick randomly between Knight and Warrior
            int randomIndex = UnityEngine.Random.Range(0, AvailablePlayerArchetypes.Count);
            ActiveTeam.Add(AvailablePlayerArchetypes[randomIndex]);
        }
    }

    // --- PERSISTENT DATA MODEL ---
    [System.Serializable]
    public class PlayerProgression
    {
        public int Gold = 100;
        public int CurrentStageIndex = 1;
        // Add unlocked craftables, metadata, progression flags, etc.
    }

    [Header("Persistent Data")]
    public PlayerProgression Progression = new PlayerProgression();
    
    // Roster of all owned units
    public List<UnitData> FullRoster = new List<UnitData>();
    
    // Units active in the current battle team (e.g., maximum 4 slots)
    public List<UnitData> ActiveTeam = new List<UnitData>();

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
    public void LoadCombatMap(string sceneName = "CombatMap")
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
            Progression.Gold += goldEarned;
            Progression.CurrentStageIndex++;
        }

        SetState(GameState.TeamManagement);
        SceneManager.LoadScene(sceneName);
    }

    private void SetState(GameState newState)
    {
        CurrentState = newState;
        OnGameStateChanged?.Invoke(newState);
    }
}