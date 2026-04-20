using UnityEngine;

/// <summary>
/// Global hotkey manager.
///
/// Shift+K     — Toggle visibility of the Kinect UI object.
/// Shift+C     — Toggle both players between Keyboard and Kinect control schemes.
///               Player 1: WASD  ↔  Kinect (body ID 0)
///               Player 2: Arrow Keys  ↔  Kinect (body ID 1)
/// Shift+M     — Toggle visibility of the Microphone Debug UI panel.
/// Shift+F     — Toggle visibility of the FPS display.
/// Shift+L     — Toggle visibility of the Logger UI panel.
/// Shift+A     — Show all / hide all UI panels (Mic, FPS, Logger, Kinect).
/// Shift+Space — Start / stop logging (calls Logger.StartLogging / StopLogging).
///
/// All visibility states are saved to PlayerPrefs and restored on next run.
/// Assign objects in the Inspector. All fields are optional.
/// </summary>
public class Hotkeys : MonoBehaviour
{
    // ── PlayerPrefs keys ──────────────────────────────────────────────────────
    private const string PrefKinectUI     = "Hotkeys_KinectUI";
    private const string PrefMicUI        = "Hotkeys_MicUI";
    private const string PrefFPS          = "Hotkeys_FPS";  // reused — key name unchanged
    private const string PrefLoggerUI     = "Hotkeys_LoggerUI";
    private const string PrefShowAll      = "Hotkeys_ShowAll";

    // ── Inspector references ──────────────────────────────────────────────────

    [Header("Shift+K — Kinect UI Toggle")]
    [Tooltip("Kinect UI object to show/hide when Shift+K is pressed.")]
    public GameObject kinectObject;

    [Header("Shift+M — Mic Debug UI Toggle")]
    [Tooltip("The Microphone Debug UI panel to show/hide when Shift+M is pressed.")]
    public GameObject micDebugUI;

    [Header("Shift+F — FPS Display Toggle")]
    [Tooltip("The FPS display GameObject to show/hide when Shift+F is pressed.")]
    public GameObject fpsDisplay;

    [Header("Shift+Space — Logger Start / Stop")]
    [Tooltip("Logger component to start/stop via Shift+Space.")]
    public Logger logger;

    [Header("Shift+L — Logger UI Toggle")]
    [Tooltip("The Logger UI panel to show/hide when Shift+L is pressed.")]
    public GameObject loggerUI;

    [Header("Shift+C — Control Scheme Toggle")]
    [Tooltip("Player 1 light controller (WASD ↔ Kinect body 0)")]
    public PlayerLightController player1Controller;

    [Tooltip("Player 2 light controller (Arrow Keys ↔ Kinect body 1)")]
    public PlayerLightController player2Controller;

    [Tooltip("Shared FOV budget — switches between keyboard and Kinect depth mode alongside the control scheme.")]
    public SharedFOVBudget sharedFOVBudget;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool _kinectVisible = true;
    private bool _micUIVisible  = true;
    private bool _fpsVisible    = true;
    private bool _loggerVisible = true;
    private bool _allVisible    = true;
    private bool _usingKinect   = false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        LoadPrefs();
        ApplyObject(kinectObject, _kinectVisible && _allVisible);
        ApplyObject(micDebugUI,   _micUIVisible  && _allVisible);
        ApplyObject(fpsDisplay,   _fpsVisible    && _allVisible);
        ApplyObject(loggerUI,     _loggerVisible && _allVisible);

        PlayerLightController reference = player1Controller != null ? player1Controller : player2Controller;
        if (reference != null)
            _usingKinect = reference.ActiveControlScheme == PlayerLightController.ControlScheme.Kinect;
    }

    private void Update()
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (shift && Input.GetKeyDown(KeyCode.K))
            ToggleKinectObjects();

        if (shift && Input.GetKeyDown(KeyCode.M))
            ToggleMicDebugUI();

        if (shift && Input.GetKeyDown(KeyCode.F))
            ToggleFPSDisplay();

        if (shift && Input.GetKeyDown(KeyCode.Space))
            ToggleLogging();

        if (shift && Input.GetKeyDown(KeyCode.C))
            ToggleControlScheme();

        if (shift && Input.GetKeyDown(KeyCode.L))
            ToggleLoggerUI();

        if (shift && Input.GetKeyDown(KeyCode.A))
            ToggleShowAll();
    }

    // ── Hotkey actions ────────────────────────────────────────────────────────

    private void ToggleShowAll()
    {
        _allVisible = !_allVisible;

        if (_allVisible)
        {
            // Restore individual states
            ApplyObject(kinectObject, _kinectVisible);
            ApplyObject(micDebugUI,   _micUIVisible);
            ApplyObject(fpsDisplay,   _fpsVisible);
            ApplyObject(loggerUI,     _loggerVisible);
        }
        else
        {
            // Hide everything regardless of individual states
            ApplyObject(kinectObject, false);
            ApplyObject(micDebugUI,   false);
            ApplyObject(fpsDisplay,   false);
            ApplyObject(loggerUI,     false);
        }

        PlayerPrefs.SetInt(PrefShowAll, _allVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Show all → {(_allVisible ? "restored" : "hidden")} (Shift+A).");
    }

    private void ToggleLoggerUI()
    {
        _loggerVisible = !_loggerVisible;
        ApplyObject(loggerUI, _loggerVisible && _allVisible);

        PlayerPrefs.SetInt(PrefLoggerUI, _loggerVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Logger UI {(_loggerVisible ? "shown" : "hidden")} (Shift+L).");
    }

    private void ToggleMicDebugUI()
    {
        _micUIVisible = !_micUIVisible;
        ApplyObject(micDebugUI, _micUIVisible && _allVisible);

        PlayerPrefs.SetInt(PrefMicUI, _micUIVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Mic Debug UI {(_micUIVisible ? "shown" : "hidden")} (Shift+M).");
    }

    private void ToggleFPSDisplay()
    {
        _fpsVisible = !_fpsVisible;
        ApplyObject(fpsDisplay, _fpsVisible && _allVisible);

        PlayerPrefs.SetInt(PrefFPS, _fpsVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] FPS display {(_fpsVisible ? "shown" : "hidden")} (Shift+F).");
    }

    private void ToggleLogging()
    {
        if (logger == null) return;

        if (logger.IsLogging)
        {
            logger.StopLogging();
            Debug.Log("[Hotkeys] Logging stopped (Shift+Space).");
        }
        else
        {
            logger.StartLogging();
            Debug.Log("[Hotkeys] Logging started (Shift+Space).");
        }
    }

    private void ToggleKinectObjects()
    {
        _kinectVisible = !_kinectVisible;
        ApplyObject(kinectObject, _kinectVisible && _allVisible);

        PlayerPrefs.SetInt(PrefKinectUI, _kinectVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Kinect UI {(_kinectVisible ? "shown" : "hidden")} (Shift+K).");
    }

    private void ToggleControlScheme()
    {
        _usingKinect = !_usingKinect;

        SetPlayer(player1Controller, PlayerLightController.ControlScheme.Player1_WASD);
        SetPlayer(player2Controller, PlayerLightController.ControlScheme.Player2_ArrowKeys);

        if (sharedFOVBudget != null)
            sharedFOVBudget.UseKinectDepth = _usingKinect;

        Debug.Log($"[Hotkeys] Control scheme → {(_usingKinect ? "Kinect" : "Keyboard")} (Shift+C).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyObject(GameObject go, bool visible)
    {
        if (go != null) go.SetActive(visible);
    }

    private void SetPlayer(PlayerLightController lc,
                           PlayerLightController.ControlScheme keyboardScheme)
    {
        if (lc == null) return;
        lc.ActiveControlScheme = _usingKinect
            ? PlayerLightController.ControlScheme.Kinect
            : keyboardScheme;
    }

    // ── PlayerPrefs persistence ───────────────────────────────────────────────

    private void LoadPrefs()
    {
        _kinectVisible = PlayerPrefs.GetInt(PrefKinectUI,  1) == 1;
        _micUIVisible  = PlayerPrefs.GetInt(PrefMicUI,     1) == 1;
        _fpsVisible    = PlayerPrefs.GetInt(PrefFPS,       1) == 1;
        _loggerVisible = PlayerPrefs.GetInt(PrefLoggerUI,  1) == 1;
        _allVisible    = PlayerPrefs.GetInt(PrefShowAll,   1) == 1;
    }
}
