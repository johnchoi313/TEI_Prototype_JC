using System;
using UnityEngine;

/// <summary>
/// Game states in order of the researcher-facing flow.
/// Tutorial will be inserted between MainMenu and Rules in a future update.
/// </summary>
public enum GameState
{
    Boot,          // Initializing — transitions to MainMenu immediately on Start
    MainMenu,      // Home screen; researcher presses Start (keyboard/mouse)
    Rules,         // Rules overlay shown over the game world before each round
    PreGame,       // 3-2-1-GO countdown; FOV tracks hands but fish do NOT move
    Playing,       // Active gameplay; fish move, timer runs, problems spawn
    LevelComplete, // Results screen; fish frozen; researcher advances or session ends
}

/// <summary>
/// Singleton game state machine. Lives in the persistent base scene (GameManagerSetup).
///
/// All systems react to OnStateChanged rather than polling State directly.
/// Transition methods encode the valid flow — call them from UI buttons or other managers.
///
/// DEBUG: Set _debugStartState in the Inspector to skip directly to any state at Play time.
/// </summary>
[DefaultExecutionOrder(-100)] // Awake() runs before all default-order scripts
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public GameState State { get; private set; } = GameState.Boot;

    /// <summary>Fired on every state transition. Args: (previousState, nextState).</summary>
    public event Action<GameState, GameState> OnStateChanged;

    [Header("Debug")]
    [Tooltip("Override the boot state during development. " +
             "Boot = normal flow (starts at MainMenu). " +
             "Set to Playing to skip straight to active gameplay.")]
    [SerializeField] private GameState _debugStartState = GameState.MainMenu;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        GameState startState = (_debugStartState == GameState.Boot)
            ? GameState.MainMenu
            : _debugStartState;

        SetState(startState);
    }

    // ── Public transition API ─────────────────────────────────────────────────

    /// <summary>MainMenu → Rules. Called when researcher clicks Start on the home screen.</summary>
    public void GoToRules() => SetState(GameState.Rules);

    /// <summary>Rules → PreGame. Called when researcher clicks "Start Game" on the rules screen.</summary>
    public void GoToPreGame() => SetState(GameState.PreGame);

    /// <summary>
    /// PreGame → Playing. Called automatically by RoundManager when the 3-2-1 countdown ends.
    /// Do NOT wire this to a UI button — the countdown drives this transition.
    /// </summary>
    public void StartPlaying() => SetState(GameState.Playing);

    /// <summary>Playing → LevelComplete. Called by RoundManager when the round timer expires.</summary>
    public void TriggerLevelComplete() => SetState(GameState.LevelComplete);

    /// <summary>
    /// LevelComplete → (load next level scene) → Rules.
    /// Called when researcher clicks "Go to Next Level". Delegates scene loading to LevelManager,
    /// which calls GoToRules() once the new scene is ready.
    /// </summary>
    public void LoadNextLevel()
    {
        if (LevelManager.Instance == null)
        {
            Debug.LogError("[GameManager] LoadNextLevel called but LevelManager is null.");
            return;
        }
        LevelManager.Instance.AdvanceLevel();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void SetState(GameState next)
    {
        if (State == next) return;
        GameState prev = State;
        State = next;
        Debug.Log($"[GameManager] {prev} → {next}");
        OnStateChanged?.Invoke(prev, next);
    }
}
