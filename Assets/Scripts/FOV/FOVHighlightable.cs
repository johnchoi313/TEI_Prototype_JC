using UnityEngine;

/// <summary>
/// Base class for any object that responds to FOV overlap.
///
/// ARCHITECTURE — subclass this, don't modify it:
///   Override OnEnterFOVRange()   → called once when FOV first touches this object
///   Override WhileInFOVRange()   → called every frame while overlapping (add more behaviors here)
///   Override OnReachedCenter()   → called once when object arrives at FOV center
///   Override OnExitFOVRange()    → called once when FOV stops touching this object
///   Override ApplyMovement()     → swap for Rigidbody physics in character subclass
///
/// Current behavior (prototype):
///   - Turns red while in FOV range
///   - Smoothly moves toward FOV center
///   - Turns green when it arrives at center
///   - Returns to original color and stops when FOV leaves
/// </summary>
public class FOVHighlightable : MonoBehaviour
{
    // ── Enums ─────────────────────────────────────────────────────────────────

    public enum FOVAttractionState { Idle, Attracted, AtCenter }

    /// <summary>
    /// Which player's FOV can attract this object.
    /// Kept separate from PlayerIndex intentionally — "Any" is a filter sentinel,
    /// not a real player identity, so it doesn't belong in the player lookup enum.
    /// </summary>
    public enum FOVOwner { Any, Player1Only, Player2Only }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("FOV Ownership")]
    [Tooltip("Which player's FOV can attract this object. Any = either player.")]
    [SerializeField] private FOVOwner respondTo = FOVOwner.Any;

    [Header("Visual Feedback")]
    [SerializeField] private Color inRangeColor  = Color.red;
    [SerializeField] private Color atCenterColor = Color.green;

    [Header("Attraction Settings")]
    [Tooltip("How quickly the object accelerates toward the FOV center (default / no noise).")]
    [SerializeField] protected float smoothTime      = 1.0f;

    [Tooltip("World-space distance from FOV center at which we consider the object 'arrived'.")]
    [SerializeField] private float arrivalThreshold = 0.1f;

    [Tooltip("Maximum movement speed in world units/sec (default / no noise).")]
    [SerializeField] protected float maxSpeed = 3f;

    [Header("Noise Battery Modulation")]
    [Tooltip("Read from AmbientNoiseSampler to scale speed with ambient noise level.")]
    [SerializeField] protected bool useNoiseBattery = true;

    [Tooltip("smoothTime when battery is fully charged (fastest). Lower = snappier.")]
    [SerializeField] protected float fastSmoothTime = 0.15f;

    [Tooltip("maxSpeed when battery is fully charged.")]
    [SerializeField] protected float fastMaxSpeed   = 10f;

    // ── State (readable by subclasses) ────────────────────────────────────────

    public FOVAttractionState AttractionState { get; private set; } = FOVAttractionState.Idle;
    public FOVOwner RespondTo => respondTo;

    /// <summary>
    /// When true, ApplyMovement is NOT called from Update — the object stops following the FOV.
    /// Set by ProblemObject during interaction animations via LockMovement() / UnlockMovement().
    /// FishFOVController.FixedUpdate also pins the physics body in place while locked.
    /// </summary>
    public bool MovementLocked { get; private set; }

    /// <summary>Stops this object from following the FOV. Safe to call from any script.</summary>
    public void LockMovement()   => MovementLocked = true;

    /// <summary>Resumes normal FOV-following after LockMovement was called.</summary>
    public void UnlockMovement() => MovementLocked = false;

    [Header("Debug (read-only in Play Mode)")]
    [SerializeField] private float _dbgBattery;
    [SerializeField] private float _dbgSmoothTime;
    [SerializeField] private float _dbgMaxSpeed;

    /// <summary>
    /// Current effective smooth time after noise-battery modulation.
    /// Readable by subclasses (e.g. FishFOVController) so they can replicate
    /// the same movement speed without duplicating the battery interpolation logic.
    /// Updated each frame inside ApplyMovement.
    /// </summary>
    protected float CurrentSmoothTime { get; private set; }

    /// <summary>
    /// Current effective max speed after noise-battery modulation.
    /// Readable by subclasses (e.g. FishFOVController).
    /// Updated each frame inside ApplyMovement.
    /// </summary>
    protected float CurrentMaxSpeed { get; private set; }

    /// <summary>World-space center of the FOV currently attracting this object (at this object's Z). Zero if idle.</summary>
    protected Vector3 ActiveFOVCenter { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    [Header("Renderer (drag child MeshRenderer here; auto-detected if left empty)")]
    [SerializeField] private Renderer _renderer;

    private Material _material;
    private Color    _originalColor;
    private Vector3  _smoothVelocity;   // used by SmoothDamp

    private static readonly int ColorID     = Shader.PropertyToID("_Color");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor"); // URP

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_renderer == null)
            _renderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        _material      = _renderer.material; // per-instance — won't affect other objects
        _originalColor = GetMaterialColor();
        OnAwake();
    }

    private void Update()
    {
        if (FOVWorldCollider.Instance == null) return;

        bool overlapped = TryGetOverlappingFOV(out Vector2 fovViewport, out FOVWorldCollider.HandWorldState activeHand);
        Camera activeCam = overlapped ? GetCameraForHand(activeHand) : null;

        switch (AttractionState)
        {
            case FOVAttractionState.Idle when overlapped:
                TransitionTo(FOVAttractionState.Attracted);
                ActiveFOVCenter = ViewportToWorldAtObjectZ(fovViewport, activeCam);
                OnEnterFOVRange(ActiveFOVCenter);
                break;

            case FOVAttractionState.Attracted:
            case FOVAttractionState.AtCenter:
                if (!overlapped)
                {
                    TransitionTo(FOVAttractionState.Idle);
                    OnExitFOVRange();
                    break;
                }

                // Project the viewport position to a world point at THIS object's Z depth.
                // Using ViewportPosition (not hand.WorldPosition) avoids parallax error
                // when objects sit at different Z depths than gamePlaneZ.
                ActiveFOVCenter = ViewportToWorldAtObjectZ(fovViewport, activeCam);

                // Smooth movement toward center — only during active gameplay and when not
                // locked by an interaction animation. FOV detection (color, highlights, events)
                // continues in all states regardless of the movement lock.
                bool canMove = !MovementLocked &&
                               (GameManager.Instance == null ||
                                GameManager.Instance.State == GameState.Playing);
                if (canMove) ApplyMovement(ActiveFOVCenter);
                WhileInFOVRange(ActiveFOVCenter, activeHand.WorldRadius);

                // Check for arrival — compare XY only, Z is irrelevant (FOV is 2D).
                float distToCenter = Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.y),
                    new Vector2(ActiveFOVCenter.x,    ActiveFOVCenter.y));

                if (AttractionState == FOVAttractionState.Attracted && distToCenter < arrivalThreshold)
                {
                    TransitionTo(FOVAttractionState.AtCenter);
                    OnReachedCenter();
                }
                else if (AttractionState == FOVAttractionState.AtCenter && distToCenter > arrivalThreshold)
                {
                    // FOV moved away from us while we were at center — re-attract.
                    TransitionTo(FOVAttractionState.Attracted);
                }
                break;
        }
    }

    // ── Virtual hooks — override in subclasses ────────────────────────────────

    /// <summary>Called in Awake, before any FOV logic. Use for subclass initialization.</summary>
    protected virtual void OnAwake() { }

    /// <summary>Called once the frame this object enters any FOV.</summary>
    protected virtual void OnEnterFOVRange(Vector3 fovCenter) { }

    /// <summary>Called every frame while inside an FOV (after movement is applied).</summary>
    protected virtual void WhileInFOVRange(Vector3 fovCenter, float fovWorldRadius) { }

    /// <summary>Called once when this object arrives at the FOV center.</summary>
    protected virtual void OnReachedCenter() { }

    /// <summary>Called once the frame this object exits all FOVs.</summary>
    protected virtual void OnExitFOVRange()
    {
        _smoothVelocity = Vector3.zero; // stop any residual movement
    }

    /// <summary>
    /// Moves this object toward target. Override in character subclass to use Rigidbody.
    /// Default: transform-based SmoothDamp (smooth acceleration + deceleration, no physics).
    /// When useNoiseBattery is true and AmbientNoiseSampler is present, smoothTime and
    /// maxSpeed are interpolated between base (slow) and fast values based on battery level.
    /// </summary>
    protected virtual void ApplyMovement(Vector3 target)
    {
        float currentSmoothTime = smoothTime;
        float currentMaxSpeed   = maxSpeed;

        if (useNoiseBattery && AmbientNoiseSampler.Instance != null)
        {
            float battery = AmbientNoiseSampler.Instance.BatteryLevel;
            currentSmoothTime = Mathf.Lerp(smoothTime, fastSmoothTime, battery);
            currentMaxSpeed   = Mathf.Lerp(maxSpeed,   fastMaxSpeed,   battery);
            _dbgBattery    = battery;
        }

        _dbgSmoothTime = currentSmoothTime;
        _dbgMaxSpeed   = currentMaxSpeed;

        // Expose to subclasses so they can replicate the same speed without
        // duplicating the battery interpolation logic.
        CurrentSmoothTime = currentSmoothTime;
        CurrentMaxSpeed   = currentMaxSpeed;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            target,
            ref _smoothVelocity,
            currentSmoothTime,
            currentMaxSpeed);
    }

    // ── FOV overlap detection ─────────────────────────────────────────────────

    private bool TryGetOverlappingFOV(out Vector2 viewportCenter, out FOVWorldCollider.HandWorldState activeHand)
    {
        bool checkLeft  = respondTo == FOVOwner.Any || respondTo == FOVOwner.Player1Only;
        bool checkRight = respondTo == FOVOwner.Any || respondTo == FOVOwner.Player2Only;

        if (checkLeft  && IsOverlappedBy(FOVWorldCollider.Instance.LeftHand,  out viewportCenter)) { activeHand = FOVWorldCollider.Instance.LeftHand;  return true; }
        if (checkRight && IsOverlappedBy(FOVWorldCollider.Instance.RightHand, out viewportCenter)) { activeHand = FOVWorldCollider.Instance.RightHand; return true; }
        viewportCenter = Vector2.zero;
        activeHand     = default;
        return false;
    }

    private bool IsOverlappedBy(FOVWorldCollider.HandWorldState hand, out Vector2 viewportCenter)
    {
        // Return the viewport center so the caller can project it at the correct Z depth.
        viewportCenter = hand.ViewportPosition;
        if (!hand.IsActive || hand.IsGhost) return false;

        // Use the camera for this hand's owner — in split mode P2 must use P2's camera.
        Camera cam = GetCameraForHand(hand);
        if (cam == null) return false;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        if (vp.z < 0f) return false;

        // Aspect-ratio correction mirrors the shader graph's distance calculation.
        Vector2 delta = new Vector2(vp.x, vp.y) - hand.ViewportPosition;
        delta.x *= (float)Screen.width / Screen.height;
        return delta.magnitude < hand.ScreenRadius;
    }

    /// <summary>
    /// Projects a viewport-space position (0-1) to a world point at this object's Z depth.
    /// More accurate than using hand.WorldPosition which is always projected at gamePlaneZ.
    /// </summary>
    private Vector3 ViewportToWorldAtObjectZ(Vector2 viewportPos, Camera cam)
    {
        if (cam == null) return transform.position;

        if (cam.orthographic)
        {
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            float x = Mathf.Lerp(-w, w, viewportPos.x) + cam.transform.position.x;
            float y = Mathf.Lerp(-h, h, viewportPos.y) + cam.transform.position.y;
            return new Vector3(x, y, transform.position.z);
        }
        else
        {
            float dist = Mathf.Abs(cam.transform.position.z - transform.position.z);
            Vector3 worldPoint = cam.ViewportToWorldPoint(
                new Vector3(viewportPos.x, viewportPos.y, dist));
            return new Vector3(worldPoint.x, worldPoint.y, transform.position.z);
        }
    }

    private static Camera GetCameraForHand(FOVWorldCollider.HandWorldState hand)
    {
        if (SplitScreenController.Instance != null)
            return SplitScreenController.Instance.GetCameraForPlayer(hand.Owner);
        return Camera.main;
    }

    // ── State transition ──────────────────────────────────────────────────────

    private void TransitionTo(FOVAttractionState next)
    {
        AttractionState = next;
    }

    // ── Material helpers ──────────────────────────────────────────────────────

    private Color GetMaterialColor()
    {
        if (_material.HasProperty(BaseColorID)) return _material.GetColor(BaseColorID);
        if (_material.HasProperty(ColorID))     return _material.GetColor(ColorID);
        return Color.white;
    }

    protected void SetMaterialColor(Color color)
    {
        if (_material.HasProperty(BaseColorID)) _material.SetColor(BaseColorID, color);
        else if (_material.HasProperty(ColorID)) _material.SetColor(ColorID, color);
    }
}
