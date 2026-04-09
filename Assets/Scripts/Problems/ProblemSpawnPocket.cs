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
///   • Assign 2–3 candidateProblems per pocket for variety over the session.
///   • Use spawnOnStart = false for pockets near player spawn zones to give
///     a grace window at round start.
/// </summary>
public class ProblemSpawnPocket : MonoBehaviour
{
    [Header("Content")]
    [Tooltip("Which problem types can spawn here. Picked randomly each time a problem spawns.")]
    [SerializeField] private ProblemDefinition[] candidateProblems;

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

    /// <summary>
    /// Returns a random candidate definition, or null if none are assigned.
    /// </summary>
    public ProblemDefinition PickDefinition()
    {
        if (candidateProblems == null || candidateProblems.Length == 0)
        {
            Debug.LogWarning($"[ProblemSpawnPocket] {name} has no candidate problems assigned.", this);
            return null;
        }
        return candidateProblems[Random.Range(0, candidateProblems.Length)];
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
