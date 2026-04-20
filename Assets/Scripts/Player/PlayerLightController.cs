using UnityEngine;

/// <summary>
/// Physics-free light controller for the maze.
///
/// The light is a transform-only object (no collider, no Rigidbody).
/// It moves via direct Transform.position manipulation each Update,
/// accelerating up to _maxSpeed and decelerating when input is released.
///
/// Movement is XY only — the object's Z coordinate is never touched.
///
/// The XY position is hard-clamped to _bounds every frame so the light
/// can never leave the playable area, even though it ignores wall colliders.
///
/// The paired PlayerFishController reads this object's Transform to decide
/// whether to follow (range + line-of-sight check on the fish side).
///
/// SCENE SETUP
///   1. Create an empty GameObject (or a sprite / Unity Light GameObject).
///      Do NOT add a Rigidbody or Collider.
///   2. Attach PlayerLightController.
///   3. Set _bounds to the XY rectangle of the playable area
///      (xMin/yMin = bottom-left corner, width/height of the inner maze area).
///      Leave width=0 to disable clamping during early testing.
///   4. Set _controlScheme to match the paired PlayerFishController.
/// </summary>
public class PlayerLightController : MonoBehaviour
{
    public enum ControlScheme { Player1_WASD, Player2_ArrowKeys, Kinect }

    [Header("Control")]
    [Tooltip("Player1 uses WASD. Player2 uses Arrow Keys. Kinect uses body tracking.")]
    [SerializeField] private ControlScheme _controlScheme = ControlScheme.Player1_WASD;

    [Tooltip("KinectPlayerController to read from when control scheme is set to Kinect")]
    [SerializeField] private KinectPlayerController _kinectController;

    /// <summary>Get or set the active control scheme at runtime (e.g. from Hotkeys).</summary>
    public ControlScheme ActiveControlScheme
    {
        get => _controlScheme;
        set => _controlScheme = value;
    }

    [Header("Movement")]
    [Tooltip("Maximum movement speed (wu/s).")]
    [SerializeField] private float _maxSpeed = 4f;

    [Tooltip("How quickly the light accelerates to max speed (wu/s²).")]
    [SerializeField] private float _acceleration = 30f;

    [Tooltip("How quickly the light decelerates when input is released (wu/s²).")]
    [SerializeField] private float _deceleration = 40f;

    [Header("Boundary")]
    [Tooltip("XY rectangle the light cannot leave. Set to the inner playable area of the maze. " +
             "Leave width = 0 to disable clamping.")]
    [SerializeField] private Rect _bounds = new Rect(-10f, -7.5f, 20f, 15f);

    [Header("Light Visual")]
    [Tooltip("Radius of the visible light — read by PlayerFishController to determine follow range.")]
    [SerializeField] private float _lightRadius = 5f;

    [Header("FOV Object")]
    [Tooltip("Transform offset from its initial world position by (RawInput * _fovOffset).")]
    [SerializeField] private Transform _fovObject;

    [Tooltip("World-unit multiplier applied to the raw input axis before offsetting the FOV object.")]
    [SerializeField] private Vector2 _fovOffset = new Vector2(2f, 2f);

    [Tooltip("Lerp speed for smoothing FOV object movement. Higher = snappier, lower = more lag.")]
    [SerializeField, Range(0f, 50f)] private float _fovSmoothing = 10f;

    [Header("FOV Zoom")]
    [Tooltip("Camera whose orthographic size is adjusted by the zoom keys.")]
    [SerializeField] private Camera _fovCamera;

    [Tooltip("How fast orthographic size and FOV object scale change per second while the key is held.")]
    [SerializeField] private float _zoomSpeed = 1f;

    [Tooltip("Default (starting) value for both orthographic size and FOV object uniform scale.")]
    [SerializeField] private float _zoomDefault = 3f;

    [Tooltip("Minimum value for both orthographic size and FOV object uniform scale.")]
    [SerializeField] private float _zoomMin = 1f;

    [Tooltip("Maximum value for both orthographic size and FOV object uniform scale.")]
    [SerializeField] private float _zoomMax = 8f;

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>Current XY velocity of the light this frame.</summary>
    public Vector3 Velocity => _velocity;

    /// <summary>How far the light illuminates — read by PlayerFishController.</summary>
    public float LightRadius => _lightRadius;

    /// <summary>
    /// Raw normalised input axis this frame (-1 to 1 on each axis).
    /// Read by PlayerCameraCircle to position the circular HUD viewport.
    /// </summary>
    public Vector2 RawInput { get; private set; }

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Vector3 _velocity = Vector3.zero;
    private float   _originZ;

    // Initial world position of the FOV object, captured once in Awake.
    private Vector3 _fovOrigin;

    // Current zoom value shared by both ortho size and FOV object scale.
    private float _currentZoom;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _originZ = transform.position.z;

        _currentZoom = _zoomDefault;

        if (_fovObject != null)
        {
            _fovOrigin = _fovObject.position;
            _fovObject.localScale = new Vector3(_currentZoom, _currentZoom, _fovObject.localScale.z);
        }

        if (_fovCamera != null)
            _fovCamera.orthographicSize = _currentZoom;
    }

    private void Update()
    {
        MoveLight();
        ClampToBounds();
        MoveFOVObjects();
        UpdateZoom();
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private void MoveLight()
    {
        Vector2 input = ReadInput();

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        RawInput = input;

        // Only XY — velocity Z stays zero so the object never drifts on Z.
        Vector3 desiredVelocity = new Vector3(input.x, input.y, 0f) * _maxSpeed;

        _velocity = input.sqrMagnitude > 0.01f
            ? Vector3.MoveTowards(_velocity, desiredVelocity, _acceleration  * Time.deltaTime)
            : Vector3.MoveTowards(_velocity, Vector3.zero,   _deceleration * Time.deltaTime);

        // Apply XY delta only; restore the original Z every frame.
        Vector3 p = transform.position;
        p.x += _velocity.x * Time.deltaTime;
        p.y += _velocity.y * Time.deltaTime;
        p.z  = _originZ;
        transform.position = p;
    }

    // ── FOV object positioning ────────────────────────────────────────────────

    private void MoveFOVObjects()
    {
        if (_fovObject == null) return;

        Vector3 target = _fovOrigin + new Vector3(
            RawInput.x * _fovOffset.x,
            RawInput.y * _fovOffset.y,
            0f);

        _fovObject.position = Vector3.Lerp(
            _fovObject.position,
            target,
            _fovSmoothing * Time.deltaTime);
    }

    // ── FOV zoom ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Raw zoom input direction this frame (-1, 0, or 1).
    /// Read by SharedFOVBudget each frame to compute how the budget should shift.
    /// </summary>
    public float ZoomInputDirection => ReadZoomInput();

    /// <summary>Zoom speed in units/second. Read by SharedFOVBudget.</summary>
    public float ZoomSpeed => _zoomSpeed;

    /// <summary>
    /// Current zoom value. Read by SharedFOVBudget.
    /// </summary>
    public float CurrentZoom => _currentZoom;

    /// <summary>
    /// Apply a zoom value directly (called by SharedFOVBudget).
    /// Clamps to [_zoomMin, _zoomMax] and pushes to camera and FOV object.
    /// </summary>
    public void ApplyZoom(float zoom)
    {
        _currentZoom = Mathf.Clamp(zoom, _zoomMin, _zoomMax);

        if (_fovCamera != null)
            _fovCamera.orthographicSize = _currentZoom;

        if (_fovObject != null)
            _fovObject.localScale = new Vector3(_currentZoom, _currentZoom, _fovObject.localScale.z);
    }

    private void UpdateZoom()
    {
        // When no SharedFOVBudget is present, fall back to self-managed zoom.
        if (SharedFOVBudget.Instance != null) return;

        float direction = ReadZoomInput();
        if (Mathf.Approximately(direction, 0f)) return;

        ApplyZoom(_currentZoom + direction * _zoomSpeed * Time.deltaTime);
    }

    private float ReadZoomInput()
    {
        if (_controlScheme == ControlScheme.Player1_WASD)
        {
            if (Input.GetKey(KeyCode.R)) return  1f;
            if (Input.GetKey(KeyCode.F)) return -1f;
        }
        else if (_controlScheme == ControlScheme.Player2_ArrowKeys)
        {
            if (Input.GetKey(KeyCode.RightShift)) return  1f;
            if (Input.GetKey(KeyCode.RightControl)) return -1f;
        }
        return 0f;
    }

    // ── Boundary clamping ─────────────────────────────────────────────────────

    private void ClampToBounds()
    {
        if (_bounds.width <= 0f) return;

        Vector3 p = transform.position;

        float clampedX = Mathf.Clamp(p.x, _bounds.xMin, _bounds.xMax);
        float clampedY = Mathf.Clamp(p.y, _bounds.yMin, _bounds.yMax);

        // Kill velocity on whichever axis hit the boundary.
        if (!Mathf.Approximately(clampedX, p.x)) _velocity.x = 0f;
        if (!Mathf.Approximately(clampedY, p.y)) _velocity.y = 0f;

        p.x = clampedX;
        p.y = clampedY;
        transform.position = p;
    }

    // ── Bounds API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Set the playable boundary at runtime (e.g. called by MazeGenerator after generation).
    /// </summary>
    public void SetBounds(Rect bounds) => _bounds = bounds;

    // ── Input ─────────────────────────────────────────────────────────────────

    private Vector2 ReadInput()
    {
        if (_controlScheme == ControlScheme.Player1_WASD)
        {
            return new Vector2(
                (Input.GetKey(KeyCode.A) ? 1f : 0f) - (Input.GetKey(KeyCode.D) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f)
            );
        }
        else if (_controlScheme == ControlScheme.Player2_ArrowKeys)
        {
            return new Vector2(
                (Input.GetKey(KeyCode.LeftArrow)  ? 1f : 0f) - (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow)    ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow)  ? 1f : 0f)
            );
        }
        else // Kinect
        {
            if (_kinectController == null) return Vector2.zero;
            return _kinectController.InputAxis;
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = new Color(1f, 0.95f, 0.4f, 0.4f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, _lightRadius);

        if (_bounds.width <= 0f) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireCube(new Vector3(_bounds.center.x, _bounds.center.y, transform.position.z),
                            new Vector3(_bounds.width, _bounds.height, 0.1f));
    }
#endif
}
