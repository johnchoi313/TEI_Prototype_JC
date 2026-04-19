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
///   2. Assign _normalProblemPrefabs (one or more prefabs with ProblemObject.cs, isDud = false).
///   3. Assign _dudProblemPrefabs    (one or more prefabs with ProblemObject.cs, isDud = true).
///   4. Tune _realProblemRatio (0–1 slider): fraction of spawns that produce a real problem.
///   5. Assign _minimapIconPrefab (a UI Image prefab for the minimap icon).
///   6. Assign _minimapIconLayer (a RectTransform in the minimap canvas hierarchy
///      that serves as the parent for all runtime-spawned problem icons).
/// </summary>
public class ProblemManager : MonoBehaviour
{
    public static ProblemManager Instance { get; private set; }

    [Header("Spawning — Prefabs")]
    [Tooltip("Normal (real) problem prefabs. One is picked at random each spawn.")]
    [SerializeField] private GameObject[] _normalProblemPrefabs;

    [Tooltip("Dud problem prefabs. One is picked at random each spawn.")]
    [SerializeField] private GameObject[] _dudProblemPrefabs;

    [Header("Spawning — Ratio")]
    [Tooltip("Fraction of spawns that produce a real problem. " +
             "1 = all real, 0 = all duds, 0.75 = 3-in-4 chance of a real problem.")]
    [Range(0f, 1f)]
    [SerializeField] private float _realProblemRatio = 0.75f;

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

        GameObject prefab = PickPrefab();
        if (prefab == null) return;

        GameObject go = Instantiate(prefab, pocket.transform.position, Quaternion.identity);
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

    /// <summary>
    /// Picks a prefab to spawn based on _realProblemRatio.
    /// Rolls a random value: below the ratio → normal prefab, at or above → dud prefab.
    /// Falls back to whichever array is populated if the other is empty.
    /// </summary>
    private GameObject PickPrefab()
    {
        bool hasNormal = _normalProblemPrefabs != null && _normalProblemPrefabs.Length > 0;
        bool hasDud    = _dudProblemPrefabs    != null && _dudProblemPrefabs.Length    > 0;

        if (!hasNormal && !hasDud)
        {
            Debug.LogError("[ProblemManager] No problem prefabs assigned — assign at least one normal or dud prefab.", this);
            return null;
        }

        // Decide type, falling back gracefully if one array is empty.
        bool spawnReal = Random.value < _realProblemRatio;
        if (spawnReal && !hasNormal) spawnReal = false;
        if (!spawnReal && !hasDud)  spawnReal = true;

        GameObject[] pool = spawnReal ? _normalProblemPrefabs : _dudProblemPrefabs;
        return pool[Random.Range(0, pool.Length)];
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
