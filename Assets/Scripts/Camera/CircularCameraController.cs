using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Two-player circular camera system driven by PlayerLightController input.
///
/// CONCEPT:
///   Each player has a dedicated camera that renders into a RenderTexture.
///   The camera views are displayed as circular "portholes" on the HUD.
///   The circle centre on screen tracks the player's light position — as
///   the light moves within the maze bounds, the circle drifts up/down/left/right
///   across the screen in the same direction.
///
///   PROXIMITY → MERGE:
///   When both light positions are close (in world space), the two circles
///   drift toward each other. When they are "super close" (mergeFullThreshold),
///   the circles have fully overlapped and a MergeProgress value of 1 is
///   published for the shader to blend the two views into one.
///
/// SCENE SETUP:
///   1. Create a Canvas (Screen Space - Overlay or Camera), add a full-screen
///      RawImage as the compositing target.
///   2. Add a material using your FOV/circular-camera shader to that RawImage.
///   3. Create two cameras in the scene, set their Target Texture to a matching
///      RenderTexture asset. Assign those cameras and render textures here.
///   4. Assign both PlayerLightController references.
///   5. The shader must accept:
///        _P1_MainTex, _P2_MainTex       (RenderTexture inputs)
///        _P1_CircleCenter  (Vector4, xy = screen UV 0-1)
///        _P2_CircleCenter  (Vector4, xy = screen UV 0-1)
///        _CircleRadius     (Float, normalised screen-space radius)
///        _MergeProgress    (Float, 0 = separate circles, 1 = fully merged)
///      CircularCameraShaderBridge handles all shader writes.
///
/// COORDINATE MAPPING:
///   The light's XY world position is remapped from the maze WorldBounds rect
///   into 0–1 screen UV, then scaled by _screenPanRange so the circle only
///   travels a portion of the screen (leaving space for both circles).
///   Centre of that range per player is offset by _p1ScreenOffset / _p2ScreenOffset.
/// </summary>
[DefaultExecutionOrder(-10)]
public class CircularCameraController : MonoBehaviour
{
    public static CircularCameraController Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Player Lights")]
    [Tooltip("PlayerLightController for Player 1.")]
    [SerializeField] private PlayerLightController _p1Light;

    [Tooltip("PlayerLightController for Player 2.")]
    [SerializeField] private PlayerLightController _p2Light;

    [Header("Cameras & Render Textures")]
    [Tooltip("Camera that renders the P1 view into P1RenderTexture.")]
    [SerializeField] private Camera _p1Camera;

    [Tooltip("Camera that renders the P2 view into P2RenderTexture.")]
    [SerializeField] private Camera _p2Camera;

    [SerializeField] private RenderTexture _p1RenderTexture;
    [SerializeField] private RenderTexture _p2RenderTexture;

    [Header("Maze Bounds")]
    [Tooltip("MazeGenerator to read WorldBounds from at runtime. " +
             "If null, _fallbackBounds is used (useful in non-maze scenes).")]
    [SerializeField] private MazeGenerator _mazeGenerator;

    [Tooltip("Fallback XY bounds used when MazeGenerator is not assigned.")]
    [SerializeField] private Rect _fallbackBounds = new Rect(-10f, -7.5f, 20f, 15f);

    [Header("Screen Layout")]
    [Tooltip("Default screen-space UV centre for P1's circle (0,0 = bottom-left, 1,1 = top-right). " +
             "The light's position offsets from this centre.")]
    [SerializeField] private Vector2 _p1BaseCenter = new Vector2(0.30f, 0.50f);

    [Tooltip("Default screen-space UV centre for P2's circle.")]
    [SerializeField] private Vector2 _p2BaseCenter = new Vector2(0.70f, 0.50f);

    [Tooltip("How far (in screen UV units, 0–1) the circle centre can travel from its base " +
             "centre when the light is at the edge of the maze. " +
             "0.15 means the circle centre moves up to 15% of screen width/height.")]
    [SerializeField, Range(0f, 0.45f)] private float _screenPanRange = 0.15f;

    [Tooltip("Radius of each circular viewport in normalised screen UV units.")]
    [SerializeField, Range(0.05f, 0.5f)] private float _circleRadius = 0.25f;

    [Header("Proximity & Merge")]
    [Tooltip("World-space distance at which the circles begin moving toward each other.")]
    [SerializeField] private float _approachThreshold = 8f;

    [Tooltip("World-space distance at which the merge is complete (MergeProgress = 1).")]
    [SerializeField] private float _mergeFullThreshold = 3f;

    [Tooltip("How fast MergeProgress transitions (lerp speed, higher = snappier).")]
    [SerializeField] private float _mergeSpeed = 3f;

    // ── Public state (read by CircularCameraShaderBridge) ─────────────────────

    /// <summary>Current screen UV centre of P1's circle (0-1 each axis).</summary>
    public Vector2 P1CircleCenter { get; private set; }

    /// <summary>Current screen UV centre of P2's circle (0-1 each axis).</summary>
    public Vector2 P2CircleCenter { get; private set; }

    /// <summary>Circle radius in normalised screen UV. Constant unless overridden.</summary>
    public float CircleRadius => _circleRadius;

    /// <summary>0 = circles fully separate, 1 = circles fully merged.</summary>
    public float MergeProgress { get; private set; }

    public RenderTexture P1RenderTexture => _p1RenderTexture;
    public RenderTexture P2RenderTexture => _p2RenderTexture;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private float _mergeProgressTarget;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Ensure cameras render to their RTs.
        if (_p1Camera != null && _p1RenderTexture != null)
            _p1Camera.targetTexture = _p1RenderTexture;
        if (_p2Camera != null && _p2RenderTexture != null)
            _p2Camera.targetTexture = _p2RenderTexture;

        P1CircleCenter = _p1BaseCenter;
        P2CircleCenter = _p2BaseCenter;
    }

    private void Update()
    {
        Rect bounds = GetBounds();
        UpdateCirclePositions(bounds);
        UpdateMergeProgress(bounds);
    }

    // ── Circle position ───────────────────────────────────────────────────────

    private void UpdateCirclePositions(Rect bounds)
    {
        Vector2 p1NormPos = GetNormalisedPosition(_p1Light, bounds);
        Vector2 p2NormPos = GetNormalisedPosition(_p2Light, bounds);

        // Remap from [0,1] to [-1,1] then scale by pan range.
        Vector2 p1Pan = (p1NormPos * 2f - Vector2.one) * _screenPanRange;
        Vector2 p2Pan = (p2NormPos * 2f - Vector2.one) * _screenPanRange;

        // When merging, pull both circles toward a shared midpoint.
        Vector2 rawP1Center = _p1BaseCenter + p1Pan;
        Vector2 rawP2Center = _p2BaseCenter + p2Pan;

        if (MergeProgress > 0f)
        {
            Vector2 midpoint = (rawP1Center + rawP2Center) * 0.5f;
            P1CircleCenter = Vector2.Lerp(rawP1Center, midpoint, MergeProgress);
            P2CircleCenter = Vector2.Lerp(rawP2Center, midpoint, MergeProgress);
        }
        else
        {
            P1CircleCenter = rawP1Center;
            P2CircleCenter = rawP2Center;
        }
    }

    private Vector2 GetNormalisedPosition(PlayerLightController light, Rect bounds)
    {
        if (light == null || bounds.width <= 0f || bounds.height <= 0f)
            return new Vector2(0.5f, 0.5f);

        Vector3 wp = light.transform.position;
        float nx = Mathf.InverseLerp(bounds.xMin, bounds.xMax, wp.x);
        float ny = Mathf.InverseLerp(bounds.yMin, bounds.yMax, wp.y);
        return new Vector2(nx, ny);
    }

    // ── Merge progress ────────────────────────────────────────────────────────

    private void UpdateMergeProgress(Rect bounds)
    {
        if (_p1Light == null || _p2Light == null)
        {
            _mergeProgressTarget = 0f;
        }
        else
        {
            Vector2 p1 = new Vector2(_p1Light.transform.position.x, _p1Light.transform.position.y);
            Vector2 p2 = new Vector2(_p2Light.transform.position.x, _p2Light.transform.position.y);
            float dist = Vector2.Distance(p1, p2);

            // Map distance → merge target: 0 at/beyond approachThreshold, 1 at/below mergeFullThreshold.
            _mergeProgressTarget = 1f - Mathf.Clamp01(
                (dist - _mergeFullThreshold) / Mathf.Max(0.001f, _approachThreshold - _mergeFullThreshold));
        }

        MergeProgress = Mathf.MoveTowards(MergeProgress, _mergeProgressTarget,
                                          _mergeSpeed * Time.deltaTime);
    }

    // ── Bounds helpers ────────────────────────────────────────────────────────

    private Rect GetBounds()
    {
        if (_mazeGenerator != null && _mazeGenerator.WorldBounds.width > 0f)
            return _mazeGenerator.WorldBounds;
        return _fallbackBounds;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Override base centres at runtime (e.g. if the screen aspect changes).
    /// </summary>
    public void SetBaseCenters(Vector2 p1Center, Vector2 p2Center)
    {
        _p1BaseCenter = p1Center;
        _p2BaseCenter = p2Center;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw screen-space circles in Game view is not possible via Gizmos,
        // but we can draw the maze bounds and pan range in Scene view.
        Rect b = (_mazeGenerator != null && _mazeGenerator.WorldBounds.width > 0f)
            ? _mazeGenerator.WorldBounds : _fallbackBounds;

        if (b.width <= 0f) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireCube(
            new Vector3(b.center.x, b.center.y, 0f),
            new Vector3(b.width, b.height, 0.1f));
    }
#endif
}
