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
            int n = targets.Length;

            // Compute each mapper's best candidate and the rank of that match.
            // Then process mappers in order of best rank (closest match first) so the
            // mapper with the strongest spatial claim always wins, regardless of targets[] order.
            var bestSlots = new int[n];
            var bestRanks = new float[n];
            for (int i = 0; i < n; i++) { bestSlots[i] = -1; bestRanks[i] = float.MaxValue; }

            for (int i = 0; i < n; i++)
            {
                if (targets[i] == null || !targets[i].useOrchestratorAssignment) continue;
                for (int b = 0; b < active; b++)
                {
                    if (!waistAtSlot[b].HasValue) continue;
                    Vector3 w = waistAtSlot[b].Value;
                    if (maxJumpFromLastWaist > 0f && _hasLastWaist[i] &&
                        Vector3.Distance(w, _lastWaistWorld[i]) > maxJumpFromLastWaist) continue;
                    float rank = RankSlotForMapper(i, b, w);
                    if (rank < bestRanks[i]) { bestRanks[i] = rank; bestSlots[i] = b; }
                }
            }

            // Sort indices by rank ascending so best-anchored mapper goes first.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => bestRanks[a].CompareTo(bestRanks[b]));

            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                Brekel_Body_v3_DefaultMapper mapper = targets[i];
                if (mapper == null || !mapper.useOrchestratorAssignment) continue;

                // Re-evaluate against currently free slots (ignoring slots taken by higher-priority mappers).
                int   bestSlot = -1;
                float bestRank = float.MaxValue;
                for (int b = 0; b < active; b++)
                {
                    if (slotUsed[b] || !waistAtSlot[b].HasValue) continue;
                    Vector3 w = waistAtSlot[b].Value;
                    if (minSep > 0f && !SeparatedFromAssigned(i, w, assignment, waistAtSlot, minSep)) continue;
                    if (maxJumpFromLastWaist > 0f && _hasLastWaist[i] &&
                        Vector3.Distance(w, _lastWaistWorld[i]) > maxJumpFromLastWaist) continue;
                    float rank = RankSlotForMapper(i, b, w);
                    if (rank < bestRank) { bestRank = rank; bestSlot = b; }
                }

                int prevSlot = mapper.ActiveStreamBodyIndex;

                if (bestSlot >= 0)
                {
                    if (bestSlot != prevSlot)
                        LogSwitch(mapper, i, prevSlot, bestSlot, active, waistAtSlot, assignment, slotUsed, minSep);

                    assignment[i]      = bestSlot;
                    slotUsed[bestSlot] = true;
                    _lastWaistWorld[i] = waistAtSlot[bestSlot].Value;
                    _hasLastWaist[i]   = true;
                    mapper.SetAssignedBodySlot(bestSlot);
                }
                else
                {
                    if (prevSlot != -1)
                        LogSwitch(mapper, i, prevSlot, -1, active, waistAtSlot, assignment, slotUsed, minSep);

                    mapper.SetAssignedBodySlot(-1);
                }
            }
        }
    }

    private void LogSwitch(
        Brekel_Body_v3_DefaultMapper mapper,
        int mapperIndex,
        int fromSlot,
        int toSlot,
        int active,
        Vector3?[] waistAtSlot,
        int[] assignment,
        bool[] slotUsed,
        float minSep)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Switch] {mapper.gameObject.name}: Body {fromSlot} → {toSlot}  |  active={active}  |  hasLastWaist={_hasLastWaist[mapperIndex]}");

        sb.Append("  Active bodies this frame:");
        for (int b = 0; b < active; b++)
        {
            if (waistAtSlot[b].HasValue)
                sb.Append($" [{b}] waist={waistAtSlot[b].Value:F3}");
            else
                sb.Append($" [{b}] NO_FRAME");
        }
        sb.AppendLine();

        if (_hasLastWaist[mapperIndex])
            sb.AppendLine($"  {mapper.gameObject.name} last waist: {_lastWaistWorld[mapperIndex]:F3}");
        else
            sb.AppendLine($"  {mapper.gameObject.name} has no last waist (first assignment or was unassigned)");

        sb.AppendLine("  Per-body evaluation:");
        for (int b = 0; b < active; b++)
        {
            if (!waistAtSlot[b].HasValue) { sb.AppendLine($"    Body {b}: SKIP — no frame data"); continue; }
            if (slotUsed[b] && b != toSlot) { sb.AppendLine($"    Body {b}: SKIP — already used by earlier mapper"); continue; }

            Vector3 w = waistAtSlot[b].Value;

            if (!SeparatedFromAssigned(mapperIndex, w, assignment, waistAtSlot, minSep))
            {
                sb.AppendLine($"    Body {b}: REJECT — too close to another assigned player (minSep={minSep:F3}m)");
                continue;
            }

            if (maxJumpFromLastWaist > 0f && _hasLastWaist[mapperIndex])
            {
                float dist = Vector3.Distance(w, _lastWaistWorld[mapperIndex]);
                if (dist > maxJumpFromLastWaist)
                {
                    sb.AppendLine($"    Body {b}: REJECT — jumped {dist:F3}m (maxJump={maxJumpFromLastWaist:F3}m)");
                    continue;
                }
            }

            float rank = RankSlotForMapper(mapperIndex, b, w);
            string distStr = _hasLastWaist[mapperIndex]
                ? $"  dist-from-last={Vector3.Distance(w, _lastWaistWorld[mapperIndex]):F3}m"
                : "  (no anchor, rank by index)";
            sb.AppendLine($"    Body {b}: rank={rank:F4}{distStr}  {(b == toSlot ? "← CHOSEN" : "")}");
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>Prefer body nearest where this player was last frame; tie-break lower Brekel index (stable).</summary>
    private float RankSlotForMapper(int mapperIndex, int slotIndex, Vector3 waistWorld)
    {
        const float indexTieBreak = 0.0001f;
        if (_hasLastWaist[mapperIndex])
            return Vector3.Distance(waistWorld, _lastWaistWorld[mapperIndex]) + slotIndex * indexTieBreak;
        return slotIndex * indexTieBreak;
    }

    /// <summary>Waist must stay at least <paramref name="minSep"/> from every other mapper already assigned this frame.</summary>
    private static bool SeparatedFromAssigned(
        int mapperIndex,
        Vector3 candidateWaist,
        int[] assignment,
        Vector3?[] waistAtSlot,
        float minSep)
    {
        if (minSep <= 0f)
            return true;

        for (int j = 0; j < assignment.Length; j++)
        {
            if (j == mapperIndex) continue;
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
