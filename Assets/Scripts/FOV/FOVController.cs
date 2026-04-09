using UnityEngine;

/// <summary>
/// Per-player FOV controller. Reads hand data from HandTrackingService,
/// drives the existing TEIHandTrackingShaderBridge for visuals, and computes
/// a world-space FOVState for gameplay systems (FishCharacter, puzzles, etc.).
///
/// Implements IFOVProvider — pass this to any system that needs FOV data.
///
/// One FOVController exists per player inside the Player prefab.
/// The TEIHandTrackingShaderBridge lives on the HandTrackingCanvas (shared between
/// both players). FOVController finds it automatically at startup — no manual wiring needed.
/// </summary>
public class FOVController : MonoBehaviour, IFOVProvider
{
    [Header("Identity")]
    [SerializeField] private PlayerIndex playerIndex;

    [Header("References")]
    [SerializeField] private HandTrackingService handService;

    [Header("Coordinate Settings")]
    [Tooltip("Z depth of the gameplay plane in world space. Camera looks down -Z (front view).")]
    [SerializeField] private float gamePlaneZ = 0f;

    [Tooltip("Flip the Y axis when projecting viewport → world. " +
             "Enable if characters appear mirrored vertically from the FOV.")]
    [SerializeField] private bool flipY = true;

    // Cached shader bridge — driven by existing TEI scripts, we only read radii from it.
    private TEIHandTrackingShaderBridge _bridge;

    private FOVState _state;

    // ── Runtime initialization (called by PlayerManager) ──────────────────────

    /// <summary>
    /// Wires the controller to a specific player index and hand service at runtime.
    /// Called by PlayerManager after the Player prefab is instantiated.
    /// </summary>
    public void Initialize(PlayerIndex index, HandTrackingService service)
    {
        playerIndex = index;
        handService = service;
    }

    // ── IFOVProvider ──────────────────────────────────────────────────────────

    public FOVState GetFOVState() => _state;

    /// <summary>Which player this controller belongs to.</summary>
    public PlayerIndex PlayerIndex => playerIndex;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // The bridge lives on the HandTrackingCanvas, not on this prefab.
        // FindObjectOfType is fine here — called once at startup.
        _bridge = FindAnyObjectByType<TEIHandTrackingShaderBridge>();
        if (_bridge == null)
            Debug.LogWarning("[FOVController] No TEIHandTrackingShaderBridge found in scene. World radius will default to 0.", this);
    }

    private void Update()
    {
        if (handService == null) return;

        HandData hand = handService.GetHandData(playerIndex);

        if (!hand.IsPresent)
        {
            _state = new FOVState { IsActive = false };
            return;
        }

        // Viewport position with optional Y flip (MediaPipe Y is inverted vs Unity viewport).
        Vector2 vp = hand.ViewportPosition;
        if (flipY) vp.y = 1f - vp.y;

        // Screen radius: bridge owns the depth-responsive radius computation.
        // Player1 → left shader hand, Player2 → right shader hand.
        float screenRadius = 0.13f; // fallback if bridge not found
        if (_bridge != null)
            screenRadius = playerIndex == PlayerIndex.Player1
                ? _bridge.CurrentLeftRadius
                : _bridge.CurrentRightRadius;

        // Project viewport position to world space on the game plane.
        Vector3 worldPos = ViewportToGamePlane(vp);

        // Convert screen radius (fraction of screen width) to world units.
        float worldRadius = ScreenRadiusToWorldUnits(screenRadius, worldPos.z);

        _state = new FOVState
        {
            IsActive         = true,
            ViewportPosition = vp,
            WorldPosition    = worldPos,
            ScreenRadius     = screenRadius,
            WorldRadius      = worldRadius,
        };
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the correct camera for this player.
    /// In split mode P2 must use its own camera — Camera.main is always P1.
    /// </summary>
    private Camera GetCamera()
    {
        if (SplitScreenController.Instance != null)
            return SplitScreenController.Instance.GetCameraForPlayer(playerIndex);
        return Camera.main;
    }

    /// <summary>
    /// Projects a 0-1 viewport position to a world-space point on the game plane (gamePlaneZ).
    /// Works for both perspective and orthographic cameras.
    /// </summary>
    private Vector3 ViewportToGamePlane(Vector2 viewport)
    {
        Camera cam = GetCamera();
        if (cam == null) return Vector3.zero;

        if (cam.orthographic)
        {
            // Orthographic: viewport maps linearly to world extents.
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            float x = Mathf.Lerp(-w, w, viewport.x) + cam.transform.position.x;
            float y = Mathf.Lerp(-h, h, viewport.y) + cam.transform.position.y;
            return new Vector3(x, y, gamePlaneZ);
        }
        else
        {
            // Perspective: cast a ray through the viewport point to the game plane.
            float distToPlane = Mathf.Abs(cam.transform.position.z - gamePlaneZ);
            Vector3 viewportPoint = new Vector3(viewport.x, viewport.y, distToPlane);
            return cam.ViewportToWorldPoint(viewportPoint);
        }
    }

    /// <summary>
    /// Converts a screen-space radius (0-1 fraction of screen width) to world units
    /// at the given world Z depth. Works for perspective cameras.
    /// For orthographic cameras the conversion is linear (no depth factor).
    /// </summary>
    private float ScreenRadiusToWorldUnits(float screenRadius, float worldZ)
    {
        Camera cam = GetCamera();
        if (cam == null) return screenRadius;

        if (cam.orthographic)
        {
            // In orthographic, 1 viewport unit = 2 * orthographicSize world units in height.
            // Width = height * aspect. We express radius as fraction of width.
            float worldWidth = cam.orthographicSize * 2f * cam.aspect;
            return screenRadius * worldWidth;
        }
        else
        {
            float distToPlane = Mathf.Abs(cam.transform.position.z - worldZ);
            float halfFovTan  = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfHeight  = halfFovTan * distToPlane;
            float worldWidth  = halfHeight * cam.aspect * 2f;
            return screenRadius * worldWidth;
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!_state.IsActive) return;
        Gizmos.color = playerIndex == PlayerIndex.Player1
            ? new Color(0.2f, 0.8f, 1f, 0.4f)
            : new Color(1f, 0.5f, 0.1f, 0.4f);
        Gizmos.DrawWireSphere(_state.WorldPosition, _state.WorldRadius);
    }
}
