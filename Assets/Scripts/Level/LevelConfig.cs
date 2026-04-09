using UnityEngine;

/// <summary>
/// Data asset for a single level. Create one per level via the Assets menu.
/// Consumed by LevelController at runtime to set up spawn points, puzzle configs, etc.
///
/// Design intent: all level data lives here — no magic numbers in scene objects.
/// Adding a new level means creating a new LevelConfig and a scene that uses it.
/// </summary>
[CreateAssetMenu(fileName = "LevelConfig_New", menuName = "TEI/Level Config")]
public class LevelConfig : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Human-readable display name for this level.")]
    public string levelName = "Unnamed Level";

    [Header("Player Spawns")]
    [Tooltip("World-space spawn positions. Index 0 = Player1, Index 1 = Player2.")]
    public Vector3[] spawnPoints = new Vector3[2];

    [Header("Progression")]
    [Tooltip("Scene name to load after this level completes. Leave empty for credits/end.")]
    public string nextLevelScene;

    [Header("Puzzle Config")]
    [Tooltip("Extensible list of puzzle definitions for this level.")]
    public PuzzleConfig[] puzzles;
}

/// <summary>
/// Base class for puzzle configuration. Subclass to add puzzle-specific data.
/// Example: CohabitZonePuzzleConfig, SwitchPuzzleConfig, etc.
/// </summary>
[System.Serializable]
public class PuzzleConfig
{
    [Tooltip("Unique ID used to match this config to a PuzzleController in the scene.")]
    public string puzzleId;
}
