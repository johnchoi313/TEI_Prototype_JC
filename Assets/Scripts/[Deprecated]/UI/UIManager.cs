using UnityEngine;

/// <summary>
/// Single source of truth for which UI panel is visible.
///
/// Sits on the root Canvas GameObject (always enabled). Subscribes to
/// GameManager.OnStateChanged in Awake and shows exactly one panel per state.
/// Individual panel scripts handle only their own button callbacks and data —
/// NOT visibility. UIManager owns all SetActive calls.
///
/// SCENE SETUP
///   1. Add this component to the root UICanvas GameObject.
///   2. Wire each panel GameObject in the Inspector.
///   3. Panel initial enabled state doesn't matter — UIManager corrects on Start().
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Panels — assign the panel GameObject for each state")]
    [Tooltip("Shown during GameState.MainMenu")]
    [SerializeField] private GameObject homeScreenPanel;

    [Tooltip("Shown during GameState.Rules")]
    [SerializeField] private GameObject rulesPanel;

    [Tooltip("Shown during GameState.PreGame (countdown overlay)")]
    [SerializeField] private GameObject preGamePanel;

    [Tooltip("Shown during GameState.Playing (HUD: timer, scores)")]
    [SerializeField] private GameObject hudPanel;

    [Tooltip("Shown during GameState.LevelComplete (results / next level)")]
    [SerializeField] private GameObject levelCompletePanel;

    [Header("World Overlays")]
    [Tooltip("The Minimap prefab root. Enabled only during Playing to avoid spoiling the puzzle layout.")]
    [SerializeField] private GameObject minimapRoot;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
        else
        {
            Debug.Log("[UIManager] Cant find GameManager");
        }
    }

    private void Start()
    {
        // Sync with current state. Handles _debugStartState skipping past MainMenu.
        if (GameManager.Instance != null)
            ShowPanelForState(GameManager.Instance.State);
        else
        {
            Debug.Log("[UIManager] Cant find GameManager");
        }
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
        ShowPanelForState(next);
    }

    private void ShowPanelForState(GameState state)
    {
        homeScreenPanel?.SetActive(state == GameState.MainMenu);
        rulesPanel?.SetActive(state == GameState.Rules);
        preGamePanel?.SetActive(state == GameState.PreGame);
        hudPanel?.SetActive(state == GameState.Playing);
        levelCompletePanel?.SetActive(state == GameState.LevelComplete);

        // Minimap is hidden until Playing to avoid spoiling the puzzle layout.
        minimapRoot?.SetActive(state == GameState.Playing);
    }
}
