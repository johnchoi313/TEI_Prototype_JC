using TMPro;
using UnityEngine;

/// <summary>
/// Singleton score tracker for the maze prototype.
///
/// Counts walls broken and stations collected, writing each value to its own
/// TMP_Text element every time a score event fires.
///
/// SCENE SETUP
///   1. Create an empty GameObject (e.g. "ScoreTracker") in the scene.
///   2. Attach this component.
///   3. Assign WallsBrokenText and StationsCollectedText to separate TMP_Text
///      elements in your Canvas. Either field can be left null to skip that display.
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

    // ── State ─────────────────────────────────────────────────────────────────

    public int WallsBroken      { get; private set; }
    public int StationsCollected { get; private set; }

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

    public void ResetScores()
    {
        WallsBroken       = 0;
        StationsCollected = 0;
        RefreshUI();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (_wallsBrokenText != null)
            _wallsBrokenText.text = $"Walls Broken: {WallsBroken}";

        if (_stationsCollectedText != null)
            _stationsCollectedText.text = $"Stations Collected: {StationsCollected}";
    }
}
