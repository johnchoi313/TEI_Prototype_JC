using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Singleton that manages the lifecycle of all problem objects in the level.
///
/// On round start, finds every ProblemSpawnPocket in the scene and fills
/// eligible pockets (those with spawnOnStart = true). When a problem despawns
/// (fixed or broken), the pocket enters a cooldown before being refilled.
///
/// A maxActiveProblems cap bounds how many problems exist simultaneously,
/// preventing screen clutter in smaller levels.
///
/// SCENE SETUP
///   1. Place this component on a Manager GameObject.
///   2. Assign _problemPrefab (the Problem prefab with ProblemObject.cs).
///   3. Assign _minimapIconPrefab (a UI Image prefab for the minimap icon).
///   4. Assign _minimapIconLayer (a RectTransform in the minimap canvas hierarchy
///      that serves as the parent for all runtime-spawned problem icons).
/// </summary>
public class ProblemManager : MonoBehaviour
{
    public static ProblemManager Instance { get; private set; }

    [Header("Spawning")]
    [Tooltip("The Problem prefab to instantiate. Must have ProblemObject.cs on the root.")]
    [SerializeField] private GameObject _problemPrefab;

    [Tooltip("Maximum number of problems active at the same time.")]
    [SerializeField] private int _maxActiveProblems = 6;

    [Tooltip("Delay (seconds) between game start and the first wave of problem spawns.")]
    [SerializeField] private float _initialSpawnDelay = 2f;

    [Tooltip("Stagger between each pocket's initial fill (seconds). Prevents all problems appearing at once.")]
    [SerializeField] private float _initialSpawnStagger = 0.5f;

    [Header("Minimap")]
    [Tooltip("UI Image prefab used as a minimap icon for each problem. " +
             "Will be instantiated into _minimapIconLayer at runtime.")]
    [SerializeField] private Image _minimapIconPrefab;

    [Tooltip("RectTransform in the minimap canvas that parents all problem icons. " +
             "Create an empty child called ProblemIconLayer inside the minimap's _mapContainer.")]
    [SerializeField] private RectTransform _minimapIconLayer;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private List<ProblemSpawnPocket> _pockets = new List<ProblemSpawnPocket>();
    private List<ProblemObject>      _active  = new List<ProblemObject>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Collect all pockets from the scene — no manual wiring needed.
        _pockets.AddRange(FindObjectsByType<ProblemSpawnPocket>(FindObjectsSortMode.None));
        Debug.Log($"[ProblemManager] Found {_pockets.Count} spawn pockets.");

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;

        // If the game is already Playing (GameManager boots directly to Playing),
        // kick off the initial spawn immediately.
        if (GameManager.Instance != null && GameManager.Instance.State == GameState.Playing)
            StartCoroutine(InitialSpawnRoutine());
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
        // Only start spawning on a fresh round entry (PreGame → Playing or debug skip).
        // Prevents re-running if Playing is entered from a state that isn't a new round.
        if (next == GameState.Playing && prev != GameState.Playing)
            StartCoroutine(InitialSpawnRoutine());

        if (next == GameState.LevelComplete)
            StopAllCoroutines(); // no more respawns once the round ends
    }

    // ── Spawn logic ───────────────────────────────────────────────────────────

    private IEnumerator InitialSpawnRoutine()
    {
        yield return new WaitForSeconds(_initialSpawnDelay);

        foreach (ProblemSpawnPocket pocket in _pockets)
        {
            if (!pocket.spawnOnStart) continue;
            if (_active.Count >= _maxActiveProblems) break;
            SpawnInPocket(pocket);
            yield return new WaitForSeconds(_initialSpawnStagger);
        }
    }

    private void SpawnInPocket(ProblemSpawnPocket pocket)
    {
        if (pocket.IsOccupied) return;
        if (_problemPrefab == null)
        {
            Debug.LogError("[ProblemManager] _problemPrefab not assigned.", this);
            return;
        }

        ProblemDefinition def = pocket.PickDefinition();
        if (def == null) return;

        GameObject go = Instantiate(_problemPrefab, pocket.transform.position, Quaternion.identity);
        ProblemObject problem = go.GetComponent<ProblemObject>();
        if (problem == null)
        {
            Debug.LogError("[ProblemManager] Prefab is missing ProblemObject component.", go);
            Destroy(go);
            return;
        }

        problem.SetSpawnPocket(pocket);
        pocket.Occupy(problem);
        _active.Add(problem);

        // Spawn and attach the minimap icon.
        if (_minimapIconPrefab != null && _minimapIconLayer != null)
        {
            Image icon = Instantiate(_minimapIconPrefab, _minimapIconLayer);
            MinimapProblemTracker tracker = problem.GetComponent<MinimapProblemTracker>();
            if (tracker != null)
                tracker.Initialize(icon);
        }
    }

    // ── Called by ProblemObject ───────────────────────────────────────────────

    /// <summary>
    /// Called by ProblemObject just before it destroys itself.
    /// Releases the pocket and schedules a respawn.
    /// </summary>
    public void OnProblemDespawned(ProblemObject problem, ProblemSpawnPocket pocket)
    {
        _active.Remove(problem);

        if (pocket == null) return;

        pocket.Release();

        float delay = problem.Definition != null ? problem.Definition.respawnDelay : 30f;

        if (GameManager.Instance != null &&
            GameManager.Instance.State == GameState.Playing)
        {
            StartCoroutine(RespawnAfterDelay(pocket, delay));
        }
    }

    private IEnumerator RespawnAfterDelay(ProblemSpawnPocket pocket, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) yield break;
        if (_active.Count >= _maxActiveProblems) yield break;

        SpawnInPocket(pocket);
    }
}
