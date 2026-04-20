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
    public enum ControlScheme { Player1_WASD, Player2_ArrowKeys }

    [Header("Control")]
    [Tooltip("Player1 uses WASD. Player2 uses Arrow Keys.")]
    [SerializeField] private ControlScheme _controlScheme = ControlScheme.Player1_WASD;

    [Header("Movement")]
    [Tooltip("Maximum movement speed (wu/s).")]
    [SerializeField] private float _maxSpeed = 8f;

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

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>Current XY velocity of the light this frame.</summary>
    public Vector3 Velocity => _velocity;

    /// <summary>How far the light illuminates — read by PlayerFishController.</summary>
    public float LightRadius => _lightRadius;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Vector3 _velocity = Vector3.zero;
    private float   _originZ;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Capture the Z the designer placed this object at — never touched again.
        _originZ = transform.position.z;
    }

    private void Update()
    {
        MoveLight();
        ClampToBounds();
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private void MoveLight()
    {
        Vector2 input = ReadInput();

        if (input.sqrMagnitude > 1f)
            input.Normalize();

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
        else
        {
            return new Vector2(
                (Input.GetKey(KeyCode.LeftArrow)  ? 1f : 0f) - (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow)    ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow)  ? 1f : 0f)
            );
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
