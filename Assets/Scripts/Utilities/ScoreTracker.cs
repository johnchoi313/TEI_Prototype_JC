using TMPro;
using UnityEngine;

/// <summary>
/// Singleton score tracker for the maze prototype.
///
/// Counts walls broken, stations collected, and ability swaps, writing each
/// value to its own TMP_Text element every time a score event fires.
/// Also runs a session timer displayed as MM:SS in a separate TMP_Text.
/// All counters are read by Logger for CSV output.
///
/// SCENE SETUP
///   1. Create an empty GameObject (e.g. "ScoreTracker") in the scene.
///   2. Attach this component.
///   3. Assign WallsBrokenText, StationsCollectedText, TimesSwappedText, and
///      TimerText to TMP_Text elements in your Canvas (any can be left null).
///   4. The timer is paused by default. Call StartTimer() to begin (Logger does this
///      automatically on StartLogging). Call ResetScores() to restart both timer and counts.
/// </summary>
public class ScoreTracker : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static ScoreTracker Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Debug UI")]
    [Tooltip("TMP text element that shows the walls-broken count.")]
    [SerializeField] private TMP_Text _wallsBrokenText;

    [Tooltip("TMP text element that shows the stations-collected count.")]
    [SerializeField] private TMP_Text _stationsCollectedText;

    [Tooltip("TMP text element that shows the ability-swap count.")]
    [SerializeField] private TMP_Text _timesSwappedText;

    [Tooltip("TMP text element that shows elapsed time as MM:SS.")]
    [SerializeField] private TMP_Text _timerText;

    // ── State ─────────────────────────────────────────────────────────────────

    public int   WallsBroken       { get; private set; }
    public int   StationsCollected { get; private set; }
    public int   TimesSwapped      { get; private set; }
    public float ElapsedSeconds    { get; private set; }
    public bool  TimerRunning      { get; private set; }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ElapsedSeconds = 0f;
        TimerRunning   = false;
        RefreshUI();
    }

    private void Update()
    {
        if (!TimerRunning) return;
        ElapsedSeconds += Time.deltaTime;
        RefreshTimer();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Resets ElapsedSeconds to zero and starts the timer.</summary>
    public void StartTimer()
    {
        ElapsedSeconds = 0f;
        TimerRunning   = true;
        RefreshTimer();
    }

    /// <summary>Pauses the timer without resetting it.</summary>
    public void StopTimer()
    {
        TimerRunning = false;
    }

    public void AddWallBreak()
    {
        WallsBroken++;
        Debug.Log($"[ScoreTracker] Wall broken — total: {WallsBroken}");
        RefreshUI();
    }

    public void AddStationCollect()
    {
        StationsCollected++;
        Debug.Log($"[ScoreTracker] Station collected — total: {StationsCollected}");
        RefreshUI();
    }

    public void AddSwap()
    {
        TimesSwapped++;
        Debug.Log($"[ScoreTracker] Ability swapped — total: {TimesSwapped}");
        RefreshUI();
    }

    public void ResetScores()
    {
        WallsBroken       = 0;
        StationsCollected = 0;
        TimesSwapped      = 0;
        ElapsedSeconds    = 0f;
        TimerRunning      = false;
        RefreshUI();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (_wallsBrokenText != null)
            _wallsBrokenText.text = $"Walls Broken: {WallsBroken}";

        if (_stationsCollectedText != null)
            _stationsCollectedText.text = $"Stations Collected: {StationsCollected}";

        if (_timesSwappedText != null)
            _timesSwappedText.text = $"Ability Swaps: {TimesSwapped}";

        RefreshTimer();
    }

    private void RefreshTimer()
    {
        if (_timerText == null) return;
        int totalSeconds = Mathf.FloorToInt(ElapsedSeconds);
        int minutes      = totalSeconds / 60;
        int seconds      = totalSeconds % 60;
        _timerText.text  = $"{minutes:00}:{seconds:00}";
    }
}
