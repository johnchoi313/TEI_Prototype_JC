using TMPro;
using UnityEngine;

/// <summary>
/// Drives the round HUD: countdown timer and per-player scores.
///
/// Subscribes to RoundManager.OnTimeChanged and ScoreManager.OnScoreChanged.
/// All display updates are event-driven — no polling.
///
/// SCENE SETUP
///   Add this component to a HUD GameObject in the scene.
///   Wire the three TMP_Text references in the Inspector:
///     _timerText    — center-top: displays MM:SS countdown.
///     _p1ScoreText  — top-left:  Player 1 score.
///     _p2ScoreText  — top-right: Player 2 score.
/// </summary>
public class RoundHUDController : MonoBehaviour
{
    [Header("Text References")]
    [SerializeField] private TMP_Text _timerText;
    [SerializeField] private TMP_Text _p1ScoreText;
    [SerializeField] private TMP_Text _p2ScoreText;

    [Header("Labels (optional)")]
    [SerializeField] private string _p1Label = "P1: ";
    [SerializeField] private string _p2Label = "P2: ";

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        RoundManager.OnTimeChanged  += UpdateTimer;
        ScoreManager.OnScoreChanged += UpdateScore;
    }

    private void OnDisable()
    {
        RoundManager.OnTimeChanged  -= UpdateTimer;
        ScoreManager.OnScoreChanged -= UpdateScore;
    }

    private void Start()
    {
        // Initialize display with current values (handles scene-reload edge cases).
        if (RoundManager.Instance != null)
            UpdateTimer(RoundManager.Instance.TimeRemaining);

        if (ScoreManager.Instance != null)
        {
            UpdateScore(PlayerIndex.Player1, ScoreManager.Instance.P1Score);
            UpdateScore(PlayerIndex.Player2, ScoreManager.Instance.P2Score);
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void UpdateTimer(float secondsRemaining)
    {
        if (_timerText == null) return;

        int minutes = Mathf.FloorToInt(secondsRemaining / 60f);
        int seconds = Mathf.FloorToInt(secondsRemaining % 60f);
        _timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void UpdateScore(PlayerIndex player, int score)
    {
        if (player == PlayerIndex.Player1 && _p1ScoreText != null)
            _p1ScoreText.text = $"{_p1Label}{score}";
        else if (player == PlayerIndex.Player2 && _p2ScoreText != null)
            _p2ScoreText.text = $"{_p2Label}{score}";
    }
}
