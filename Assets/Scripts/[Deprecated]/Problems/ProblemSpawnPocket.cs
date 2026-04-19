using UnityEngine;

/// <summary>
/// Level designer marker that defines a valid spawn location for problem objects.
///
/// Place GameObjects with this component throughout the level to define WHERE
/// problems can appear. ProblemManager discovers all pockets at Start via
/// FindObjectsByType — no manual list wiring required.
///
/// Each pocket tracks whether it is currently occupied and which problem
/// types it can produce. Pockets draw orange gizmos in the Scene view.
///
/// LEVEL DESIGN GUIDE
///   • 10–12 pockets recommended for a 5-minute round.
///   • Vary placement: some in open water, some behind breakable walls.
///   • Use spawnOnStart = false for pockets near player spawn zones to give
///     a grace window at round start.
///   • Which prefab spawns here is decided by ProblemManager (_realProblemRatio).
///     Pockets only control WHERE problems appear, not what type.
/// </summary>
public class ProblemSpawnPocket : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("If false, this pocket is skipped during the initial fill at round start. " +
             "Useful for pockets near player spawn points.")]
    [SerializeField] public bool spawnOnStart = true;

    [Header("Gizmo")]
    [SerializeField] private float gizmoRadius = 0.5f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    public bool IsOccupied { get; private set; }
    public ProblemObject CurrentProblem { get; private set; }

    // ── API ───────────────────────────────────────────────────────────────────

    public void Occupy(ProblemObject problem)
    {
        IsOccupied     = true;
        CurrentProblem = problem;
    }

    public void Release()
    {
        IsOccupied     = false;
        CurrentProblem = null;
    }

    // ── Gizmo ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = IsOccupied ? new Color(1f, 0.5f, 0f, 0.4f) : new Color(1f, 0.8f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, gizmoRadius);
    }
#endif
}
