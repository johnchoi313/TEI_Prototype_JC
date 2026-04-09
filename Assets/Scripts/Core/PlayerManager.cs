using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns and owns the two fish character instances.
///
/// PREFAB MODE (target state)
///   Assign _p1FishPrefab and _p2FishPrefab in the Inspector. Each prefab is a
///   self-contained fish with FOVHighlightable.respondTo baked in (Player1Only /
///   Player2Only). Call OnLevelLoaded() (from LevelManager after scene load) to
///   destroy old instances and spawn new ones at LevelController's spawn points.
///   After spawning, fires OnPlayersSpawned so systems with fish references
///   (e.g. PowerUpManager) can re-cache them.
///
/// PROTOTYPE FALLBACK (current state)
///   Assign the scene-placed fish GameObjects to _p1SceneCharacter / _p2SceneCharacter.
///   GetPlayerInstance() returns runtime instances first, then falls back to these.
///   Remove the scene references once prefab spawning is fully wired.
///
/// FOV BINDING
///   Each fish prefab must have FOVHighlightable.respondTo set to Player1Only or
///   Player2Only. This is the ONLY wiring needed — FOVWorldCollider handles the rest.
///   No FOVController.Initialize() call is required for FOVHighlightable-based fish.
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    [Header("Fish Prefabs (Spawning Mode)")]
    [Tooltip("Fish prefab for Player 1. Must have FOVHighlightable.respondTo = Player1Only.")]
    [SerializeField] private GameObject _p1FishPrefab;

    [Tooltip("Fish prefab for Player 2. Must have FOVHighlightable.respondTo = Player2Only.")]
    [SerializeField] private GameObject _p2FishPrefab;

    [Header("Prototype Scene Override")]
    [Tooltip("Scene-placed P1 fish. Used as fallback when prefab spawning hasn't run. " +
             "Clear once prefab mode is active.")]
    [SerializeField] private GameObject _p1SceneCharacter;

    [Tooltip("Scene-placed P2 fish. Used as fallback when prefab spawning hasn't run.")]
    [SerializeField] private GameObject _p2SceneCharacter;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired after both players are spawned (or after scene refs are used as fallback).
    /// Subscribe to re-cache fish references in systems like PowerUpManager.
    /// Args: (p1GameObject, p2GameObject)
    /// </summary>
    public static event Action<GameObject, GameObject> OnPlayersSpawned;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private readonly GameObject[] _playerInstances = new GameObject[2];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the fish character for the given player.
    /// Prefers the runtime-spawned instance; falls back to the scene-placed reference.
    /// </summary>
    public GameObject GetPlayerInstance(PlayerIndex index)
    {
        GameObject spawned = _playerInstances[(int)index];
        if (spawned != null) return spawned;
        return index == PlayerIndex.Player1 ? _p1SceneCharacter : _p2SceneCharacter;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        bool hasPrefabs = _p1FishPrefab != null || _p2FishPrefab != null;
        Debug.Log("[Player Manager] has prefabs: " + hasPrefabs.ToString());
        if (!hasPrefabs)
        {
            // Pure prototype mode — scene-placed fish, fire immediately.
            if (_p1SceneCharacter != null || _p2SceneCharacter != null)
                OnPlayersSpawned?.Invoke(_p1SceneCharacter, _p2SceneCharacter);
        }
        else if (LevelManager.Instance == null || !LevelManager.Instance.HasLevelSequence)
        {
            // Prefab mode, single-scene: LevelManager won't load a scene so
            // LoadLevelRoutine never fires. Spawn now against whatever LevelController
            // is already in the active scene.
            OnLevelLoaded();
        }
        // else: multi-scene mode — LevelManager.LoadLevelRoutine() calls OnLevelLoaded()
        // after the level scene finishes loading. Don't spawn here.
    }

    // ── Called by LevelManager after scene load ───────────────────────────────

    /// <summary>
    /// Destroys previous spawned instances and spawns fresh fish at the spawn
    /// points defined by the LevelController in the newly loaded scene.
    /// No-ops gracefully if prefabs aren't assigned (prototype mode).
    /// </summary>
    public void OnLevelLoaded()
    {
        if (_p1FishPrefab == null || _p2FishPrefab == null)
        {
            // Prototype mode — use scene-placed fish.
            // After a LoadSceneMode.Single reload, Inspector scene refs become null because
            // the old objects were destroyed. Re-find them by FOVOwner assignment.
            if (_p1SceneCharacter == null || _p2SceneCharacter == null)
                RefindSceneFish();

            Debug.Log("[PlayerManager] Prototype mode — using scene references.");
            OnPlayersSpawned?.Invoke(_p1SceneCharacter, _p2SceneCharacter);
            return;
        }

        DestroyExistingPlayers();

        SpawnPlayer(PlayerIndex.Player1, FindSpawnPosition(PlayerIndex.Player1));
        SpawnPlayer(PlayerIndex.Player2, FindSpawnPosition(PlayerIndex.Player2));

        // Notify LevelController if one exists (multi-scene mode).
        LevelController level = FindLevelController();
        if (level != null) level.PlayersReady = true;

        Debug.Log("[PlayerManager] Both players spawned.");

        OnPlayersSpawned?.Invoke(
            _playerInstances[(int)PlayerIndex.Player1],
            _playerInstances[(int)PlayerIndex.Player2]);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void SpawnPlayer(PlayerIndex index, Vector3 position)
    {
        
        GameObject prefab = index == PlayerIndex.Player1 ? _p1FishPrefab : _p2FishPrefab;
        if (prefab == null)
        {
            Debug.LogError($"[PlayerManager] No prefab assigned for {index}.");
            return;
        }

        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        instance.name = $"Fish_{index}";
        Debug.Log("[PlayerManager] Spawn called for " + instance.name);
        _playerInstances[(int)index] = instance;
    }

    private void DestroyExistingPlayers()
    {
        for (int i = 0; i < _playerInstances.Length; i++)
        {
            if (_playerInstances[i] != null)
            {
                Destroy(_playerInstances[i]);
                _playerInstances[i] = null;
            }
        }
    }

    /// <summary>
    /// After a full scene reload (LoadSceneMode.Single), Inspector scene refs become null.
    /// Scan for FOVHighlightable components to re-find the two fish by their FOVOwner setting.
    /// </summary>
    private void RefindSceneFish()
    {
        var highlightables = FindObjectsByType<FOVHighlightable>(FindObjectsSortMode.None);
        foreach (var h in highlightables)
        {
            if (h.RespondTo == FOVHighlightable.FOVOwner.Player1Only && _p1SceneCharacter == null)
                _p1SceneCharacter = h.gameObject;
            else if (h.RespondTo == FOVHighlightable.FOVOwner.Player2Only && _p2SceneCharacter == null)
                _p2SceneCharacter = h.gameObject;
        }

        if (_p1SceneCharacter == null) Debug.LogWarning("[PlayerManager] Could not find P1 fish (FOVOwner.Player1Only). Is it in the scene?");
        if (_p2SceneCharacter == null) Debug.LogWarning("[PlayerManager] Could not find P2 fish (FOVOwner.Player2Only). Is it in the scene?");
    }

    /// <summary>
    /// Resolves the world-space spawn position for a player.
    ///
    /// Resolution order:
    ///   1. SpawnPointMarker in the active scene (drop an Empty with this component — no wiring needed).
    ///   2. LevelController.GetSpawnPoint() — multi-scene mode with a LevelConfig SO.
    ///   3. Vector3.zero fallback with a warning.
    /// </summary>
    private static Vector3 FindSpawnPosition(PlayerIndex index)
    {
        var markers = FindObjectsByType<SpawnPointMarker>(FindObjectsSortMode.None);
        foreach (var m in markers)
            if (m.player == index) return m.transform.position;

        LevelController level = FindLevelController();
        if (level != null) return level.GetSpawnPoint(index);

        Debug.LogWarning($"[PlayerManager] No SpawnPointMarker or LevelController found for {index}. Spawning at origin.");
        return Vector3.zero;
    }

    private static LevelController FindLevelController()
    {
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            LevelController lc = root.GetComponentInChildren<LevelController>();
            if (lc != null) return lc;
        }
        return null;
    }
}
