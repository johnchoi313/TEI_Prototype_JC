using UnityEngine;
using UnityEngine.UI;

public class TEIHandTrackingShaderBridge : MonoBehaviour
{
    public static TEIHandTrackingShaderBridge Instance { get; private set; }
    [Header("Input Source")]
    [SerializeField] private TEIHandTrackingFilter filter;

    [Header("Gesture Source")]
    [SerializeField] private TEIHandGestureInterpreter gestures;

    [Header("Target")]
    [SerializeField] private Graphic targetGraphic;

    [Header("Coordinate Fixes")]
    [SerializeField] private bool flipX = false;
    [SerializeField] private bool flipY = true;

    [Header("Radius Balance")]
    [SerializeField] private float minRadius = 0.05f;
    [SerializeField] private float maxRadius = 0.12f;
    [SerializeField] private float radiusSmoothing = 8f;

    [Header("Depth Comparison Tuning")]
    [SerializeField] private float depthPower = 1f;

    [Header("Depth Blur")]
    [SerializeField, Range(0f, 1f)] private float maxBlurAmount = 1.0f;
    [SerializeField, Range(0.5f, 4f)] private float blurCurveExponent = 2f;
    [Tooltip("Amplifies the depth difference so blur reaches max with a modest hand separation. " +
             "At 2, a balance deviation of 0.25 (one hand noticeably closer) = full blur. " +
             "Increase if blur never saturates; decrease if it feels too hair-trigger.")]
    [SerializeField, Range(1f, 6f)] private float blurContrastBoost = 2f;

    [Header("Ghost / Default State")]
    [Tooltip("Opacity of the FOV circle when the hand is not detected. 0 = invisible, 1 = fully visible.")]
    [SerializeField, Range(0f, 1f)] private float ghostOpacity = 0.35f;
    [Tooltip("How fast the FOV circle drifts back to its default home position when the hand is lost.")]
    [SerializeField] private float positionReturnSpeed = 3f;
    [Tooltip("How fast the circle fades between active (1.0) and ghost opacity when a hand appears or disappears.")]
    [SerializeField] private float activeTransitionSpeed = 5f;

    [Header("Fist Pulse")]
    [SerializeField] private float pulseDecaySpeed = 3f;
    [SerializeField] private float pulseTriggerValue = 1f;

    // ── Private state ─────────────────────────────────────────────────────────

    private Material runtimeMaterial;

    // Radii
    private float currentLeftRadius;
    private float currentRightRadius;

    // Smoothed positions — owned by the bridge so we can drive them toward
    // a home position when the hand is absent, rather than snapping off-screen.
    private Vector2 currentLeftPos;
    private Vector2 currentRightPos;

    // Smooth active/opacity values (0–1). Replaces the old binary 0/1 flag
    // so the ghost circle fades in/out rather than popping.
    private float currentLeftActive;
    private float currentRightActive;

    // Remember which horizontal side each hand was last occupying so the circle
    // returns to the center of that side (0.25 = left half, 0.75 = right half).
    private float leftHandLastSideX  = 0.25f;
    private float rightHandLastSideX = 0.75f;

    private float leftPulse;
    private float rightPulse;

    // ── Shader property IDs ───────────────────────────────────────────────────

    private static readonly int Hand1PosID    = Shader.PropertyToID("_Hand1Pos");
    private static readonly int Hand2PosID    = Shader.PropertyToID("_Hand2Pos");
    private static readonly int Hand1ActiveID = Shader.PropertyToID("_Hand1Active");
    private static readonly int Hand2ActiveID = Shader.PropertyToID("_Hand2Active");
    private static readonly int Hand1RadiusID = Shader.PropertyToID("_Hand1Radius");
    private static readonly int Hand2RadiusID = Shader.PropertyToID("_Hand2Radius");

    private static readonly int Hand1FistHeldID = Shader.PropertyToID("_Hand1FistHeld");
    private static readonly int Hand2FistHeldID = Shader.PropertyToID("_Hand2FistHeld");
    private static readonly int Hand1PulseID    = Shader.PropertyToID("_Hand1FistPulse");
    private static readonly int Hand2PulseID    = Shader.PropertyToID("_Hand2FistPulse");

    private static readonly int Hand1BlurID = Shader.PropertyToID("_Hand1BlurAmount");
    private static readonly int Hand2BlurID = Shader.PropertyToID("_Hand2BlurAmount");

    private static readonly int Fist1ColorID    = Shader.PropertyToID("_Fist1Color");
    private static readonly int Fist2ColorID    = Shader.PropertyToID("_Fist2Color");
    private static readonly int MergeProgressID = Shader.PropertyToID("_MergeProgress");

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (targetGraphic == null)
        {
            Debug.LogError("[TEIHandTrackingShaderBridge] No target Graphic assigned.", this);
            enabled = false;
            return;
        }

        if (targetGraphic.material == null)
        {
            Debug.LogError("[TEIHandTrackingShaderBridge] Target Graphic has no material assigned.", this);
            enabled = false;
            return;
        }

        runtimeMaterial = new Material(targetGraphic.material);
        targetGraphic.material = runtimeMaterial;

        // Radius initialisation
        float midRadius = (minRadius + maxRadius) * 0.5f;
        currentLeftRadius  = midRadius;
        currentRightRadius = midRadius;
        runtimeMaterial.SetFloat(Hand1RadiusID, currentLeftRadius);
        runtimeMaterial.SetFloat(Hand2RadiusID, currentRightRadius);

        // Position initialisation — start at home positions, circles hidden
        currentLeftPos    = ComputeHomePosition(leftHandLastSideX,  isLeft: true);
        currentRightPos   = ComputeHomePosition(rightHandLastSideX, isLeft: false);
        currentLeftActive  = 0f;
        currentRightActive = 0f;
        runtimeMaterial.SetVector(Hand1PosID, currentLeftPos);
        runtimeMaterial.SetVector(Hand2PosID, currentRightPos);
        runtimeMaterial.SetFloat(Hand1ActiveID, 0f);
        runtimeMaterial.SetFloat(Hand2ActiveID, 0f);

        // Blur + gesture initialisation
        runtimeMaterial.SetFloat(Hand1BlurID, 0f);
        runtimeMaterial.SetFloat(Hand2BlurID, 0f);
        runtimeMaterial.SetFloat(Hand1FistHeldID, 0f);
        runtimeMaterial.SetFloat(Hand2FistHeldID, 0f);
        runtimeMaterial.SetFloat(Hand1PulseID, 0f);
        runtimeMaterial.SetFloat(Hand2PulseID, 0f);
    }

    private void Update()
    {
        if (filter == null || runtimeMaterial == null)
            return;

        UpdateHand(isLeft: true,  active: filter.HasLeftHand,  filterPosition: filter.LeftHand);
        UpdateHand(isLeft: false, active: filter.HasRightHand, filterPosition: filter.RightHand);

        UpdateRadii();
        UpdateGestureVisuals();
    }

    // ── Hand position + active/ghost ──────────────────────────────────────────

    /// <summary>
    /// Updates one hand's shader position and active/opacity value.
    /// When the hand is present, the circle follows the filter position and fades to full opacity.
    /// When the hand is absent, the circle drifts to the center of its last-occupied screen half
    /// and fades to ghostOpacity — acting as a "put your hand here" affordance.
    /// </summary>
    private void UpdateHand(bool isLeft, bool active, Vector2 filterPosition)
    {
        ref Vector2 currentPos    = ref (isLeft ? ref currentLeftPos    : ref currentRightPos);
        ref float   currentActive = ref (isLeft ? ref currentLeftActive : ref currentRightActive);
        ref float   lastSideX     = ref (isLeft ? ref leftHandLastSideX : ref rightHandLastSideX);

        int posID    = isLeft ? Hand1PosID    : Hand2PosID;
        int activeID = isLeft ? Hand1ActiveID : Hand2ActiveID;

        Vector2 targetPos;
        float   targetActive;

        if (active)
        {
            // Hand is tracked — record which side it's on and follow it.
            lastSideX    = filterPosition.x < 0.5f ? 0.25f : 0.75f;
            targetPos    = filterPosition;
            targetActive = 1f;
        }
        else
        {
            // Hand lost — drift to home position (world-projected in together mode,
            // centre-of-half in split mode).
            targetPos    = ComputeHomePosition(lastSideX, isLeft);
            targetActive = ghostOpacity;
        }

        currentPos    = Vector2.Lerp(currentPos,    targetPos,    positionReturnSpeed    * Time.deltaTime);
        currentActive = Mathf.Lerp(currentActive, targetActive, activeTransitionSpeed * Time.deltaTime);

        // Apply coordinate flips before sending to shader
        Vector2 uv = currentPos;
        if (flipX) uv.x = 1f - uv.x;
        if (flipY) uv.y = 1f - uv.y;

        runtimeMaterial.SetVector(posID,    uv);
        runtimeMaterial.SetFloat (activeID, currentActive);
    }

    /// <summary>
    /// Returns the target position (filter/pre-flip space) for a hand that is absent.
    ///
    /// Split mode:
    ///   Returns the centre of the player's assigned screen half (blended with
    ///   SplitProgress). This keeps the camera rig still — no pan delta.
    ///
    /// Together mode:
    ///   Projects the hand's frozen world position through P1's camera so the ghost
    ///   circle stays at the hand's actual world location on the shared screen,
    ///   rather than drifting to the arbitrary screen centre.
    ///   As P1's camera pans, the ghost circle naturally tracks with the world.
    /// </summary>
    private Vector2 ComputeHomePosition(float lastSideX, bool isLeft)
    {
        bool inSplit = SplitScreenController.Instance != null &&
                       SplitScreenController.Instance.CurrentState == SplitScreenController.SplitState.SplitScreen;

        if (inSplit)
        {
            // Home to the centre of the player's ASSIGNED viewport half.
            // We ask SplitScreenController for the actual bounds rather than
            // hardcoding 0.25/0.75, because P1IsOnLeft can be false (P1's half
            // is the right side when players are physically on opposite sides).
            // Hardcoding caused both ghosts to go to the wrong half every time.
            // This mirrors the same pattern used in FOVWorldCollider.BuildGhostState().
            PlayerIndex player      = isLeft ? PlayerIndex.Player1 : PlayerIndex.Player2;
            Rect        bounds      = SplitScreenController.Instance.GetPlayerViewportBounds(player);
            float       assignedHomeX = bounds.x + bounds.width * 0.5f;
            float       splitT      = SplitScreenController.Instance.SplitProgress;
            float       homeX       = Mathf.Lerp(0.5f, assignedHomeX, splitT);
            return new Vector2(homeX, 0.5f);
        }

        // Together mode: project the frozen world position through P1's camera.
        // FOVWorldCollider stores a truly frozen world position for absent hands.
        if (FOVWorldCollider.Instance != null)
        {
            FOVWorldCollider.HandWorldState hand = isLeft
                ? FOVWorldCollider.Instance.LeftHand
                : FOVWorldCollider.Instance.RightHand;

            if (hand.IsGhost)
            {
                Camera cam = SplitScreenController.Instance != null
                    ? SplitScreenController.Instance.GetCameraForPlayer(PlayerIndex.Player1)
                    : Camera.main;

                if (cam != null)
                {
                    // WorldToViewportPoint → camera-viewport space (Y = 0 bottom, 1 top).
                    // The bridge stores currentPos in filter space (Y from top when flipY=true).
                    // Undo the flip so the bridge will re-apply it correctly when sending to shader.
                    Vector3 vp      = cam.WorldToViewportPoint(hand.WorldPosition);
                    float   filterY = flipY ? 1f - vp.y : vp.y;
                    return new Vector2(vp.x, filterY);
                }
            }
        }

        // Fallback: hand has never been tracked yet — sit at screen centre.
        return new Vector2(0.5f, 0.5f);
    }

    // ── Radii + Blur ──────────────────────────────────────────────────────────

    /// <summary>
    /// Computes depth balance from both hand scales and drives radius + blur for both circles.
    /// Intentionally runs even when one hand is absent — TEIHandTrackingFilter holds the last
    /// known scale when a hand is lost, so the ghost circle's radius mirrors the active hand
    /// proportionally rather than snapping to a fixed mid-size.
    /// </summary>
    private void UpdateRadii()
    {
        float leftScale  = Mathf.Max(0.0001f, filter.LeftHandScale);
        float rightScale = Mathf.Max(0.0001f, filter.RightHandScale);

        if (!Mathf.Approximately(depthPower, 1f))
        {
            leftScale  = Mathf.Pow(leftScale,  depthPower);
            rightScale = Mathf.Pow(rightScale, depthPower);
        }

        // balance: 0 = right hand clearly closer, 1 = left hand clearly closer, 0.5 = equal.
        // When neither hand has data both scales = 0.0001 → balance = 0.5 → equal radii.
        float balance = leftScale / (leftScale + rightScale);

        float targetLeftRadius  = Mathf.Lerp(maxRadius, minRadius, balance);
        float targetRightRadius = Mathf.Lerp(minRadius, maxRadius, balance);

        currentLeftRadius  = Mathf.Lerp(currentLeftRadius,  targetLeftRadius,  radiusSmoothing * Time.deltaTime);
        currentRightRadius = Mathf.Lerp(currentRightRadius, targetRightRadius, radiusSmoothing * Time.deltaTime);

        runtimeMaterial.SetFloat(Hand1RadiusID, currentLeftRadius);
        runtimeMaterial.SetFloat(Hand2RadiusID, currentRightRadius);

        // Blur — same balance signal. Remapped so a moderate depth difference saturates to full blur.
        float leftBlurRaw  = Mathf.Clamp01((0.5f - balance) * blurContrastBoost * 2f);
        float rightBlurRaw = Mathf.Clamp01((balance - 0.5f) * blurContrastBoost * 2f);
        runtimeMaterial.SetFloat(Hand1BlurID, Mathf.Pow(leftBlurRaw,  blurCurveExponent) * maxBlurAmount);
        runtimeMaterial.SetFloat(Hand2BlurID, Mathf.Pow(rightBlurRaw, blurCurveExponent) * maxBlurAmount);
    }

    // ── Gesture visuals ───────────────────────────────────────────────────────

    private void UpdateGestureVisuals()
    {
        if (gestures == null)
        {
            runtimeMaterial.SetFloat(Hand1FistHeldID, 0f);
            runtimeMaterial.SetFloat(Hand2FistHeldID, 0f);
            runtimeMaterial.SetFloat(Hand1PulseID, 0f);
            runtimeMaterial.SetFloat(Hand2PulseID, 0f);
            return;
        }

        // float leftHeld = gestures.IsLeftFist ? 1f : 0f;
        // float rightHeld = gestures.IsRightFist ? 1f : 0f;

        // if (gestures.LeftFistDown)
        //     leftPulse = pulseTriggerValue;

        // if (gestures.RightFistDown)
        //     rightPulse = pulseTriggerValue;

        // leftPulse = Mathf.MoveTowards(leftPulse, 0f, pulseDecaySpeed * Time.deltaTime);
        // rightPulse = Mathf.MoveTowards(rightPulse, 0f, pulseDecaySpeed * Time.deltaTime);

        runtimeMaterial.SetFloat(Hand1FistHeldID, 0f);
        runtimeMaterial.SetFloat(Hand2FistHeldID, 0f);
        runtimeMaterial.SetFloat(Hand1PulseID, 0f);
        runtimeMaterial.SetFloat(Hand2PulseID, 0f);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetOutlineColor(bool isLeft, Color color)
    {
        if (runtimeMaterial == null) return;
        runtimeMaterial.SetColor(isLeft ? Fist1ColorID : Fist2ColorID, color);
    }

    public void SetMergeProgress(float t)
    {
        if (runtimeMaterial == null) return;
        runtimeMaterial.SetFloat(MergeProgressID, Mathf.Clamp01(t));
    }

    public Material GetRuntimeMaterial() => runtimeMaterial;

    /// <summary>
    /// Current smoothed screen-space radii (0-1 fraction of screen width).
    /// Read by FOVController to compute a matching world-space radius for gameplay.
    /// </summary>
    public float CurrentLeftRadius  => currentLeftRadius;
    public float CurrentRightRadius => currentRightRadius;

    /// <summary>
    /// Current smoothed screen-space positions (UV 0-1, pre-flip, same space as filter output).
    /// When a hand is absent, these drift toward the ghost home position.
    /// Read by FOVWorldCollider to provide a real world-space coordinate for merge checks.
    /// </summary>
    public Vector2 CurrentLeftPos  => currentLeftPos;
    public Vector2 CurrentRightPos => currentRightPos;

    /// <summary>
    /// Post-flip canvas UV positions — same transform applied when sending to the shader.
    /// Use these for cross-player distance checks that must work in both together and split mode.
    /// </summary>
    public Vector2 CurrentLeftPosCanvas
    {
        get
        {
            Vector2 uv = currentLeftPos;
            if (flipX) uv.x = 1f - uv.x;
            if (flipY) uv.y = 1f - uv.y;
            return uv;
        }
    }

    public Vector2 CurrentRightPosCanvas
    {
        get
        {
            Vector2 uv = currentRightPos;
            if (flipX) uv.x = 1f - uv.x;
            if (flipY) uv.y = 1f - uv.y;
            return uv;
        }
    }
}
