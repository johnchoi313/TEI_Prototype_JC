using UnityEngine;

/// <summary>
/// Maintains a shared FOV budget between two players such that
/// P1 zoom + P2 zoom always equals _totalBudget (default 10).
///
/// KEYBOARD MODE:
///   P1's zoom keys shift the budget toward P1; P2's keys shift it toward P2.
///   If both press simultaneously the net delta is zero.
///
/// KINECT DEPTH MODE (Shift+C to toggle):
///   Pelvis Z range is assumed to be [-1, 1] (total range = 2).
///   Usable FOV per player = _totalBudget - (2 * _perPlayerMin).
///
///   BOTH PLAYERS ACTIVE — differential:
///     zDiff = p2Z - p1Z, clamped to [-_maxZDiff, +_maxZDiff].
///     p1Share = midpoint + (zDiff / _maxZDiff) * halfUsable
///     At max diff, one player holds (_totalBudget - _perPlayerMin),
///     the other holds _perPlayerMin.
///
///   ONE PLAYER ACTIVE — absolute Z of the active player:
///     The active player's Z is remapped from [-1.5, 1.5] across the full
///     usable range. Higher Z = larger FOV for that player.
///     Inactive player gets the remainder.
///
///   FOV changes are smoothed via Lerp to avoid jarring jumps from noisy Z values.
///
/// SCENE SETUP:
///   Add to any persistent manager GameObject.
///   Assign _p1, _p2 (PlayerLightController) and _p1Kinect, _p2Kinect.
///   Shift+C in Hotkeys toggles UseKinectDepth.
/// </summary>
public class SharedFOVBudget : MonoBehaviour
{
    public static SharedFOVBudget Instance { get; private set; }

    [Header("Players")]
    [SerializeField] private PlayerLightController _p1;
    [SerializeField] private PlayerLightController _p2;

    [Header("Budget")]
    [Tooltip("Combined zoom total shared between both players.")]
    [SerializeField] private float _totalBudget = 10f;

    [Tooltip("Minimum zoom either player can hold.")]
    [SerializeField] private float _perPlayerMin = 2f;

    [Header("Kinect Depth Mode")]
    [Tooltip("Toggled by Hotkeys Shift+C to match the active control scheme.")]
    [SerializeField] private bool _useKinectDepth = false;

    [SerializeField] private KinectPlayerController _p1Kinect;
    [SerializeField] private KinectPlayerController _p2Kinect;

    [Tooltip("Maximum Z differential between two active players that maps to the full FOV range. " +
             "Pelvis Z spans [-1.5, 1.5] so a max diff of 3 covers the full range.")]
    [SerializeField] private float _maxZDiff = 3f;

    [Tooltip("Z range minimum for a single active player's absolute depth mapping.")]
    [SerializeField] private float _zMin = -1.5f;

    [Tooltip("Z range maximum for a single active player's absolute depth mapping.")]
    [SerializeField] private float _zMax = 1.5f;

    [Tooltip("Lerp speed for smoothing FOV changes in Kinect mode. Higher = snappier.")]
    [SerializeField] private float _kinectFOVSmoothing = 5f;

    /// <summary>Toggled by Hotkeys (Shift+C) to match the active control scheme.</summary>
    public bool UseKinectDepth
    {
        get => _useKinectDepth;
        set => _useKinectDepth = value;
    }

    // ── Runtime ───────────────────────────────────────────────────────────────

    private float _smoothedP1Zoom = -1f; // -1 = uninitialised

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (_p1 == null || _p2 == null) return;

        float p1Zoom;

        if (_useKinectDepth)
        {
            float target = ComputeKinectZoom();
            target = Mathf.Clamp(target, _perPlayerMin, _totalBudget - _perPlayerMin);

            if (_smoothedP1Zoom < 0f) _smoothedP1Zoom = target; // first frame snap
            _smoothedP1Zoom = Mathf.Lerp(_smoothedP1Zoom, target, _kinectFOVSmoothing * Time.deltaTime);
            p1Zoom = _smoothedP1Zoom;
        }
        else
        {
            p1Zoom = Mathf.Clamp(ComputeKeyboardZoom(), _perPlayerMin, _totalBudget - _perPlayerMin);
            _smoothedP1Zoom = p1Zoom; // keep in sync so switching modes doesn't jump
        }

        _p1.ApplyZoom(p1Zoom);
        _p2.ApplyZoom(_totalBudget - p1Zoom);
    }

    // ── Keyboard mode ─────────────────────────────────────────────────────────

    private float ComputeKeyboardZoom()
    {
        float netDir = _p1.ZoomInputDirection - _p2.ZoomInputDirection;
        if (Mathf.Approximately(netDir, 0f))
            return _p1.CurrentZoom;

        return _p1.CurrentZoom + netDir * _p1.ZoomSpeed * Time.deltaTime;
    }

    // ── Kinect depth mode ─────────────────────────────────────────────────────

    private float ComputeKinectZoom()
    {
        if (_p1Kinect == null || _p2Kinect == null)
            return _totalBudget * 0.5f;

        bool p1Active = _p1Kinect.IsTracked;
        bool p2Active = _p2Kinect.IsTracked;

        float usableRange = _totalBudget - 2f * _perPlayerMin;
        float midpoint    = _totalBudget * 0.5f;

        if (p1Active && p2Active)
        {
            // Differential: positive zDiff means P2 is further back → P2 gets more FOV.
            float zDiff  = Mathf.Clamp(_p2Kinect.WorldZ - _p1Kinect.WorldZ, -_maxZDiff, _maxZDiff);
            float t      = zDiff / _maxZDiff;           // -1 to 1
            return midpoint + t * (usableRange * 0.5f); // p1 share
        }

        if (p1Active)
        {
            float t = Mathf.InverseLerp(_zMin, _zMax, _p1Kinect.WorldZ);
            return Mathf.Lerp(_perPlayerMin, _totalBudget - _perPlayerMin, t);
        }

        if (p2Active)
        {
            float t      = Mathf.InverseLerp(_zMin, _zMax, _p2Kinect.WorldZ);
            float p2Zoom = Mathf.Lerp(_perPlayerMin, _totalBudget - _perPlayerMin, t);
            return _totalBudget - p2Zoom;
        }

        // Neither active — even split.
        return midpoint;
    }
}
