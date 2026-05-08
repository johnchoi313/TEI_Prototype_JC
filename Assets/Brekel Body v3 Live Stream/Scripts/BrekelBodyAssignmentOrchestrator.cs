using UnityEngine;

/// <summary>
/// Two-player friendly Brekel slot assignment: each <see cref="Brekel_Body_v3_DefaultMapper"/> in <see cref="targets"/>
/// gets stream index <c>0</c>, <c>1</c>, … or <c>-1</c>. Chooses bodies by waist position nearest this player's last waist,
/// enforces minimum waist separation between different players, and only considers indices Brekel actually sent this frame
/// (<see cref="Brekel_Body_v3_Receiver.ActiveBodyCount"/>).
/// </summary>
[DefaultExecutionOrder(-100)]
public class BrekelBodyAssignmentOrchestrator : MonoBehaviour
{
    [Header("References")]
    public Brekel_Body_v3_Receiver receiver;

    [Tooltip("Order matters: targets[0] is filled first, then targets[1], …")]
    public Brekel_Body_v3_DefaultMapper[] targets;

    [Header("Distance (meters, Unity space)")]
    [Tooltip(
        "Minimum distance between two waists when both mappers have a body. Stops the same physical person " +
        "(duplicate tracks) from driving both avatars.")]
    public float minSeparationBetweenPlayers = 0.35f;

    [Tooltip(
        "If &gt; 0: ignore stream bodies whose waist is farther than this from this mapper's last assigned waist. " +
        "Use 0 to disable (recommended start).")]
    public float maxJumpFromLastWaist = 0f;

    private Vector3[] _lastWaistWorld;
    private bool[]    _hasLastWaist;

    private void Awake()
    {
        int n = targets != null ? targets.Length : 0;
        _lastWaistWorld = new Vector3[n];
        _hasLastWaist   = new bool[n];
    }

    private void Start()
    {
        if (targets == null)
            return;
        foreach (Brekel_Body_v3_DefaultMapper m in targets)
        {
            if (m != null && !m.useOrchestratorAssignment)
                Debug.LogWarning(
                    $"[BrekelBodyAssignmentOrchestrator] '{m.gameObject.name}' is in targets but " +
                    $"Use Orchestrator Assignment is off — it will be skipped.",
                    m);
        }
    }

    private void Update()
    {
        if (receiver == null || !receiver.IsConnected)
        {
            ResetAssignments();
            return;
        }

        int maxBodies = Brekel_Body_v3_Receiver.MaxBodies;
        int active    = Mathf.Clamp(receiver.ActiveBodyCount, 0, maxBodies);

        var waistAtSlot = new Vector3?[maxBodies];
        for (int b = 0; b < active; b++)
        {
            BrekelBodyFrame f = receiver.GetBody(b);
            if (f != null)
                waistAtSlot[b] = f.joints[(int)Brekel_joint_name_v3.waist].position;
        }

        var assignment = new int[targets != null ? targets.Length : 0];
        for (int i = 0; i < assignment.Length; i++)
            assignment[i] = -1;

        var slotUsed = new bool[maxBodies];
        float minSep = minSeparationBetweenPlayers;

        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                Brekel_Body_v3_DefaultMapper mapper = targets[i];
                if (mapper == null || !mapper.useOrchestratorAssignment)
                    continue;

                int   bestSlot = -1;
                float bestRank = float.MaxValue;

                for (int b = 0; b < active; b++)
                {
                    if (slotUsed[b] || !waistAtSlot[b].HasValue)
                        continue;

                    Vector3 w = waistAtSlot[b].Value;

                    if (!SeparatedFromEarlierPlayers(i, w, assignment, waistAtSlot, minSep))
                        continue;

                    if (maxJumpFromLastWaist > 0f && _hasLastWaist[i])
                    {
                        if (Vector3.Distance(w, _lastWaistWorld[i]) > maxJumpFromLastWaist)
                            continue;
                    }

                    float rank = RankSlotForMapper(i, b, w);
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        bestSlot = b;
                    }
                }

                if (bestSlot >= 0)
                {
                    assignment[i]        = bestSlot;
                    slotUsed[bestSlot]   = true;
                    Vector3 w            = waistAtSlot[bestSlot].Value;
                    _lastWaistWorld[i]   = w;
                    _hasLastWaist[i]     = true;
                    mapper.SetAssignedBodySlot(bestSlot);
                }
                else
                {
                    mapper.SetAssignedBodySlot(-1);
                }
            }
        }
    }

    /// <summary>Prefer body nearest where this player was last frame; tie-break lower Brekel index (stable).</summary>
    private float RankSlotForMapper(int mapperIndex, int slotIndex, Vector3 waistWorld)
    {
        const float indexTieBreak = 0.0001f;
        if (_hasLastWaist[mapperIndex])
            return Vector3.Distance(waistWorld, _lastWaistWorld[mapperIndex]) + slotIndex * indexTieBreak;
        return slotIndex * indexTieBreak;
    }

    /// <summary>Waist must stay at least <paramref name="minSep"/> from every higher-priority mapper already assigned this frame.</summary>
    private static bool SeparatedFromEarlierPlayers(
        int mapperIndex,
        Vector3 candidateWaist,
        int[] assignment,
        Vector3?[] waistAtSlot,
        float minSep)
    {
        if (minSep <= 0f)
            return true;

        for (int j = 0; j < mapperIndex; j++)
        {
            int sj = assignment[j];
            if (sj < 0 || !waistAtSlot[sj].HasValue)
                continue;
            if (Vector3.Distance(waistAtSlot[sj].Value, candidateWaist) < minSep)
                return false;
        }

        return true;
    }

    private void OnDisable()
    {
        ResetAssignments();
    }

    private void ResetAssignments()
    {
        if (_hasLastWaist != null)
            for (int i = 0; i < _hasLastWaist.Length; i++)
                _hasLastWaist[i] = false;

        if (targets == null)
            return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null && targets[i].useOrchestratorAssignment)
                targets[i].SetAssignedBodySlot(-1);
        }
    }
}
