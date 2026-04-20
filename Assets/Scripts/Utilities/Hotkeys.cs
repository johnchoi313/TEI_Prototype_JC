using UnityEngine;

/// <summary>
/// Global hotkey manager.
///
/// Shift+K  — Toggle visibility of all assigned Kinect UI and Camera objects.
/// Shift+C  — Toggle both players between Keyboard and Kinect control schemes.
///            Player 1: WASD  ↔  Kinect (body ID 0)
///            Player 2: Arrow Keys  ↔  Kinect (body ID 1)
///
/// Assign objects in the Inspector. All fields are optional.
/// </summary>
public class Hotkeys : MonoBehaviour
{
    [Header("Shift+K — Kinect UI / Camera Toggle")]
    [Tooltip("GameObjects to show/hide when Shift+K is pressed")]
    public GameObject[] kinectObjects;

    [Header("Shift+C — Control Scheme Toggle")]
    [Tooltip("Player 1 light controller (WASD ↔ Kinect body 0)")]
    public PlayerLightController player1Controller;

    [Tooltip("Player 2 light controller (Arrow Keys ↔ Kinect body 1)")]
    public PlayerLightController player2Controller;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private bool _kinectVisible = true;
    private bool _usingKinect   = false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Sync toggle state with whatever scheme is set in the Inspector
        PlayerLightController reference = player1Controller != null ? player1Controller : player2Controller;
        if (reference != null)
            _usingKinect = reference.ActiveControlScheme == PlayerLightController.ControlScheme.Kinect;
    }

    private void Update()
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (shift && Input.GetKeyDown(KeyCode.K))
            ToggleKinectObjects();

        if (shift && Input.GetKeyDown(KeyCode.C))
            ToggleControlScheme();
    }

    // ── Hotkey actions ────────────────────────────────────────────────────────

    private void ToggleKinectObjects()
    {
        _kinectVisible = !_kinectVisible;

        if (kinectObjects != null)
            foreach (GameObject go in kinectObjects)
                if (go != null) go.SetActive(_kinectVisible);

        Debug.Log($"[Hotkeys] Kinect objects {(_kinectVisible ? "shown" : "hidden")} (Shift+K).");
    }

    private void ToggleControlScheme()
    {
        _usingKinect = !_usingKinect;

        SetPlayer(player1Controller, PlayerLightController.ControlScheme.Player1_WASD);
        SetPlayer(player2Controller, PlayerLightController.ControlScheme.Player2_ArrowKeys);

        Debug.Log($"[Hotkeys] Control scheme → {(_usingKinect ? "Kinect" : "Keyboard")} (Shift+C).");
    }

    private void SetPlayer(PlayerLightController lc,
                           PlayerLightController.ControlScheme keyboardScheme)
    {
        if (lc == null) return;
        lc.ActiveControlScheme = _usingKinect
            ? PlayerLightController.ControlScheme.Kinect
            : keyboardScheme;
    }
}
