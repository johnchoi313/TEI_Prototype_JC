using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Owns two timed sequences that drive the game forward:
///
///   PreGame countdown (3-2-1-GO)
///     Triggered by GameState.PreGame.
///     Fires OnPreGameTick each second with whole seconds remaining.
///     On completion, calls GameManager.Instance.StartPlaying().
///
///   Round timer (active gameplay countdown)
///     Triggered by GameState.Playing.
///     Fires OnTimeChanged each second with seconds remaining.
///     On expiry, calls GameManager.Instance.TriggerLevelComplete().
///
/// Place on a persistent Manager GameObject in GameManagerSetup.
/// </summary>
public class RoundManager : MonoBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("PreGame Countdown")]
    [Tooltip("Duration of the 3-2-1-GO countdown before the round starts.")]
    [SerializeField] private float _preGameDuration = 3f;

    [Header("Round Timer")]
    [Tooltip("Total round duration in seconds. " +
             "Set to 10 for quick debug sessions; 300 for production (5 minutes).")]
    [SerializeField] public float roundDuration = 300f;

    // ── Public state ──────────────────────────────────────────────────────────

    public float TimeRemaining { get; private set; }
    public bool  RoundActive   { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires each second during the PreGame countdown.
    /// Arg = whole seconds remaining (3, 2, 1, then 0 = "GO").
    /// </summary>
    public static event Action<int> OnPreGameTick;

    /// <summary>Fires each second during the active round. Arg = seconds remaining.</summary>
    public static event Action<float> OnTimeChanged;

    /// <summary>Fires once when the round timer reaches zero.</summary>
    public static event Action OnRoundExpired;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;

        // Handle debug start state: if GameManager already skipped to a mid-flow state.
        if (GameManager.Instance != null)
            HandleStateChanged(GameState.Boot, GameManager.Instance.State);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    // ── State handling ────────────────────────────────────────────────────────

    private void HandleStateChanged(GameState prev, GameState next)
    {
        switch (next)
        {
            case GameState.PreGame:
                StopAllCoroutines();
                RoundActive = false;
                StartCoroutine(PreGameRoutine());
                break;

            case GameState.Playing:
                // Triggered either by PreGame completing (normal path) or debug skip.
                if (!RoundActive)
                {
                    StopAllCoroutines();
                    StartRound();
                }
                break;

            case GameState.LevelComplete:
            case GameState.MainMenu:
            case GameState.Rules:
                StopAllCoroutines();
                RoundActive = false;
                break;
        }
    }

    // ── PreGame countdown ─────────────────────────────────────────────────────

    private IEnumerator PreGameRoutine()
    {
        float remaining = _preGameDuration;
        Debug.Log($"[RoundManager] PreGame countdown started: {_preGameDuration}s");

        while (remaining > 0f)
        {
            OnPreGameTick?.Invoke(Mathf.CeilToInt(remaining));
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        // Emit 0 = "GO"
        OnPreGameTick?.Invoke(0);
        yield return new WaitForSeconds(0.5f); // brief hold so UI can show "GO"

        Debug.Log("[RoundManager] PreGame complete — transitioning to Playing.");
        GameManager.Instance?.StartPlaying();
    }

    // ── Round timer ───────────────────────────────────────────────────────────

    private void StartRound()
    {
        TimeRemaining = roundDuration;
        RoundActive   = true;
        StartCoroutine(RoundTimerRoutine());
        Debug.Log($"[RoundManager] Round started — {roundDuration}s");
    }

    private IEnumerator RoundTimerRoutine()
    {
        while (TimeRemaining > 0f)
        {
            yield return new WaitForSeconds(1f);

            // Pause support: stall the coroutine if RoundActive is cleared externally.
            while (!RoundActive)
                yield return null;

            TimeRemaining = Mathf.Max(0f, TimeRemaining - 1f);
            OnTimeChanged?.Invoke(TimeRemaining);
        }

        if (RoundActive)
        {
            RoundActive = false;
            Debug.Log("[RoundManager] Round expired.");
            OnRoundExpired?.Invoke();
            GameManager.Instance?.TriggerLevelComplete();
        }
    }
}
