using UnityEngine;

/// <summary>
/// Physics-based fish controller for the maze.
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
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerFishController : MonoBehaviour
{
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

    [Header("Line of Sight")]
    [Tooltip("Layer(s) whose colliders block line-of-sight. Set to your maze wall layer.")]
    [SerializeField] private LayerMask _wallLayerMask = ~0;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Rigidbody _rb;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity    = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints   = RigidbodyConstraints.FreezeRotation
                          | RigidbodyConstraints.FreezePositionZ;
    }

    private void FixedUpdate()
    {
        if (_target == null || !IsInRange() || !HasLineOfSight())
        {
            // No valid target — bleed velocity to zero.
            _rb.linearVelocity *= 1f - _drag * Time.fixedDeltaTime;
            return;
        }

        Vector3 toTarget = _target.position - transform.position;
        toTarget.z = 0f;

        _rb.linearVelocity = toTarget.normalized * _followSpeed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsInRange()
    {
        Vector3 delta = _target.position - transform.position;
        delta.z = 0f;
        return delta.sqrMagnitude <= _followRadius * _followRadius;
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

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = new Color(1f, 0.9f, 0.2f, 0.3f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, _followRadius);

        if (_target == null) return;

        bool active = Application.isPlaying && IsInRange() && HasLineOfSight();
        Gizmos.color = active ? new Color(0.2f, 1f, 0.3f, 0.8f) : new Color(1f, 0.2f, 0.2f, 0.5f);
        Gizmos.DrawLine(transform.position, _target.position);
    }
#endif
}
