using UnityEngine;

/// <summary>
/// Fish-specific subclass of FOVHighlightable.
///
/// WHY THIS EXISTS:
///   FOVHighlightable.ApplyMovement uses direct transform.position writes + SmoothDamp.
///   On a dynamic Rigidbody this causes a race condition: the physics engine applies
///   depenetration forces after FishAnimator.FixedUpdate already zeroed velocity, so
///   the fish jolts and rotates near walls. SmoothDamp lag also keeps FishAnimator's
///   smoothed velocity non-zero at rest → perpetual rotation glitch.
///
/// WHAT THIS DOES:
///   1. Smooths the raw FOV center target before it reaches ApplyMovement, absorbing
///      per-frame MediaPipe jitter before it becomes velocity in FishAnimator.
///
///   2. Overrides ApplyMovement to cache a _targetPos instead of writing transform.position.
///      When AtCenter, snaps tightly (very short smoothTime) so the fish is always
///      coincident with the FOV circle → FishAnimator sees near-zero velocity → no rotation.
///
///   3. Applies the cached position via rb.MovePosition in FixedUpdate. On a dynamic
///      Rigidbody, MovePosition is a physics constraint — the engine moves the fish as
///      far as it can without overlapping static colliders. Wall blocking is preserved;
///      the transform.position vs physics race condition is eliminated.
///
/// SETUP:
///   Replace FOVHighlightable on each fish root GO with this component.
///   Re-wire respondTo (Player1Only / Player2Only) in the Inspector.
///   Keep the Rigidbody as dynamic — OnAwake sets the correct constraints.
///   FishAnimator.FixedUpdate (velocity zeroing) should be removed; it is no longer needed.
/// </summary>
public class FishFOVController : FOVHighlightable
{
    [Header("Fish — Input Filter")]
    [Tooltip("How fast the smoothed FOV target tracks the real FOV center. " +
             "12–15 = responsive but jitter-free at playtest distance. " +
             "Lower = more lag but smoother; higher = tracks more of the raw noise.")]
    [SerializeField] private float targetSmoothRate = 12f;

    [Header("Fish — At-Center Tracking")]
    [Tooltip("smoothTime used when AtCenter state is active. " +
             "Very short so the fish is pinned precisely under the FOV circle, " +
             "meaning FishAnimator sees near-zero velocity and holds facing direction.")]
    [SerializeField] private float atCenterSmoothTime = 0.05f;

    [Header("Stand-Position Override")]
    [Tooltip("Smooth time used when driving the fish to a problem's stand position.")]
    [SerializeField] private float _overrideSmoothTime = 0.3f;

    [Tooltip("Max speed when driving the fish to a problem's stand position.")]
    [SerializeField] private float _overrideMaxSpeed = 12f;

    // ── Private ───────────────────────────────────────────────────────────────

    private Rigidbody _rb;

    /// <summary>Jitter-filtered version of ActiveFOVCenter; what we actually chase.</summary>
    private Vector3 _smoothedTarget;
    private bool    _targetInitialized;

    /// <summary>Position computed in Update (ApplyMovement); applied in FixedUpdate.</summary>
    private Vector3 _targetPos;

    private Vector3 _vel; // SmoothDamp velocity ref — shared across all movement paths, MUST be
                          // zeroed whenever switching paths to prevent velocity bleed.

    // Stand-position override — set by ProblemObject when a power-up interaction begins.
    // Redirects movement to a fixed world position instead of the FOV center.
    private Vector3? _overrideTarget = null;

    // Cached world bounds — used to clamp _targetPos before rb.MovePosition as a safety net
    // against any movement path sending the fish past the boundary walls.
    private Rect? _worldBounds;

    private void Start()
    {
        MinimapBoundsMarker marker = FindAnyObjectByType<MinimapBoundsMarker>();
        if (marker != null) _worldBounds = marker.worldBounds;
    }

    protected override void OnAwake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.isKinematic   = false; // dynamic — rb.MovePosition respects static colliders
            _rb.constraints   = RigidbodyConstraints.FreezeRotation
                              | RigidbodyConstraints.FreezePositionZ;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        _targetPos = transform.position;
    }

    // ── Public API — stand-position override ─────────────────────────────────

    /// <summary>
    /// Called by ProblemObject when a power-up interaction begins.
    /// The fish will smoothly move to worldPos and stay there until ClearMovementOverride.
    /// Works even when the fish is outside FOV range (handled in FixedUpdate).
    /// </summary>
    public void SetOverrideTarget(Vector3 worldPos)
    {
        _overrideTarget = worldPos;

        // Sever the velocity link between the FOV-following path and the override path.
        // Without this, _vel retains the fish's current travel direction and magnitude,
        // causing SmoothDamp to overshoot _targetPos into or through a boundary wall
        // in the first few FixedUpdate frames of the override.
        _smoothedTarget = transform.position; // start smooth from current position
        _vel            = Vector3.zero;       // kill SmoothDamp accumulated momentum

        // Also zero the Rigidbody's own physics velocity so the physics engine doesn't
        // carry pre-existing movement into the override's MovePosition calls.
        if (_rb != null) _rb.linearVelocity = Vector3.zero;
    }

    /// <summary>
    /// Called by ProblemObject after the interaction animation completes.
    /// Resumes normal FOV-following behavior.
    /// </summary>
    public void ClearMovementOverride()
    {
        _overrideTarget = null;

        // Reset _smoothedTarget to current position so FOV-following resumes from where
        // the fish actually is, rather than snapping toward the last smoothed target.
        // Also zero _vel so any residual override SmoothDamp momentum doesn't bleed
        // into the first ApplyMovement call after the override ends.
        _smoothedTarget = transform.position;
        _vel            = Vector3.zero;
        if (_rb != null) _rb.linearVelocity = Vector3.zero;
    }

    // ── FOVHighlightable overrides ────────────────────────────────────────────

    /// <summary>
    /// Called by FOVHighlightable.Update each frame the fish is inside an FOV.
    /// target = ActiveFOVCenter (viewport-projected to world at fish's Z).
    ///
    /// We do NOT write transform.position here — that races with physics.
    /// Instead we store _targetPos and let FixedUpdate apply it via MovePosition.
    /// </summary>
    protected override void ApplyMovement(Vector3 target)
    {
        // Note: override target and movement lock are both handled in FixedUpdate,
        // not here. FOVHighlightable.Update gates this call via MovementLocked,
        // so this path only runs when the fish is freely following the FOV.

        // ── Step 1: filter the raw target ────────────────────────────────────
        // Lerp at targetSmoothRate/sec absorbs high-frequency MediaPipe jitter.
        // The smoothed target settles to zero delta when the hand holds still →
        // FishAnimator's smoothed velocity drops below rotationDeadzone → no rotation.
        if (!_targetInitialized)
        {
            _smoothedTarget    = target;
            _targetPos         = transform.position;
            _targetInitialized = true;
        }
        _smoothedTarget = Vector3.Lerp(_smoothedTarget, target, targetSmoothRate * Time.deltaTime);

        // ── Step 2: compute battery-modulated speed (mirrors FOVHighlightable logic) ──
        // We duplicate these 6 lines rather than call base.ApplyMovement(), because
        // base.ApplyMovement also writes transform.position — calling it here would
        // apply two SmoothDamp passes per frame, cutting effective speed in half.
        float st  = smoothTime;
        float spd = maxSpeed;
        if (useNoiseBattery && AmbientNoiseSampler.Instance != null)
        {
            float battery = AmbientNoiseSampler.Instance.BatteryLevel;
            st  = Mathf.Lerp(smoothTime,  fastSmoothTime, battery);
            spd = Mathf.Lerp(maxSpeed,    fastMaxSpeed,   battery);
        }

        // ── Step 3: choose approach behaviour based on state ──────────────────
        // AtCenter  → very tight smoothing so fish is pinned to the FOV circle.
        //             FishAnimator sees near-zero velocity → holds facing direction.
        // Attracted → normal battery-modulated SmoothDamp.
        float activeSmoothTime = AttractionState == FOVAttractionState.AtCenter
            ? atCenterSmoothTime
            : st;
        float activeMaxSpeed = AttractionState == FOVAttractionState.AtCenter
            ? Mathf.Infinity   // no speed cap when snapping
            : spd;

        // Cache into _targetPos; applied in FixedUpdate via rb.MovePosition so the
        // physics engine can stop the fish at wall surfaces without a race condition.
        _targetPos = Vector3.SmoothDamp(
            transform.position, _smoothedTarget, ref _vel, activeSmoothTime, activeMaxSpeed);
    }

    protected override void OnExitFOVRange()
    {
        base.OnExitFOVRange();
        _vel = Vector3.zero;
    }

    // ── FixedUpdate — physics-safe movement ───────────────────────────────────

    private void FixedUpdate()
    {
        // Priority 1: override target is set → drive fish toward stand position.
        // This runs unconditionally — regardless of AttractionState, FOV range, or MovementLocked.
        // FOVHighlightable.MovementLocked gates ApplyMovement (Update path), so Update won't
        // fight against this. Both systems can be active simultaneously: lock stops FOV following,
        // override drives the fish to the stand position.
        if (_overrideTarget.HasValue)
        {
            if (!_targetInitialized) { _smoothedTarget = transform.position; _targetInitialized = true; }
            _smoothedTarget = Vector3.Lerp(_smoothedTarget, _overrideTarget.Value, targetSmoothRate * Time.fixedDeltaTime);
            _targetPos = Vector3.SmoothDamp(transform.position, _smoothedTarget, ref _vel, _overrideSmoothTime, _overrideMaxSpeed);
        }
        // Priority 2: movement is locked but no override target → freeze the physics body in place.
        // _targetPos keeps its last value from the previous ApplyMovement call; snap it to
        // the current transform so the Rigidbody doesn't drift from residual velocity.
        else if (MovementLocked)
        {
            _targetPos = transform.position;
            _vel       = Vector3.zero;
        }
        // Priority 3 (normal): _targetPos was already set by ApplyMovement in Update this frame.

        // Safety clamp: no movement path should ever send the fish past the level boundary.
        // The velocity resets above prevent this in practice, but the clamp is a hard guarantee
        // that survives any future changes to the movement paths.
        _targetPos = ClampToBounds(_targetPos);

        // MovePosition on a dynamic Rigidbody moves the body to the target position
        // as a physics constraint — the engine sweeps and stops at colliders.
        // This is the correct way to drive a physics body from scripted positions
        // without the transform.position race condition against depenetration forces.
        if (_rb != null)
            _rb.MovePosition(_targetPos);
    }

    private Vector3 ClampToBounds(Vector3 pos)
    {
        if (!_worldBounds.HasValue) return pos;
        pos.x = Mathf.Clamp(pos.x, _worldBounds.Value.xMin, _worldBounds.Value.xMax);
        pos.y = Mathf.Clamp(pos.y, _worldBounds.Value.yMin, _worldBounds.Value.yMax);
        return pos;
    }
}
