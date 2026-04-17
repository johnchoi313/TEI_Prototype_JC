using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A problem object that players discover and resolve using their power-ups.
///
/// STATES
///   Idle   — undiscovered; minimap icon hidden.
///   Found  — FOV has illuminated it; CD turns yellow; minimap icon appears.
///   Fixed  — Fix power-up used nearby; CD slides into TV; screen turns green; scores a point; despawns.
///   Broken — Break power-up used nearby; all meshes scatter; no score; despawns.
///            On a dud problem, Break is the *correct* action — same scatter, no score, no penalty.
///            On a dud problem, Fix is the *wrong* action → TryDudPenalty: CD slides in, screen turns
///            red, score deducted by penaltyValue.
///
/// HOW FIX/BREAK DETECTION WORKS
///   PowerUpManager fires OnPowerUpActivated (static event) the instant a fist
///   gesture is detected. Every ProblemObject in the scene receives this event.
///   Each one checks:
///     1. Is my state Found?            (not already resolved)
///     2. Is the activating player within _interactionRadius world units?
///     3. Does the power-up role match Fix or Break?
///   If all three pass → TryFix or TryBreak.
///
/// PLAYER FREEZE
///   When Fix or Break triggers, the activating player's fish is redirected to
///   _standPosition (an empty child Transform placed in front of the TV) via
///   FishFOVController.SetOverrideTarget. The override is cleared after the
///   despawn delay so normal FOV-following resumes automatically.
///
/// VISUAL FEEDBACK
///   All color changes and animations are delegated to ProblemVisualController
///   (auto-fetched via GetComponent in OnAwake). SetMaterialColor from the base
///   class is never called — ProblemVisualController owns all renderer state.
///
/// PREFAB REQUIREMENTS
///   • Root must have at least one child Renderer (required by FOVHighlightable base).
///   • Assign a ProblemDefinition SO in the Inspector.
///   • Assign _standPosition (empty child Transform) in the Inspector.
///   • ProblemVisualController must also be on the root GO.
/// </summary>
public class ProblemObject : FOVHighlightable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Problem")]
    [SerializeField] private ProblemDefinition _definition;

    [Tooltip("World-unit radius within which a player's fist gesture will interact with this problem.")]
    [SerializeField] private float _interactionRadius = 3f;

    [Tooltip("Empty child Transform placed where the player should stand during the interaction animation. " +
             "If not assigned, the fish freezes at its current position when the power-up fires.")]
    [SerializeField] private Transform _standPosition;

    [Tooltip("Seconds the TV stays visible with the green screen after the CD slides in, before despawning.")]
    [SerializeField] private float _postAnimationHoldDuration = 2f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    public ProblemState      State      { get; private set; } = ProblemState.Idle;
    public ProblemDefinition Definition => _definition;

    private ProblemSpawnPocket      _spawnPocket;
    private ProblemVisualController _visuals;

    // The root BoxCollider that physically blocks the fish during normal play.
    // Disabled at the start of any interaction so the fish's dynamic Rigidbody
    // doesn't receive a PhysX depenetration impulse when rb.MovePosition fires
    // toward the stand position while the fish is overlapping the TV.
    // Never re-enabled — the problem despawns after every interaction.
    private Collider _physicsCollider;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when a player breaks this problem. Used by ResearchDataLogger.</summary>
    public static event Action<PlayerIndex> OnProblemBroken;

    // ── FOVHighlightable overrides ────────────────────────────────────────────

    protected override void OnAwake()
    {
        _visuals           = GetComponent<ProblemVisualController>();
        _physicsCollider   = GetComponent<Collider>();
        _visuals?.Refresh(ProblemState.Idle);
    }

    protected override void OnEnterFOVRange(Vector3 fovCenter)
    {
        if (State == ProblemState.Idle)
            TransitionTo(ProblemState.Found);
    }

    /// <summary>Problems do not move toward the FOV — no-op.</summary>
    protected override void ApplyMovement(Vector3 target) { }

    /// <summary>Found persists after FOV leaves — no-op.</summary>
    protected override void OnExitFOVRange() { }

    // ── Power-up interaction ──────────────────────────────────────────────────

    private void OnEnable()  => PowerUpManager.OnPowerUpActivated += HandlePowerUpActivated;
    private void OnDisable() => PowerUpManager.OnPowerUpActivated -= HandlePowerUpActivated;

    private void HandlePowerUpActivated(PlayerIndex player, PowerUpDefinition def)
    {
        if (State != ProblemState.Found) return;
        if (def.role == PowerUpRole.None) return;

        // Direct distance check — no trigger colliders needed.
        // Use PowerUpManager.GetFish — same reference as the one that received Apply(),
        // guaranteed correct even if PlayerManager scene refs are stale after prefab rebuilds.
        GameObject playerGO = PowerUpManager.Instance?.GetFish(player);
        if (playerGO == null) return;

        float dist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.y),
            new Vector2(playerGO.transform.position.x, playerGO.transform.position.y));

        if (dist > _interactionRadius) return;

        bool isDud = _definition != null && _definition.isDud;

        if (def.role == PowerUpRole.Fix)
        {
            if (isDud) TryDudPenalty(player);
            else       TryFix(player);
        }
        else if (def.role == PowerUpRole.Break)
        {
            TryBreak(player); // same for both normal and dud — scatter, no score
        }
    }

    private void TryFix(PlayerIndex player)
    {
        DisablePhysicsCollider(); // must be first — stops depenetration before rb.MovePosition fires
        TransitionTo(ProblemState.Fixed); // UpdateVisuals → _visuals.Refresh(Fixed) → CD green
        ScoreManager.Instance?.AddScore(player, _definition != null ? _definition.pointValue : 1);
        SoundManager.Instance?.PlayAt(_definition?.fixedSound, transform.position);
        FreezePlayerAt(player);
        StartCoroutine(FixSequence(player));
    }

    private IEnumerator FixSequence(PlayerIndex player)
    {
        // Wait for the visual animation (CD slide + screen color change) to complete,
        // or fall back to despawnDelay if no visual controller is wired.
        if (_visuals != null)
            yield return _visuals.PlayFixAnimation();
        else
            yield return new WaitForSeconds(_definition != null ? _definition.despawnDelay : 3f);

        // Hold on the green-screen TV so the player can see the resolved state before it disappears.
        yield return new WaitForSeconds(_postAnimationHoldDuration);

        UnfreezePlayer(player);
        ProblemManager.Instance?.OnProblemDespawned(this, _spawnPocket);
        Destroy(gameObject);
    }

    private void TryBreak(PlayerIndex player)
    {
        DisablePhysicsCollider(); // must be first — stops depenetration before rb.MovePosition fires
        TransitionTo(ProblemState.Broken); // UpdateVisuals → Refresh(Broken) → CD red
        OnProblemBroken?.Invoke(player);
        SoundManager.Instance?.PlayAt(_definition?.brokenSound, transform.position);

        Vector3 blastOrigin = PowerUpManager.Instance?.GetFish(player)?.transform.position
                              ?? transform.position;
        FreezePlayerAt(player);
        _visuals?.PlayBreakAnimation(blastOrigin);

        StartCoroutine(BreakSequence(player));
    }

    private IEnumerator BreakSequence(PlayerIndex player)
    {
        yield return new WaitForSeconds(_definition != null ? _definition.despawnDelay : 3f);
        UnfreezePlayer(player);
        ProblemManager.Instance?.OnProblemDespawned(this, _spawnPocket);
        Destroy(gameObject);
    }

    private void TryDudPenalty(PlayerIndex player)
    {
        // Wrong action: Fix on a dud. CD slides in, screen turns red, score deducted.
        DisablePhysicsCollider();
        TransitionTo(ProblemState.Broken); // Refresh(Broken) → CD turns red
        ScoreManager.Instance?.SubtractScore(player, _definition != null ? _definition.penaltyValue : 1);
        SoundManager.Instance?.PlayAt(_definition?.brokenSound, transform.position);
        FreezePlayerAt(player);
        StartCoroutine(MalfunctionSequence(player));
    }

    private IEnumerator MalfunctionSequence(PlayerIndex player)
    {
        if (_visuals != null)
            yield return _visuals.PlayMalfunctionAnimation();
        else
            yield return new WaitForSeconds(_definition != null ? _definition.despawnDelay : 3f);

        // Hold on the red-screen TV so the player can see the failure state before it disappears.
        yield return new WaitForSeconds(_postAnimationHoldDuration);

        UnfreezePlayer(player);
        ProblemManager.Instance?.OnProblemDespawned(this, _spawnPocket);
        Destroy(gameObject);
    }

    // ── Collider management ───────────────────────────────────────────────────

    /// <summary>
    /// Disables the root BoxCollider so the fish's dynamic Rigidbody stops generating
    /// PhysX depenetration impulses against the TV when rb.MovePosition fires.
    /// Called at the very start of TryFix / TryBreak, before FreezePlayerAt.
    /// Never re-enabled — the problem always despawns after interaction.
    /// </summary>
    private void DisablePhysicsCollider()
    {
        if (_physicsCollider != null)
            _physicsCollider.enabled = false;
    }

    // ── Player freeze helpers ─────────────────────────────────────────────────

    private void FreezePlayerAt(PlayerIndex player)
    {
        GameObject fish = PowerUpManager.Instance?.GetFish(player);
        if (fish == null) return;

        // FOVHighlightable is guaranteed on every fish root — stops the fish following the FOV.
        FOVHighlightable fovCtrl = fish.GetComponent<FOVHighlightable>();
        fovCtrl?.LockMovement();

        // FishFOVController is optional — provides smooth movement to the stand position.
        // If not present (fish uses base FOVHighlightable directly), the lock above is enough.
        FishFOVController fishCtrl = fish.GetComponent<FishFOVController>()
                                  ?? fish.GetComponentInChildren<FishFOVController>();
        if (fishCtrl != null)
        {
            Vector3 target = _standPosition != null ? _standPosition.position : fish.transform.position;
            fishCtrl.SetOverrideTarget(target);
        }
    }

    private void UnfreezePlayer(PlayerIndex player)
    {
        GameObject fish = PowerUpManager.Instance?.GetFish(player);
        if (fish == null) return;

        fish.GetComponent<FOVHighlightable>()?.UnlockMovement();

        FishFOVController fishCtrl = fish.GetComponent<FishFOVController>()
                                  ?? fish.GetComponentInChildren<FishFOVController>();
        fishCtrl?.ClearMovementOverride();
    }

    // ── Registration ──────────────────────────────────────────────────────────

    public void SetSpawnPocket(ProblemSpawnPocket pocket) => _spawnPocket = pocket;

    // ── Internal ──────────────────────────────────────────────────────────────

    private void TransitionTo(ProblemState next)
    {
        State = next;
        UpdateVisuals();
        Debug.Log($"[ProblemObject] {name}: {next}");
    }

    private void UpdateVisuals()
    {
        _visuals?.Refresh(State);

        if (_definition == null) return;

        // Found sound plays here; fix/break sounds play in TryFix/TryBreak.
        if (State == ProblemState.Found)
            SoundManager.Instance?.PlayAt(_definition.foundSound, transform.position);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _interactionRadius);
    }
#endif
}
