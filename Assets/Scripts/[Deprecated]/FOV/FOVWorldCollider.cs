using UnityEngine;

/// <summary>
/// Translates hand-tracking viewport data into world-space spheres that match
/// the FOV shader circles exactly.
///
/// Attach this to the same GameObject as TEIHandTrackingShaderBridge.
/// Other scripts (e.g. FOVHighlightable) read from FOVWorldCollider.Instance.
///
/// GHOST WORLD POSITION:
///   When a hand goes absent, its last known world position is stored and frozen.
///   The ghost's WorldPosition never changes until the hand returns — it is truly
///   stuck in world space, independent of any camera movement.
///
///   ViewportPosition for a ghost is set to the centre of that player's viewport
///   bounds (full-screen UV). PlayerCameraController returns (0.5, 0.5) when
///   IsActive == false, but ghosts have IsActive = true so they continue to
///   participate in split/merge distance checks. The centre-of-bounds viewport
///   position produces zero pan delta, keeping the camera rig still.
/// </summary>
public class FOVWorldCollider : MonoBehaviour
{
    public static FOVWorldCollider Instance { get; private set; }

    [Header("Sources (same refs as ShaderBridge)")]
    [SerializeField] private TEIHandTrackingFilter filter;
    [SerializeField] private TEIHandTrackingShaderBridge bridge;

    [Header("Coordinate Settings")]
    [Tooltip("Must match the Flip Y setting on TEIHandTrackingShaderBridge.")]
    [SerializeField] private bool flipY = true;

    [Tooltip("World-space Z of the gameplay plane. Match your 3D object Z positions.")]
    [SerializeField] private float gamePlaneZ = 0f;

    // ── Public hand state ─────────────────────────────────────────────────────

    public struct HandWorldState
    {
        public bool        IsActive;
        /// <summary>
        /// True when the hand is not physically tracked — position is frozen at last
        /// known world position. Ghost hands contribute to merge distance checks but
        /// should NOT trigger gameplay (fish responses, FOV highlights, etc.).
        /// </summary>
        public bool        IsGhost;
        public PlayerIndex Owner;
        public Vector3     WorldPosition; // world-space center (for gizmos / 3D use)
        public float       WorldRadius;   // world-space radius at gamePlaneZ

        // Viewport-space data — use these for overlap tests (Z-independent, matches shader exactly)
        public Vector2     ViewportPosition; // 0-1, same coords the shader receives
        public float       ScreenRadius;     // 0-1 fraction of screen width, same as _Hand1Radius shader param
    }

    public HandWorldState LeftHand  { get; private set; }
    public HandWorldState RightHand { get; private set; }

    // ── Frozen ghost world positions ──────────────────────────────────────────

    private Vector3 _lastLeftWorld;
    private Vector3 _lastRightWorld;
    private bool    _hasLeftWorld;
    private bool    _hasRightWorld;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (filter == null)
            filter = FindAnyObjectByType<TEIHandTrackingFilter>();
        if (bridge == null)
            bridge = GetComponent<TEIHandTrackingShaderBridge>();
    }

    private void Update()
    {
        // Build states for hands that are currently physically tracked.
        HandWorldState leftReal  = filter.HasLeftHand
            ? BuildRealState(PlayerIndex.Player1, filter.LeftHand,  bridge.CurrentLeftRadius)
            : default;
        HandWorldState rightReal = filter.HasRightHand
            ? BuildRealState(PlayerIndex.Player2, filter.RightHand, bridge.CurrentRightRadius)
            : default;

        // Update frozen world positions while hands are actively tracked.
        if (leftReal.IsActive)  { _lastLeftWorld  = leftReal.WorldPosition;  _hasLeftWorld  = true; }
        if (rightReal.IsActive) { _lastRightWorld = rightReal.WorldPosition; _hasRightWorld = true; }

        // Publish: real hand if present, frozen ghost if we have a last known position,
        // or inactive (no split/merge participation) if the hand has never been seen.
        LeftHand = leftReal.IsActive ? leftReal
            : (_hasLeftWorld
                ? BuildGhostState(PlayerIndex.Player1, _lastLeftWorld,  bridge.CurrentLeftRadius)
                : new HandWorldState { IsActive = false });

        RightHand = rightReal.IsActive ? rightReal
            : (_hasRightWorld
                ? BuildGhostState(PlayerIndex.Player2, _lastRightWorld, bridge.CurrentRightRadius)
                : new HandWorldState { IsActive = false });
    }

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a live state from real hand-tracking data.
    /// Applies viewport boundary enforcement in split mode (hands outside their
    /// half are marked inactive so they don't interfere with the other player).
    /// </summary>
    private HandWorldState BuildRealState(PlayerIndex player, Vector2 viewportPos, float screenRadius)
    {
        Camera cam = SplitScreenController.Instance != null
            ? SplitScreenController.Instance.GetCameraForPlayer(player)
            : Camera.main;

        if (cam == null)
            return new HandWorldState { IsActive = false };

        Vector2 vp = viewportPos;
        if (flipY) vp.y = 1f - vp.y;

        // In split mode, reject hands that have strayed outside their assigned half.
        if (SplitScreenController.Instance != null &&
            SplitScreenController.Instance.CurrentState == SplitScreenController.SplitState.SplitScreen)
        {
            Rect validBounds = SplitScreenController.Instance.GetPlayerViewportBounds(player);
            if (!validBounds.Contains(vp))
                return new HandWorldState { IsActive = false };
        }

        Vector3 worldPos = ViewportToGamePlane(vp, cam);
        float   worldRad = ScreenRadiusToWorldUnits(screenRadius, worldPos.z, cam);

        return new HandWorldState
        {
            IsActive         = true,
            IsGhost          = false,
            Owner            = player,
            WorldPosition    = worldPos,
            WorldRadius      = worldRad,
            ViewportPosition = vp,
            ScreenRadius     = screenRadius,
        };
    }

    /// <summary>
    /// Builds a ghost state using a frozen world position.
    ///
    /// WorldPosition — the stored last-known world position. Never changes until the
    ///   hand returns. Truly stuck in world space regardless of any camera movement.
    ///
    /// ViewportPosition:
    ///   Split mode   → centre of this player's assigned half (0.25 or 0.75, y=0.5).
    ///                  PlayerCameraController reads this for pan; centre = zero pan,
    ///                  so the absent player's camera rig holds perfectly still.
    ///   Together mode → actual viewport projection of the frozen world position
    ///                  through P1's camera. P2's PlayerCameraController returns early
    ///                  in together mode (shadows P1) and never reads ViewportPosition,
    ///                  so this has no effect on camera movement. It DOES keep the
    ///                  viewport position consistent with the visual circle position,
    ///                  so overlap / interaction systems register collisions correctly
    ///                  rather than at the phantom (0.5, 0.5) screen centre.
    /// </summary>
    private HandWorldState BuildGhostState(PlayerIndex player, Vector3 frozenWorldPos, float screenRadius)
    {
        bool inSplit = SplitScreenController.Instance != null &&
                       SplitScreenController.Instance.CurrentState == SplitScreenController.SplitState.SplitScreen;

        Vector2 ghostVP;

        if (inSplit)
        {
            // Centre of the player's assigned half → zero pan delta for the camera rig.
            Rect bounds = SplitScreenController.Instance.GetPlayerViewportBounds(player);
            ghostVP = new Vector2(
                bounds.x + bounds.width  * 0.5f,
                bounds.y + bounds.height * 0.5f);
        }
        else
        {
            // Together mode: project the frozen world position through P1's camera.
            // Camera.WorldToViewportPoint returns Y-from-bottom (camera viewport space),
            // which is the same convention BuildRealState uses after applying flipY.
            // No additional flip is needed here.
            Camera p1Cam = SplitScreenController.Instance != null
                ? SplitScreenController.Instance.GetCameraForPlayer(PlayerIndex.Player1)
                : Camera.main;

            if (p1Cam != null)
            {
                Vector3 vp = p1Cam.WorldToViewportPoint(frozenWorldPos);
                ghostVP = new Vector2(vp.x, vp.y);
            }
            else
            {
                ghostVP = new Vector2(0.5f, 0.5f);
            }
        }

        Camera cam = SplitScreenController.Instance != null
            ? SplitScreenController.Instance.GetCameraForPlayer(player)
            : Camera.main;

        float worldRad = cam != null
            ? ScreenRadiusToWorldUnits(screenRadius, frozenWorldPos.z, cam)
            : screenRadius;

        return new HandWorldState
        {
            IsActive         = true,
            IsGhost          = true,
            Owner            = player,
            WorldPosition    = frozenWorldPos,
            WorldRadius      = worldRad,
            ViewportPosition = ghostVP,
            ScreenRadius     = screenRadius,
        };
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private Vector3 ViewportToGamePlane(Vector2 vp, Camera cam)
    {
        if (cam.orthographic)
        {
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            float x = Mathf.Lerp(-w, w, vp.x) + cam.transform.position.x;
            float y = Mathf.Lerp(-h, h, vp.y) + cam.transform.position.y;
            return new Vector3(x, y, gamePlaneZ);
        }
        else
        {
            float dist = Mathf.Abs(cam.transform.position.z - gamePlaneZ);
            return cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, dist));
        }
    }

    private float ScreenRadiusToWorldUnits(float screenRadius, float worldZ, Camera cam)
    {
        if (cam.orthographic)
        {
            float worldWidth = cam.orthographicSize * 2f * cam.aspect;
            return screenRadius * worldWidth;
        }
        else
        {
            float dist       = Mathf.Abs(cam.transform.position.z - worldZ);
            float halfHeight = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist;
            float worldWidth = halfHeight * cam.aspect * 2f;
            return screenRadius * worldWidth;
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        DrawHandGizmo(LeftHand,  new Color(0.2f, 0.8f, 1f, 0.8f));
        DrawHandGizmo(RightHand, new Color(1f, 0.5f, 0.1f, 0.8f));
    }

    private void DrawHandGizmo(HandWorldState state, Color color)
    {
        if (!state.IsActive) return;

        // Ghost hands draw at reduced alpha so they're visually distinct.
        Color c = state.IsGhost ? new Color(color.r, color.g, color.b, color.a * 0.4f) : color;

        UnityEditor.Handles.color = c;
        UnityEditor.Handles.DrawWireDisc(state.WorldPosition, Vector3.forward, state.WorldRadius);

        float cross = state.WorldRadius * 0.1f;
        UnityEditor.Handles.DrawLine(
            state.WorldPosition + Vector3.left  * cross,
            state.WorldPosition + Vector3.right * cross);
        UnityEditor.Handles.DrawLine(
            state.WorldPosition + Vector3.down * cross,
            state.WorldPosition + Vector3.up   * cross);
    }
#endif
}
