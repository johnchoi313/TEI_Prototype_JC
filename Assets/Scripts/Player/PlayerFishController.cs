using UnityEngine;

/// <summary>
/// Physics-based fish controller for the maze.
///
/// Mic-volume speed override
///   Assign a MicVolumeToFishSpeed component to Mic Volume Driver.
///   When set, the fish's follow speed is driven by that component's
///   CurrentSpeed each FixedUpdate instead of the serialized _followSpeed
///   field. Multiple fish controllers can share one MicVolumeToFishSpeed.
///
/// Particle emission
///   Assign a ParticleSystem to Speed Particles. Its emission rateOverTime is
///   remapped every Update from [MinEmission, MaxEmission] based on current speed.
///   Enable Use Driver Speed Range to automatically pull the speed bounds from the
///   assigned MicVolumeToFishSpeed (MinSpeed / MaxSpeed); otherwise set them manually.
///
/// Automatically follows _target if it is:
///   - Within _followRadius world units, AND
///   - Has unobstructed line-of-sight (no wall colliders between fish and target).
///
/// When either condition fails the fish bleeds velocity to a stop via _drag.
/// The fish is constrained by physics — maze wall colliders block it naturally.
/// Gravity is disabled and rotation is fully frozen (top-down XY plane).
///
/// SCENE SETUP
///   1. Create a GameObject with a primitive collider + Rigidbody.
///   2. Attach PlayerFishController.
///   3. Assign _target (typically the paired PlayerLightController's Transform).
///   4. Set _wallLayerMask to the layer your maze wall cubes are on.
///   5. Optionally assign a shared MicVolumeToFishSpeed to Mic Volume Driver.
/// </summary>
public enum EmissionCurveMode { Linear, Exponential }

[RequireComponent(typeof(Rigidbody))]
public class PlayerFishController : MonoBehaviour
{
    [Header("Mic Volume Driver")]
    [Tooltip("Optional. When assigned, follow speed is read from this component each frame " +
             "instead of the Follow Speed field below. Lets multiple fish share one mic source.")]
    [SerializeField] private MicVolumeToFishSpeed _micVolumeDriver;

    [Header("Target")]
    [Tooltip("The transform this fish follows (e.g. a PlayerLightController GameObject).")]
    [SerializeField] private Transform _target;

    [Header("Follow Behaviour")]
    [Tooltip("Maximum speed the fish moves toward the target (wu/s).")]
    [SerializeField] private float _followSpeed = 4f;

    [Tooltip("World-unit radius within which the target attracts the fish.")]
    [SerializeField] private float _followRadius = 5f;

    [Tooltip("Velocity damping factor applied per second when there is no valid target. " +
             "Higher = stops faster. 1 = instant stop.")]
    [Range(0f, 1f)]
    [SerializeField] private float _drag = 0.85f;

    [Tooltip("Distance at which the fish is considered to have reached the target and stops moving.")]
    [SerializeField] private float _arrivalThreshold = 1f;

    [Header("Line of Sight")]
    [Tooltip("Layer(s) whose colliders block line-of-sight. Set to your maze wall layer.")]
    [SerializeField] private LayerMask _wallLayerMask = ~0;

    [Header("Mesh Rotation")]
    [Tooltip("Child transform that holds the fish mesh. Only this object rotates — the collider parent stays frozen.")]
    [SerializeField] private Transform _meshTransform;

    [Tooltip("Degrees per second the mesh rotates toward its target orientation.")]
    [SerializeField] private float _rotationSpeed = 180f;


    [Header("Particle Effects")]
    [Tooltip("ParticleSystem whose emission rateOverTime is driven by current fish speed.")]
    [SerializeField] private ParticleSystem _speedParticles;

    [Tooltip("Emission rate (particles/sec) when speed is at or below the minimum.")]
    [SerializeField] private float _particleMinEmission = 0f;

    [Tooltip("Emission rate (particles/sec) when speed is at or above the maximum.")]
    [SerializeField] private float _particleMaxEmission = 50f;

    [Tooltip("When enabled and a Mic Volume Driver is assigned, the emission speed range is " +
             "read automatically from MicVolumeToFishSpeed.MinSpeed / MaxSpeed.")]
    [SerializeField] private bool _useDriverSpeedRange = true;

    [Tooltip("Speed (wu/s) that maps to minimum emission. Used when Use Driver Speed Range is off " +
             "or no Mic Volume Driver is assigned.")]
    [SerializeField] private float _particleMinSpeed = 0f;

    [Tooltip("Speed (wu/s) that maps to maximum emission. Used when Use Driver Speed Range is off " +
             "or no Mic Volume Driver is assigned.")]
    [SerializeField] private float _particleMaxSpeed = 10f;

    [Tooltip("Linear: emission scales evenly with speed.\n" +
             "Exponential: emission stays low until speed is high, then rises sharply — " +
             "few bubbles at rest, burst at full speed. Controlled by Exponent below.")]
    [SerializeField] private EmissionCurveMode _emissionCurveMode = EmissionCurveMode.Exponential;

    [Tooltip("Exponent for exponential mode. 1 = identical to linear. " +
             "2 = quadratic (moderate curve). 3–4 = very bottom-heavy, large burst at high speed.")]
    [SerializeField] private float _emissionExponent = 2f;

    [Header("Wiggle")]
    [Tooltip("Max wiggle angle added to Y (side-to-side tail wag) while moving.")]
    [SerializeField] private float _wiggleAmplitudeY = 15f;

    [Tooltip("Max wiggle angle added to Z (up-down body flex) while moving.")]
    [SerializeField] private float _wiggleAmplitudeZ = 3f;

    [Tooltip("Wiggle oscillation speed in Hz.")]
    [SerializeField] private float _wiggleFrequency = 3f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Rigidbody _rb;
    private float _meshRotZ   = 0f;   // current Z tilt (fish nose up/down)
    private float _meshRotY   = 0f;   // current Y flip (0 = right, 180 = left)
    private float _wiggleTime = 0f;   // independent time accumulator for wiggle

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity    = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints   = RigidbodyConstraints.FreezeRotation
                          | RigidbodyConstraints.FreezePositionZ;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the fish's follow speed at runtime (e.g. driven by mic volume).
    /// </summary>
    public float FollowSpeed
    {
        get => _followSpeed;
        set => _followSpeed = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Physics-safe teleport: moves the Rigidbody and clears all velocity so
    /// the fish doesn't drift away from its new position on the next physics tick.
    /// Preserves the fish's original Z so it stays on the XY plane.
    /// </summary>
    public void Teleport(Vector3 worldXY)
    {
        Vector3 target = new Vector3(worldXY.x, worldXY.y, transform.position.z);
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.position        = target;
        transform.position  = target;
    }

    private void Update()
    {
        if (_meshTransform == null || _target == null) return;
        UpdateMeshRotation();
        UpdateParticleEmission();
    }

    private void FixedUpdate()
    {
        if (_target == null || !IsInRange() || !HasLineOfSight())
        {
            _rb.linearVelocity *= 1f - _drag * Time.fixedDeltaTime;
            return;
        }

        if (HasArrived())
        {
            _rb.linearVelocity = Vector3.zero;
            return;
        }

        Vector3 toTarget = _target.position - transform.position;
        toTarget.z = 0f;

        float dist = toTarget.magnitude;
        float activeSpeed = _micVolumeDriver != null ? _micVolumeDriver.CurrentSpeed : _followSpeed;
        float speed = Mathf.Min(activeSpeed, dist / Time.fixedDeltaTime);
        _rb.linearVelocity = toTarget.normalized * speed;
    }

    // ── Mesh rotation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rotates the cosmetic mesh child independently of the physics collider:
    ///   Z axis — tilts the nose toward the target, clamped to ±80°.
    ///             Positive Z = counter-clockwise = nose tilts up-left.
    ///   Y axis — flips the sprite/mesh: 0° when target is to the right,
    ///             180° when target is to the left.
    /// Both angles are smoothed at _rotationSpeed degrees/second.
    /// </summary>
    private void UpdateMeshRotation()
    {
        Vector3 toTarget = _target.position - transform.position;
        toTarget.z = 0f;

        // Y flip: facing direction from X component.
        float targetY = (toTarget.x < 0f) ? 180f : 0f;

        // Z tilt: angle in XY plane, clamped so fish doesn't flip upside-down.
        float angle   = Mathf.Atan2(toTarget.y, Mathf.Abs(toTarget.x)) * Mathf.Rad2Deg;
        float targetZ = Mathf.Clamp(angle, -80f, 80f);

        // Smooth base orientation.
        float step = _rotationSpeed * Time.deltaTime;
        _meshRotY = Mathf.MoveTowards(_meshRotY, targetY, step);
        _meshRotZ = Mathf.MoveTowards(_meshRotZ, targetZ, step);

        // Wiggle: only while actively moving toward target.
        float speed = _rb.linearVelocity.magnitude;
        float activeSpeed = _micVolumeDriver != null ? _micVolumeDriver.CurrentSpeed : _followSpeed;
        float speedFactor = Mathf.Clamp01(speed / Mathf.Max(activeSpeed, 0.001f));

        if (speedFactor > 0.01f)
            _wiggleTime += Time.deltaTime * _wiggleFrequency * Mathf.PI * 2f;

        float wiggleY = Mathf.Sin(_wiggleTime)           * _wiggleAmplitudeY * speedFactor;
        float wiggleZ = Mathf.Sin(_wiggleTime + Mathf.PI * 0.5f) * _wiggleAmplitudeZ * speedFactor;

        _meshTransform.localRotation = Quaternion.Euler(0f, _meshRotY + wiggleY, _meshRotZ + wiggleZ);
    }

    // ── Particle emission ─────────────────────────────────────────────────────

    private void UpdateParticleEmission()
    {
        if (_speedParticles == null) return;

        float minSpeed = _particleMinSpeed;
        float maxSpeed = _particleMaxSpeed;
        if (_useDriverSpeedRange && _micVolumeDriver != null)
        {
            minSpeed = _micVolumeDriver.MinSpeed;
            maxSpeed = _micVolumeDriver.MaxSpeed;
        }

        float t = Mathf.InverseLerp(minSpeed, Mathf.Max(maxSpeed, minSpeed + 0.001f), _rb.linearVelocity.magnitude);

        if (_emissionCurveMode == EmissionCurveMode.Exponential)
            t = Mathf.Pow(t, Mathf.Max(_emissionExponent, 0.001f));

        float emission = Mathf.Lerp(_particleMinEmission, _particleMaxEmission, t);

        var em = _speedParticles.emission;
        em.rateOverTime = emission;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsInRange()
    {
        Vector3 delta = _target.position - transform.position;
        delta.z = 0f;
        return delta.sqrMagnitude <= _followRadius * _followRadius;
    }

    private bool HasArrived()
    {
        Vector3 delta = _target.position - transform.position;
        delta.z = 0f;
        return delta.sqrMagnitude <= _arrivalThreshold * _arrivalThreshold;
    }

    /// <summary>
    /// 3D raycast from fish to target on the XY plane.
    /// Returns true when nothing on _wallLayerMask blocks the path.
    /// </summary>
    private bool HasLineOfSight()
    {
        Vector3 origin    = transform.position; origin.z = 0f;
        Vector3 targetPos = _target.position;   targetPos.z = 0f;

        Vector3 dir  = targetPos - origin;
        float   dist = dir.magnitude;

        if (dist < 0.001f) return true;

        return !Physics.Raycast(origin, dir / dist, dist, _wallLayerMask,
                                QueryTriggerInteraction.Ignore);
    }

}
