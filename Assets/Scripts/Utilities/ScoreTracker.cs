using TMPro;
using UnityEngine;

/// <summary>
/// Singleton score tracker for the maze prototype.
///
/// Counts walls broken, stations collected, and ability swaps, writing each
/// value to its own TMP_Text element every time a score event fires.
/// All three counters are also read by Logger for CSV output.
///
/// SCENE SETUP
///   1. Create an empty GameObject (e.g. "ScoreTracker") in the scene.
///   2. Attach this component.
///   3. Assign WallsBrokenText, StationsCollectedText, and TimesSwappedText to
///      separate TMP_Text elements in your Canvas (any field can be left null).
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

    // ── State ─────────────────────────────────────────────────────────────────

    public int WallsBroken       { get; private set; }
    public int StationsCollected { get; private set; }
    public int TimesSwapped      { get; private set; }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        RefreshUI();
    }

    // ── Public API ────────────────────────────────────────────────────────────

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
    }
}
