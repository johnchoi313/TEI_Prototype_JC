using UnityEngine;

/// <summary>
/// Rotates the Mesh child pivot to face the fish's movement direction in XY.
/// Controls wake particle emission, size, and speed based on velocity × noise battery.
/// Draws a cyan forward-direction gizmo in Scene view (wire sphere = nose/front).
///
/// ROTATION — Y-flip + clamped Z:
///   Flips Y 180° when entering the left hemisphere so Z rotation stays in [-90°, 90°].
///   Tail/collider never sweeps a large arc; cannot clip through walls during turns.
///
/// PARTICLES — direction:
///   The Speed PS is a root sibling (not under Mesh pivot) so it doesn't auto-rotate.
///   Every frame we rotate the PS transform so its local +Y points opposite to velocity.
///   Unity Cone emitters emit along local +Y, so particles travel backward from the fish.
///   Simulation Space = World means already-emitted particles keep their trajectory
///   even when the fish turns.
///
/// PARTICLES — emission formula:
///   rate = (speedFraction × baseEmission) + (speedFraction × battery² × maxBatteryEmission)
///   battery² gives a quadratic curve: near-zero at low charge, dramatic at full charge.
///   Size and speed multipliers also scale with battery for compounding visual impact.
///
/// SETUP:
///   1. Add to fish root.
///   2. Wire _meshPivot  → the "Mesh" child Transform.
///   3. Wire _wakeParticles → the "Speed" child ParticleSystem.
///   4. Particle System Inspector:
///        Simulation Space = World
///        Shape = Cone, Angle 20, Radius 0.05
///        Start Size  = 1  (code sets startSizeMultiplier  each frame)
///        Start Speed = 1  (code sets startSpeedMultiplier each frame)
///        Start Lifetime = 0.5 ~ 1.0 (random between two constants)
///        Max Particles = 120
/// </summary>
public class FishAnimator : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Child empty Transform holding all mesh/visual objects. Auto-finds 'Mesh' child if empty.")]
    [SerializeField] private Transform _meshPivot;

    [Tooltip("Speed child ParticleSystem. Auto-finds first child PS if empty.")]
    [SerializeField] private ParticleSystem _wakeParticles;

    [Header("Rotation")]
    [Tooltip("Degrees per second the Z angle rotates toward velocity direction while moving.")]
    [SerializeField] private float rotationSpeed = 90f;

    [Tooltip("Degrees per second Z drifts back to 0 (horizontal) when fish is idle.")]
    [SerializeField] private float idleReturnSpeed = 45f;

    [Tooltip("Min speed (units/sec) before rotation updates. Prevents jitter at rest. " +
             "Raise this if the fish rotates when the player holds still (hand-tracking noise " +
             "at playtest distance produces ~0.2-0.3 units/sec even when stationary).")]
    [SerializeField] private float rotationDeadzone = 0.4f;

    [Tooltip("Framerate-independent velocity smoothing. 0 = instant, 0.99 = very sluggish. " +
             "Lower values settle faster so the fish stops rotating sooner after the hand stops.")]
    [Range(0f, 0.99f)]
    [SerializeField] private float velocitySmoothing = 0.65f;

    [Header("Particles — Speed Reference")]
    [Tooltip("Match this to FOVHighlightable.maxSpeed (default 3). Used to normalise speed → emission.")]
    [SerializeField] private float defaultMaxSpeed = 3f;

    [Header("Particles — Emission")]
    [Tooltip("Particles/sec at full default speed with ZERO battery. The baseline trickle when moving.")]
    [SerializeField] private float baseEmission = 8f;

    [Tooltip("Particles/sec ADDED at full battery + full speed. Uses battery² curve — very low when quiet, dramatic when loud.")]
    [SerializeField] private float maxBatteryEmission = 40f;

    [Header("Particles — Visual Scale")]
    [Tooltip("startSizeMultiplier at zero battery. Set Start Size = 1 in the PS Inspector.")]
    [SerializeField] private float minParticleSize = 0.05f;

    [Tooltip("startSizeMultiplier at full battery.")]
    [SerializeField] private float maxParticleSize = 0.22f;

    [Tooltip("startSpeedMultiplier at zero battery. Set Start Speed = 1 in the PS Inspector.")]
    [SerializeField] private float minParticleSpeed = 0.6f;

    [Tooltip("startSpeedMultiplier at full battery.")]
    [SerializeField] private float maxParticleSpeed = 4.0f;

    [Header("Gizmo")]
    [SerializeField] private float gizmoLength = 1.5f;

    // ── Private ───────────────────────────────────────────────────────────────

    private Vector3 _lastPosition;
    private Vector3 _smoothedVelocity;

    // Y-flip rotation state
    private bool  _facingLeft;
    private float _currentFacingZ;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        _lastPosition = transform.position;

        if (_meshPivot == null)
        {
            Transform t = transform.Find("Mesh");
            if (t != null) _meshPivot = t;
            else Debug.LogWarning($"[FishAnimator] No 'Mesh' child on {name}. Wire _meshPivot.", this);
        }

        if (_wakeParticles == null)
            _wakeParticles = GetComponentInChildren<ParticleSystem>();
        // Rigidbody setup is handled by FishFOVController.OnAwake.
    }

    private void LateUpdate()
    {
        // ── Velocity — framerate-independent exponential smoothing ────────────
        Vector3 rawVel = (transform.position - _lastPosition) / Time.deltaTime;
        rawVel.z = 0f;

        float alpha = 1f - Mathf.Pow(velocitySmoothing, Time.deltaTime * 60f);
        _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, rawVel, alpha);
        _lastPosition     = transform.position;

        float speed = _smoothedVelocity.magnitude;

        // ── Mesh pivot rotation ───────────────────────────────────────────────
        if (_meshPivot != null)
        {
            if (speed > rotationDeadzone)
            {
                float angle = Mathf.Atan2(_smoothedVelocity.y, _smoothedVelocity.x) * Mathf.Rad2Deg;

                // Y-flip when entering left hemisphere — keeps Z in [-90°, 90°].
                bool shouldFaceLeft = Mathf.Abs(angle) > 90f;
                if (shouldFaceLeft != _facingLeft)
                    _facingLeft = shouldFaceLeft;

                // Remap to [-90, 90]; negate when flipped because Y=180° mirrors Z.
                float targetZ = _facingLeft
                    ? -(angle > 0f ? angle - 180f : angle + 180f)
                    : angle;

                _currentFacingZ = Mathf.MoveTowardsAngle(
                    _currentFacingZ, targetZ, rotationSpeed * Time.deltaTime);
            }
            else
            {
                _currentFacingZ = Mathf.MoveTowardsAngle(
                    _currentFacingZ, 0f, idleReturnSpeed * Time.deltaTime);
            }

            _meshPivot.localEulerAngles = new Vector3(0f, _facingLeft ? 180f : 0f, _currentFacingZ);
        }

        // ── Particle system ───────────────────────────────────────────────────
        if (_wakeParticles != null)
        {
            float battery   = AmbientNoiseSampler.Instance != null
                              ? AmbientNoiseSampler.Instance.BatteryLevel : 0f;

            // Normalise speed against default max (not fast max) so a moving fish at
            // default speed already reads as ~1.0 — particles visible without noise.
            float speedFraction = Mathf.Clamp01(speed / defaultMaxSpeed);

            // ── Direction: point PS local +Y opposite to movement ─────────────
            // Unity Cone emitters shoot along local +Y. Rotating the PS transform
            // around Z so +Y faces backward makes particles trail behind the fish.
            // Because Simulation Space = World, already-emitted particles are unaffected.
            if (speed > rotationDeadzone)
            {
                // Atan2 of -velocity gives the angle of the backward direction.
                float backwardAngle = Mathf.Atan2(-_smoothedVelocity.y, -_smoothedVelocity.x)
                                      * Mathf.Rad2Deg;
                // -90° offset: +Y is 90° ahead of +X, so subtract 90° to align +Y to backwardAngle.
                _wakeParticles.transform.localEulerAngles =
                    new Vector3(0f, 0f, backwardAngle - 90f);
            }

            // ── Emission rate ─────────────────────────────────────────────────
            // battery² creates a quadratic curve: the difference between a quiet room
            // (battery≈0, boost≈0) and a loud one (battery≈1, boost=max) is dramatic.
            // Base emission ensures the fish always leaves a trickle when moving.
            float batterySquared = battery * battery;
            float rate = speedFraction * (baseEmission + batterySquared * maxBatteryEmission);

            var emission = _wakeParticles.emission;
            emission.rateOverTime = rate;

            // ── Particle size + speed scale with battery ──────────────────────
            // Three simultaneous visual channels (count, size, speed) make the
            // difference between silence and full battery unmistakably obvious.
            var main = _wakeParticles.main;
            main.startSizeMultiplier  = Mathf.Lerp(minParticleSize,  maxParticleSize,  batterySquared);
            main.startSpeedMultiplier = Mathf.Lerp(minParticleSpeed, maxParticleSpeed, batterySquared);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Z rotation of the mesh pivot, clamped to [-90, 90]. Used by MinimapPlayerTracker.</summary>
    public float FacingZAngle => _currentFacingZ;

    /// <summary>True when the fish is in the left hemisphere (mesh pivot has Y=180 applied).
    /// In 2D UI the equivalent is localScale.x = -1.</summary>
    public bool IsFacingLeft => _facingLeft;

    // ── Gizmo ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Transform pivot = _meshPivot != null ? _meshPivot : transform;
        Gizmos.color = Color.cyan;
        Vector3 fwd = pivot.right * gizmoLength;
        Gizmos.DrawRay(transform.position, fwd);
        // Wire sphere = nose (front of fish).
        Gizmos.DrawWireSphere(transform.position + fwd, 0.08f);
    }
}
