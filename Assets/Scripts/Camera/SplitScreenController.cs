using UnityEngine;

/// <summary>
/// Manages the single ↔ split-screen camera transition.
///
/// SPLIT TRIGGER — FOV world-space distance:
///   Both players' FOV world positions are computed via FOVWorldCollider.
///   In together mode, GetCameraForPlayer(P2) returns P1's camera, so both
///   world positions are in P1's coordinate space (= what the player sees on
///   screen). When those positions are far enough apart, split fires.
///   This is purely hand-driven — camera positions are irrelevant.
///
/// COORDINATE CONSISTENCY:
///   Together (splitProgress == 0): both players use P1's camera.
///     Screen IS P1's camera. All viewport↔world math matches the display.
///   Transitioning / split (splitProgress > 0): each player uses own camera.
///     Each half of the screen is rendered by that player's rig.
///
/// P2 RIG IN TOGETHER MODE:
///   P2's rig shadows P1's rig (PlayerCameraController handles this).
///   When split fires, P2's rig starts at P1's position and pans toward P2's
///   FOV — a smooth visual split from one shared view into two independent ones.
///
/// GHOST HANDS (absent player):
///   FOVWorldCollider stores the last known WORLD position when a hand goes absent.
///   The ghost HandWorldState has IsActive=true, IsGhost=true, and WorldPosition
///   frozen at that last known world coordinate — it never moves regardless of
///   camera movement. ViewportPosition is set to the centre of the player's
///   viewport bounds so PlayerCameraController produces zero pan (camera stays still).
///   Merge is always evaluated against ghost distance (active player can walk back).
///   Split fires when at least one hand is real — a real player can split away from
///   a ghost's frozen world position to re-enter split mode.
/// </summary>
public class SplitScreenController : MonoBehaviour
{
    public static SplitScreenController Instance { get; private set; }

    [Header("Cameras")]
    [SerializeField] private Camera p1Camera;
    [SerializeField] private Camera p2Camera;

    [Header("Split Thresholds")]
    [Tooltip("World-space distance between the two FOV positions at which split activates.")]
    [SerializeField] private float splitThreshold = 8f;
    [Tooltip("World-space distance at which the view merges back. Must be less than splitThreshold.")]
    [SerializeField] private float mergeThreshold = 5f;

    [Header("Transition")]
    [SerializeField] private float transitionDuration = 0.5f;


    // ── State ─────────────────────────────────────────────────────────────────

    public enum SplitState { Together, SplitScreen }

    public SplitState CurrentState { get; private set; } = SplitState.Together;

    /// <summary>0 = fully together, 1 = fully split. Drives the shader.</summary>
    public float SplitProgress => splitProgress;

    /// <summary>True when P1's rig is on the left side of the split.</summary>
    public bool P1IsOnLeft { get; private set; } = true;

    private float splitProgress;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (p1Camera != null) p1Camera.enabled = true;
        if (p2Camera != null) p2Camera.enabled = true;
    }

    private void Update()
    {
        if (p1Camera == null || p2Camera == null) return;
        if (FOVWorldCollider.Instance == null) return;

        var p1Hand = FOVWorldCollider.Instance.LeftHand;
        var p2Hand = FOVWorldCollider.Instance.RightHand;

        // Only evaluate when both hands are tracked.
        // FOV world positions are computed using GetCameraForPlayer() —
        // in together mode (splitProgress==0) that returns P1's camera for both,
        // so the distance is in a consistent world space that matches the display.
        // Both hands always provide valid world positions (ghost hands use camera centre).
        // FOVWorldCollider guarantees IsActive == true for all states including absent hands.
        if (p1Hand.IsActive && p2Hand.IsActive)
        {
            float dist = Vector2.Distance(
                new Vector2(p1Hand.WorldPosition.x, p1Hand.WorldPosition.y),
                new Vector2(p2Hand.WorldPosition.x, p2Hand.WorldPosition.y));

            // Merge: always evaluated — ghost world position (camera centre) participates.
            // Active player moving toward the absent player's camera area triggers merge.
            if (CurrentState == SplitState.SplitScreen && dist <= mergeThreshold)
            {
                CurrentState = SplitState.Together;
            }

            // Split: requires at least one real hand.
            // A real player can split away from an absent player's frozen world position,
            // which is what enables re-splitting after a merge with a ghost.
            // Two frozen ghosts cannot split on their own (both IsGhost → blocked).
            if (CurrentState == SplitState.Together && dist >= splitThreshold
                && !(p1Hand.IsGhost && p2Hand.IsGhost))
            {
                P1IsOnLeft   = p1Hand.WorldPosition.x <= p2Hand.WorldPosition.x;
                CurrentState = SplitState.SplitScreen;
            }
        }

        float targetProgress = CurrentState == SplitState.SplitScreen ? 1f : 0f;
        float step = transitionDuration > 0f ? Time.deltaTime / transitionDuration : 1f;
        splitProgress = Mathf.MoveTowards(splitProgress, targetProgress, step);
    }

    // ── Debug API ─────────────────────────────────────────────────────────────

    /// <summary>Force the view back to together mode instantly. Use for debug resets.</summary>
    public void ForceTogether()
    {
        CurrentState  = SplitState.Together;
        splitProgress = 0f;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the camera whose viewport space matches what is on screen for this player.
    ///
    /// Together (splitProgress == 0): screen = P1's camera full-screen.
    ///   Both players use P1's camera so all viewport↔world math is consistent
    ///   with what the player actually sees.
    ///
    /// Transitioning or split (splitProgress > 0): each half is rendered by
    ///   the player's own rig, so each player uses their own camera.
    /// </summary>
    public Camera GetCameraForPlayer(PlayerIndex player)
    {
        if (player == PlayerIndex.Player2 && p2Camera != null && splitProgress > 0f)
            return p2Camera;
        return p1Camera != null ? p1Camera : Camera.main;
    }

    /// <summary>
    /// Returns this player's valid viewport rect.
    /// Together → full screen. Split → assigned left or right half.
    /// </summary>
    public Rect GetPlayerViewportBounds(PlayerIndex player)
    {
        if (CurrentState != SplitState.SplitScreen)
            return new Rect(0f, 0f, 1f, 1f);

        bool onLeft = (player == PlayerIndex.Player1) ? P1IsOnLeft : !P1IsOnLeft;
        return onLeft
            ? new Rect(0f,   0f, 0.5f, 1f)
            : new Rect(0.5f, 0f, 0.5f, 1f);
    }
}
