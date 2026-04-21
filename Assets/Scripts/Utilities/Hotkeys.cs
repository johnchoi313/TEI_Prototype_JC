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
/// Shift+A     — Master show/hide: turns every UI panel on or off together.
/// Shift+Space — Start / stop logging (calls Logger.StartLogging / StopLogging).
/// Shift+R     — Regenerate the maze. Stops logging first if a session is active.
/// Shift+V     — Toggle visibility of the Minimap camera GameObject.
/// Shift+S     — Show/hide SimpleSpectrum bars by offsetting +100 Y (no SetActive).
/// Tab         — Swap fish abilities between players (BreakWall ↔ CollectStation).
///
/// All visibility states are saved to PlayerPrefs and restored on next run.
/// Assign objects in the Inspector. All fields are optional.
/// </summary>
public class Hotkeys : MonoBehaviour
{
    // ── PlayerPrefs keys ──────────────────────────────────────────────────────
    private const string PrefKinectUI     = "Hotkeys_KinectUI";
    private const string PrefMicUI        = "Hotkeys_MicUI";
    private const string PrefFPS          = "Hotkeys_FPS";
    private const string PrefLoggerUI     = "Hotkeys_LoggerUI";
    private const string PrefUsingKinect  = "Hotkeys_UsingKinect";
    private const string PrefMinimapUI    = "Hotkeys_MinimapUI";
    private const string PrefSpectrumUI   = "Hotkeys_SpectrumUI";

    private const float SpectrumHideOffset = 100f;

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

    [Header("Shift+R — Regenerate Maze")]
    [Tooltip("MazeGenerator to regenerate via Shift+R. Stops active logging first.")]
    public MazeGenerator mazeGenerator;

    [Header("Shift+L — Logger UI Toggle")]
    [Tooltip("The Logger UI panel to show/hide when Shift+L is pressed.")]
    public GameObject loggerUI;

    [Header("Shift+C — Control Scheme Toggle")]
    [Tooltip("Player 1 light controller (WASD ↔ Kinect body 0)")]
    public PlayerLightController player1Controller;

    [Tooltip("Player 2 light controller (Arrow Keys ↔ Kinect body 1)")]
    public PlayerLightController player2Controller;

    [Tooltip("Player 1 fish ability (E key ↔ Kinect jump)")]
    public FishAbility player1Ability;

    [Tooltip("Player 2 fish ability (Return key ↔ Kinect jump)")]
    public FishAbility player2Ability;

    [Tooltip("Shared FOV budget — switches between keyboard and Kinect depth mode alongside the control scheme.")]
    public SharedFOVBudget sharedFOVBudget;

    [Header("Shift+V — Minimap Camera Toggle")]
    [Tooltip("The Minimap camera GameObject to show/hide when Shift+V is pressed.")]
    public GameObject minimapCamera;

    [Header("Shift+S — SimpleSpectrum Toggle")]
    [Tooltip("SimpleSpectrum bars GameObject. Toggled by offsetting +100 Y instead of SetActive.")]
    public GameObject spectrumBars;

    [Header("Tab — Ability Swap")]
    [Tooltip("Renderer on Player 1's fish (or any indicator object) whose material is swapped on Tab.")]
    public Renderer player1AbilityRenderer;

    [Tooltip("Renderer on Player 2's fish (or any indicator object) whose material is swapped on Tab.")]
    public Renderer player2AbilityRenderer;

    [Tooltip("Material representing the Break Wall ability.")]
    public Material breakWallMaterial;

    [Tooltip("Material representing the Collect Station ability.")]
    public Material collectStationMaterial;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool  _kinectVisible   = true;
    private bool  _micUIVisible    = true;
    private bool  _fpsVisible      = true;
    private bool  _loggerVisible   = true;
    private bool  _usingKinect     = false;
    private bool  _minimapVisible  = true;
    private bool  _spectrumVisible = true;
    private float _spectrumOriginalY;

    // Master toggle for Shift+A — does not affect individual panel states or prefs.
    private bool  _allOn = true;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        LoadPrefs();
        ApplyObject(kinectObject,  _kinectVisible);
        ApplyObject(micDebugUI,    _micUIVisible);
        ApplyObject(fpsDisplay,    _fpsVisible);
        ApplyObject(loggerUI,      _loggerVisible);
        ApplyObject(minimapCamera, _minimapVisible);

        // Capture the scene-authored Y before any offset is applied, then restore state.
        if (spectrumBars != null)
        {
            _spectrumOriginalY = spectrumBars.transform.position.y;
            ApplySpectrumPosition(_spectrumVisible);
        }

        // Restore saved control scheme, then apply it to the controllers.
        PlayerLightController reference = player1Controller != null ? player1Controller : player2Controller;
        bool defaultKinect = reference != null && reference.ActiveControlScheme == PlayerLightController.ControlScheme.Kinect;
        _usingKinect = PlayerPrefs.GetInt(PrefUsingKinect, defaultKinect ? 1 : 0) == 1;
        SetPlayer(player1Controller, PlayerLightController.ControlScheme.Player1_WASD);
        SetPlayer(player2Controller, PlayerLightController.ControlScheme.Player2_ArrowKeys);
        SetAbility(player1Ability,   PlayerLightController.ControlScheme.Player1_WASD);
        SetAbility(player2Ability,   PlayerLightController.ControlScheme.Player2_ArrowKeys);
        if (player1Ability != null) ApplyAbilityMaterial(player1AbilityRenderer, player1Ability.CurrentAbilityType);
        if (player2Ability != null) ApplyAbilityMaterial(player2AbilityRenderer, player2Ability.CurrentAbilityType);
        if (sharedFOVBudget != null)
            sharedFOVBudget.UseKinectDepth = _usingKinect;
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

        if (shift && Input.GetKeyDown(KeyCode.R))
            RegenerateMaze();

        if (shift && Input.GetKeyDown(KeyCode.V))
            ToggleMinimapCamera();

        if (shift && Input.GetKeyDown(KeyCode.S))
            ToggleSpectrum();

        if (Input.GetKeyDown(KeyCode.Tab))
            SwapAbilities();
    }

    // ── Hotkey actions ────────────────────────────────────────────────────────

    private void ToggleShowAll()
    {
        _allOn = !_allOn;
        ApplyObject(kinectObject,  _allOn);
        ApplyObject(micDebugUI,    _allOn);
        ApplyObject(fpsDisplay,    _allOn);
        ApplyObject(loggerUI,      _allOn);
        ApplyObject(minimapCamera, _allOn);
        ApplySpectrumPosition(_allOn);
        Debug.Log($"[Hotkeys] All UI panels {(_allOn ? "shown" : "hidden")} (Shift+A).");
    }

    private void ToggleLoggerUI()
    {
        _loggerVisible = !_loggerVisible;
        ApplyObject(loggerUI, _loggerVisible);

        PlayerPrefs.SetInt(PrefLoggerUI, _loggerVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Logger UI {(_loggerVisible ? "shown" : "hidden")} (Shift+L).");
    }

    private void ToggleMicDebugUI()
    {
        _micUIVisible = !_micUIVisible;
        ApplyObject(micDebugUI, _micUIVisible);

        PlayerPrefs.SetInt(PrefMicUI, _micUIVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Mic Debug UI {(_micUIVisible ? "shown" : "hidden")} (Shift+M).");
    }

    private void ToggleFPSDisplay()
    {
        _fpsVisible = !_fpsVisible;
        ApplyObject(fpsDisplay, _fpsVisible);

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
        ApplyObject(kinectObject, _kinectVisible);

        PlayerPrefs.SetInt(PrefKinectUI, _kinectVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Kinect UI {(_kinectVisible ? "shown" : "hidden")} (Shift+K).");
    }

    private void RegenerateMaze()
    {
        if (mazeGenerator == null)
        {
            Debug.LogWarning("[Hotkeys] Cannot regenerate — assign a MazeGenerator (Shift+R).");
            return;
        }

        if (logger != null && logger.IsLogging)
        {
            logger.StopLogging();
            Debug.Log("[Hotkeys] Logging stopped before maze regeneration (Shift+R).");
        }

        mazeGenerator.Regenerate();
        Debug.Log("[Hotkeys] Maze regenerated (Shift+R).");
    }

    private void ToggleMinimapCamera()
    {
        _minimapVisible = !_minimapVisible;
        ApplyObject(minimapCamera, _minimapVisible);

        PlayerPrefs.SetInt(PrefMinimapUI, _minimapVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Minimap camera {(_minimapVisible ? "shown" : "hidden")} (Shift+V).");
    }

    private void ToggleSpectrum()
    {
        _spectrumVisible = !_spectrumVisible;
        ApplySpectrumPosition(_spectrumVisible);

        PlayerPrefs.SetInt(PrefSpectrumUI, _spectrumVisible ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Spectrum bars {(_spectrumVisible ? "shown" : "hidden")} (Shift+S).");
    }

    private void SwapAbilities()
    {
        if (player1Ability == null || player2Ability == null)
        {
            Debug.LogWarning("[Hotkeys] Cannot swap abilities — assign both player ability components (Tab).");
            return;
        }

        FishAbility.AbilityType p1 = player1Ability.CurrentAbilityType;
        player1Ability.CurrentAbilityType = player2Ability.CurrentAbilityType;
        player2Ability.CurrentAbilityType = p1;

        ApplyAbilityMaterial(player1AbilityRenderer, player1Ability.CurrentAbilityType);
        ApplyAbilityMaterial(player2AbilityRenderer, player2Ability.CurrentAbilityType);
        ScoreTracker.Instance?.AddSwap();
        ScreenFlash.Instance?.Flash();

        Debug.Log($"[Hotkeys] Abilities swapped — P1: {player1Ability.CurrentAbilityType}, P2: {player2Ability.CurrentAbilityType} (Tab).");
    }

    private void ApplyAbilityMaterial(Renderer rend, FishAbility.AbilityType abilityType)
    {
        if (rend == null) return;
        Material mat = abilityType == FishAbility.AbilityType.BreakWall
            ? breakWallMaterial
            : collectStationMaterial;
        if (mat != null)
            rend.sharedMaterial = mat;
    }

    private void ToggleControlScheme()
    {
        _usingKinect = !_usingKinect;

        SetPlayer(player1Controller, PlayerLightController.ControlScheme.Player1_WASD);
        SetPlayer(player2Controller, PlayerLightController.ControlScheme.Player2_ArrowKeys);
        SetAbility(player1Ability,   PlayerLightController.ControlScheme.Player1_WASD);
        SetAbility(player2Ability,   PlayerLightController.ControlScheme.Player2_ArrowKeys);

        if (sharedFOVBudget != null)
            sharedFOVBudget.UseKinectDepth = _usingKinect;

        PlayerPrefs.SetInt(PrefUsingKinect, _usingKinect ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"[Hotkeys] Control scheme → {(_usingKinect ? "Kinect" : "Keyboard")} (Shift+C).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyObject(GameObject go, bool visible)
    {
        if (go != null) go.SetActive(visible);
    }

    private void ApplySpectrumPosition(bool visible)
    {
        if (spectrumBars == null) return;
        Vector3 pos = spectrumBars.transform.position;
        pos.y = visible ? _spectrumOriginalY : _spectrumOriginalY + SpectrumHideOffset;
        spectrumBars.transform.position = pos;
    }

    private void SetPlayer(PlayerLightController lc,
                           PlayerLightController.ControlScheme keyboardScheme)
    {
        if (lc == null) return;
        lc.ActiveControlScheme = _usingKinect
            ? PlayerLightController.ControlScheme.Kinect
            : keyboardScheme;
    }

    private void SetAbility(FishAbility ability,
                            PlayerLightController.ControlScheme keyboardScheme)
    {
        if (ability == null) return;
        ability.ControlScheme = _usingKinect
            ? PlayerLightController.ControlScheme.Kinect
            : keyboardScheme;
    }

    // ── PlayerPrefs persistence ───────────────────────────────────────────────

    private void LoadPrefs()
    {
        _kinectVisible  = PlayerPrefs.GetInt(PrefKinectUI,  1) == 1;
        _micUIVisible   = PlayerPrefs.GetInt(PrefMicUI,     1) == 1;
        _fpsVisible     = PlayerPrefs.GetInt(PrefFPS,       1) == 1;
        _loggerVisible  = PlayerPrefs.GetInt(PrefLoggerUI,  1) == 1;
        _minimapVisible  = PlayerPrefs.GetInt(PrefMinimapUI,  1) == 1;
        _spectrumVisible = PlayerPrefs.GetInt(PrefSpectrumUI, 1) == 1;
        // _usingKinect is loaded directly in Awake (needs Inspector fallback value)
    }
}
