using UnityEngine;

/// <summary>
/// Lives in each level scene. Reads the LevelConfig and exposes spawn points
/// to the PlayerManager. Also signals GameManager when the level is complete.
///
/// Acts as the entry point for any level-specific logic (cinematics, intro text, etc.).
/// </summary>
public class LevelController : MonoBehaviour
{
    [SerializeField] private LevelConfig config;

    /// <summary>Set by PlayerManager after it has spawned both characters.</summary>
    public bool PlayersReady { get; set; }

    public LevelConfig Config => config;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= OnGameStateChanged;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the world-space spawn position for a given player.
    ///
    /// Resolution order:
    ///   1. SpawnPointMarker in the active scene (preferred — visual, per-scene, no wiring).
    ///   2. LevelConfig.spawnPoints[] fallback (legacy / data-driven override).
    ///   3. Vector3.zero if neither is available.
    /// </summary>
    public Vector3 GetSpawnPoint(PlayerIndex player)
    {
        // Prefer scene-placed SpawnPointMarker — drop two Empties with this component
        // in the level scene, set their player field, and no other wiring is needed.
        var markers = FindObjectsByType<SpawnPointMarker>(FindObjectsSortMode.None);
        foreach (var m in markers)
        {
            if (m.player == player)
                return m.transform.position;
        }

        // Fallback: raw Vector3 values from the LevelConfig SO.
        int idx = (int)player;
        if (config != null && config.spawnPoints != null && idx < config.spawnPoints.Length)
        {
            Debug.LogWarning($"[LevelController] No SpawnPointMarker found for {player} — using LevelConfig fallback.");
            return config.spawnPoints[idx];
        }

        Debug.LogWarning($"[LevelController] No spawn point found for {player}. Using world origin.");
        return Vector3.zero;
    }

    /// <summary>Call this from puzzle logic when the win condition is satisfied.</summary>
    public void NotifyLevelComplete()
    {
        GameManager.Instance?.TriggerLevelComplete();
    }

    // ── State Reactions ───────────────────────────────────────────────────────

    private void OnGameStateChanged(GameState prev, GameState next)
    {
        if (next == GameState.LevelComplete)
            OnLevelComplete();
    }

    private void OnLevelComplete()
    {
        // Level progression is now owned by LevelManager._levelSequence.
        // The researcher clicks "Next Level" on LevelCompleteController →
        // GameManager.LoadNextLevel() → LevelManager.AdvanceLevel().
        // Nothing to do here; this hook is reserved for level-specific
        // effects (e.g. cinematics) triggered at the moment of completion.
    }
}
