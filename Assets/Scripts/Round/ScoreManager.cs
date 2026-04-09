using System;
using UnityEngine;

/// <summary>
/// Tracks P1 and P2 scores for the current round.
///
/// Called by ProblemObject.TryFix() via AddScore(). Resets automatically
/// when the game transitions from a non-Playing state back to Playing
/// (i.e. a new round begins).
///
/// Place on a persistent Manager GameObject in the scene.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    // ── Public state ──────────────────────────────────────────────────────────

    public int P1Score          { get; private set; }
    public int P2Score          { get; private set; }

    /// <summary>Number of problems successfully fixed by each player this round.</summary>
    public int P1ProblemsSolved { get; private set; }
    public int P2ProblemsSolved { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired whenever a score changes. Args = (player, new total score).</summary>
    public static event Action<PlayerIndex, int> OnScoreChanged;

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
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void AddScore(PlayerIndex player, int amount)
    {
        if (player == PlayerIndex.Player1) { P1Score += amount; P1ProblemsSolved++; }
        else                               { P2Score += amount; P2ProblemsSolved++; }

        int newTotal = player == PlayerIndex.Player1 ? P1Score : P2Score;
        OnScoreChanged?.Invoke(player, newTotal);
        Debug.Log($"[ScoreManager] {player} score: {newTotal} ({(player == PlayerIndex.Player1 ? P1ProblemsSolved : P2ProblemsSolved)} problems)");
    }

    public void ResetScores()
    {
        P1Score = 0;
        P2Score = 0;
        P1ProblemsSolved = 0;
        P2ProblemsSolved = 0;
        OnScoreChanged?.Invoke(PlayerIndex.Player1, 0);
        OnScoreChanged?.Invoke(PlayerIndex.Player2, 0);
    }

    // ── State handling ────────────────────────────────────────────────────────

    private void HandleStateChanged(GameState prev, GameState next)
    {
        // Reset scores when a new round starts (PreGame → Playing, or debug skip).
        // Using prev == PreGame ensures we don't reset if a future Paused state is added.
        if (next == GameState.Playing && prev == GameState.PreGame)
            ResetScores();
    }
}
