using UnityEngine;

/// <summary>
/// Runs before <see cref="Brekel_Body_v3_DefaultMapper"/> (default execution order -100) and assigns each
/// mapped avatar a unique Brekel stream body slot. Uses last-known waist position to stay stable when the
/// tracker reorders body IDs, with grace timers and optional anchor reset after long absence.
/// Each target must have <see cref="Brekel_Body_v3_DefaultMapper.useOrchestratorAssignment"/> enabled.
/// </summary>
[DefaultExecutionOrder(-100)]
public class BrekelBodyAssignmentOrchestrator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("TCP receiver that owns the latest Brekel body frames.")]
    public Brekel_Body_v3_Receiver receiver;

    [Tooltip("Ordered list (e.g. Player 1, Player 2). Earlier entries win ties when choosing slots.")]
    public Brekel_Body_v3_DefaultMapper[] targets;

    [Header("Assignment")]
    [Tooltip("Maximum distance (stream waist position) from last-known waist for a candidate slot to be accepted.")]
    public float maxAssignmentDistance = 1.5f;

    [Tooltip("Seconds without a new timestamp on the current slot before grace ends and search may begin.")]
    public float searchAfterStaleSeconds = 0.5f;

    [Tooltip("Seconds fully unassigned before discarding last-known waist so any available slot can be picked.")]
    public float positionResetSeconds = 5f;

    private struct TargetState
    {
        public int     AssignedSlot;
        public float   StaleTimer;
        public float   SearchingTimer;
        public Vector3 LastWaist;
        public bool    HasLastWaist;
    }

    private TargetState[] _states;
    private float[]       _slotTimestampCache;
    private bool[]        _slotActive;
    private bool[]        _reservedSlots;

    private void Awake()
    {
        int n = targets != null ? targets.Length : 0;
        _states = new TargetState[n];
        for (int i = 0; i < n; i++)
            _states[i].AssignedSlot = -1;

        int m = Brekel_Body_v3_Receiver.MaxBodies;
        _slotTimestampCache = new float[m];
        for (int i = 0; i < m; i++)
            _slotTimestampCache[i] = float.MinValue;

        _slotActive = new bool[m];
        _reservedSlots = new bool[m];
    }

    private void Start()
    {
        if (targets == null)
            return;
        for (int i = 0; i < targets.Length; i++)
        {
            Brekel_Body_v3_DefaultMapper m = targets[i];
            if (m != null && !m.useOrchestratorAssignment)
                Debug.LogWarning(
                    $"[BrekelBodyAssignmentOrchestrator] '{m.gameObject.name}' is in the targets list but " +
                    $"Use Orchestrator Assignment is disabled on Brekel_Body_v3_DefaultMapper — it will be skipped.",
                    m);
        }
    }

    private void Update()
    {
        if (receiver == null || !receiver.IsConnected)
        {
            ClearAssignments();
            return;
        }

        RefreshSlotActivity();

        for (int i = 0; i < _reservedSlots.Length; i++)
            _reservedSlots[i] = false;

        if (targets == null)
            return;

        for (int t = 0; t < targets.Length; t++)
        {
            Brekel_Body_v3_DefaultMapper mapper = targets[t];
            if (mapper == null || !mapper.useOrchestratorAssignment)
                continue;

            int slot = ResolveSlot(t);
            mapper.SetAssignedBodySlot(slot);
            if (slot >= 0)
                _reservedSlots[slot] = true;
        }
    }

    private void OnDisable()
    {
        ClearAssignments();
    }

    private void ClearAssignments()
    {
        if (targets == null)
            return;
        foreach (Brekel_Body_v3_DefaultMapper m in targets)
        {
            if (m != null && m.useOrchestratorAssignment)
                m.SetAssignedBodySlot(-1);
        }

        if (_states != null)
        {
            for (int i = 0; i < _states.Length; i++)
                _states[i].AssignedSlot = -1;
        }
    }

    /// <summary>
    /// Marks slots whose frame timestamp changed since last frame as active for this frame only.
    /// </summary>
    private void RefreshSlotActivity()
    {
        for (int b = 0; b < Brekel_Body_v3_Receiver.MaxBodies; b++)
        {
            BrekelBodyFrame f = receiver.GetBody(b);
            if (f == null)
            {
                _slotActive[b] = false;
                continue;
            }

            bool changed = !Mathf.Approximately(f.timestamp, _slotTimestampCache[b]);
            _slotActive[b] = changed;
            if (changed)
                _slotTimestampCache[b] = f.timestamp;
        }
    }

    private int ResolveSlot(int targetIndex)
    {
        ref TargetState state = ref _states[targetIndex];

        if (state.AssignedSlot >= 0)
        {
            int s = state.AssignedSlot;

            if (_slotActive[s])
            {
                BrekelBodyFrame held = receiver.GetBody(s);
                if (held != null)
                {
                    state.StaleTimer = 0f;
                    state.LastWaist = held.joints[(int)Brekel_joint_name_v3.waist].position;
                    state.HasLastWaist = true;
                }
                return s;
            }

            state.StaleTimer += Time.deltaTime;
            if (state.StaleTimer < searchAfterStaleSeconds)
                return s;

            Debug.Log(
                $"[BrekelBodyAssignmentOrchestrator] Target index {targetIndex} lost slot {s} " +
                $"after {state.StaleTimer:F1}s — releasing.",
                this);
            state.AssignedSlot = -1;
            state.SearchingTimer = 0f;
        }

        state.SearchingTimer += Time.deltaTime;
        if (state.HasLastWaist && state.SearchingTimer >= positionResetSeconds)
        {
            state.HasLastWaist = false;
            Debug.Log(
                $"[BrekelBodyAssignmentOrchestrator] Target index {targetIndex} anchor expired " +
                $"({state.SearchingTimer:F1}s unassigned).",
                this);
        }

        int   bestSlot = -1;
        float bestDist = state.HasLastWaist ? maxAssignmentDistance : float.MaxValue;

        for (int b = 0; b < Brekel_Body_v3_Receiver.MaxBodies; b++)
        {
            if (!_slotActive[b])
                continue;
            if (_reservedSlots[b])
                continue;

            BrekelBodyFrame candidate = receiver.GetBody(b);
            if (candidate == null)
                continue;

            float dist = state.HasLastWaist
                ? Vector3.Distance(
                    candidate.joints[(int)Brekel_joint_name_v3.waist].position,
                    state.LastWaist)
                : 0f;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestSlot = b;
            }
        }

        if (bestSlot >= 0)
        {
            state.AssignedSlot = bestSlot;
            state.StaleTimer = 0f;
            state.SearchingTimer = 0f;

            BrekelBodyFrame newBody = receiver.GetBody(bestSlot);
            if (newBody != null)
            {
                state.LastWaist = newBody.joints[(int)Brekel_joint_name_v3.waist].position;
                state.HasLastWaist = true;
            }

            Debug.Log(
                $"[BrekelBodyAssignmentOrchestrator] Target index {targetIndex} claimed slot {bestSlot} " +
                $"(dist={bestDist:F2}m).",
                this);
            return bestSlot;
        }

        return -1;
    }
}
