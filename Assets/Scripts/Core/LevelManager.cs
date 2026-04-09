using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the ordered sequence of level scenes.
///
/// The base scene (GameManagerSetup) is never unloaded — it contains all persistent
/// managers, cameras, and UI. Level scenes are loaded additively on top of it.
///
/// SETUP
///   1. Assign scene names (as they appear in Build Settings) to _levelSequence[].
///   2. The first scene in the list loads automatically at boot as the world background.
///   3. Call AdvanceLevel() (via GameManager.LoadNextLevel()) to progress to the next scene.
///
/// PROTOTYPE MODE
///   If _levelSequence is empty, no scene auto-loads. The scene placed directly in
///   GameManagerSetup acts as the level. This preserves the existing prototype workflow.
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Level Sequence")]
    [Tooltip("Ordered list of level scene names exactly as they appear in Build Settings. " +
             "The first scene auto-loads at boot. Leave empty for prototype (single-scene) mode.")]
    [SerializeField] private string[] _levelSequence = new string[0];

    [Tooltip("When enabled, reaching the end of the sequence (or having no sequence) reloads " +
             "the current level instead of showing Session Complete. Useful for research sessions " +
             "where you want a clean reset between runs.")]
    [SerializeField] private bool _loopSameLevel = false;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private int    _currentLevelIndex = -1;
    private string _currentLevelScene;

    /// <summary>
    /// True when this is the final level and there is no loop. Controls whether
    /// LevelCompleteController shows "Next Level" or "Session Complete".
    /// </summary>
    public bool IsLastLevel => !_loopSameLevel &&
                               (_levelSequence.Length == 0 ||
                                _currentLevelIndex >= _levelSequence.Length - 1);

    /// <summary>True when a level sequence is configured (multi-scene mode).</summary>
    public bool HasLevelSequence => _levelSequence.Length > 0;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (_levelSequence.Length > 0)
        {
            _currentLevelIndex = 0;
            StartCoroutine(LoadLevelRoutine(_levelSequence[0], notifyGameManager: false));
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the next level in the sequence, unloading the current one.
    /// After loading completes, transitions GameManager to Rules so the researcher
    /// can brief players before the next round.
    ///
    /// Called by GameManager.LoadNextLevel().
    /// </summary>
    public void AdvanceLevel()
    {
        if (_levelSequence.Length == 0)
        {
            // Prototype / single-scene mode.
            if (_loopSameLevel)
                ReloadCurrentLevel();   // clean reload — resets all scene objects
            else
                GameManager.Instance?.GoToRules();  // same scene, no reload
            return;
        }

        if (IsLastLevel)
        {
            if (_loopSameLevel)
            {
                // Reload the current (last) level for another run.
                _currentLevelIndex = 0;
                StartCoroutine(LoadLevelRoutine(_levelSequence[0], notifyGameManager: true));
            }
            else
            {
                Debug.Log("[LevelManager] Session complete — no more levels.");
            }
            return;
        }

        _currentLevelIndex++;
        StartCoroutine(LoadLevelRoutine(_levelSequence[_currentLevelIndex], notifyGameManager: true));
    }

    /// <summary>Reloads the current level (resets all scene objects) then goes to Rules.</summary>
    public void ReloadCurrentLevel()
    {
        if (string.IsNullOrEmpty(_currentLevelScene))
        {
            // Prototype mode: everything lives in one scene. Use LoadSceneMode.Single so
            // all scene objects are fully destroyed and recreated from scratch.
            // DontDestroyOnLoad managers (GameManager, LevelManager, PlayerManager) survive.
            string scene = SceneManager.GetActiveScene().name;
            StartCoroutine(PrototypeFullReloadRoutine(scene));
        }
        else
        {
            // Multi-scene mode: unload the additive level scene and reload it.
            StartCoroutine(LoadLevelRoutine(_currentLevelScene, notifyGameManager: true));
        }
    }

    /// <summary>
    /// Full single-scene reload for prototype mode. Uses LoadSceneMode.Single so every
    /// scene object is cleanly destroyed and recreated. After load completes, waits one
    /// frame for all Awake/Start calls to finish, then tells PlayerManager to re-find
    /// fish and GameManager to go to Rules.
    /// </summary>
    private IEnumerator PrototypeFullReloadRoutine(string sceneName)
    {
        // Set state to Rules BEFORE reloading. GameManager is DontDestroyOnLoad so it
        // survives the reload with State = Rules. The new UIManager.Start() reads this
        // state directly and shows the Rules screen — no event timing dependency.
        GameManager.Instance?.GoToRules();

        Debug.Log($"[LevelManager] Full reload: {sceneName}");
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        while (!load.isDone) yield return null;

        // One frame: lets all Awake() + Start() calls in the reloaded scene finish.
        yield return null;

        // Re-find scene fish (serialized Inspector refs on PlayerManager go null during
        // a Single reload because the old scene objects are destroyed).
        PlayerManager.Instance?.OnLevelLoaded();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private IEnumerator LoadLevelRoutine(string sceneName, bool notifyGameManager)
    {
        // Unload the previous level scene if one is active.
        if (!string.IsNullOrEmpty(_currentLevelScene))
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(_currentLevelScene);
            while (unload != null && !unload.isDone) yield return null;
        }

        // Load the new level additively — base scene stays alive.
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (!load.isDone) yield return null;

        _currentLevelScene = sceneName;
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
        Debug.Log($"[LevelManager] Loaded level: {sceneName} (index {_currentLevelIndex})");

        // Spawn players at the spawn points defined in the new scene.
        // Must happen before GoToRules so fish exist when the Rules screen shows.
        PlayerManager.Instance?.OnLevelLoaded();

        // After the level world is ready, move to the Rules screen so the researcher
        // can brief players before starting the round.
        if (notifyGameManager)
            GameManager.Instance?.GoToRules();
    }
}
