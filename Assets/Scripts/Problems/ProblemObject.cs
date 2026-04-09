using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A problem object that players discover and resolve using their power-ups.
///
/// STATES
///   Idle   — undiscovered; dimly lit; no minimap icon.
///   Found  — FOV has illuminated it; full color; minimap icon appears.
///   Fixed  — Fix power-up used nearby; scores a point; despawns after delay.
///   Broken — Break power-up used nearby; no score; despawns after delay.
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
///   No trigger colliders or layer-matrix setup needed. The radius check
///   happens at the exact moment of activation, which is also the most
///   physically intuitive feel for the player.
///
/// PREFAB REQUIREMENTS
///   • Root must have a Renderer (required by FOVHighlightable).
///   • Assign a ProblemDefinition SO in the Inspector.
/// </summary>
public class ProblemObject : FOVHighlightable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Problem")]
    [SerializeField] private ProblemDefinition _definition;

    [Tooltip("World-unit radius within which a player's fist gesture will interact with this problem.")]
    [SerializeField] private float _interactionRadius = 3f;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource _audioSource;

    // ── Runtime state ─────────────────────────────────────────────────────────

    public ProblemState      State      { get; private set; } = ProblemState.Idle;
    public ProblemDefinition Definition => _definition;

    private ProblemSpawnPocket _spawnPocket;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when a player breaks this problem. Used by ResearchDataLogger.</summary>
    public static event Action<PlayerIndex> OnProblemBroken;

    // ── FOVHighlightable overrides ────────────────────────────────────────────

    protected override void OnAwake()
    {
        if (_definition != null)
            SetMaterialColor(_definition.idleColor);
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

        if (def.role == PowerUpRole.Fix)   TryFix(player);
        else if (def.role == PowerUpRole.Break) TryBreak(player);
    }

    private void TryFix(PlayerIndex player)
    {
        TransitionTo(ProblemState.Fixed);
        ScoreManager.Instance?.AddScore(player, _definition != null ? _definition.pointValue : 1);
        PlaySound(_definition?.fixedSound);
        StartCoroutine(DespawnAfterDelay(_definition != null ? _definition.despawnDelay : 3f));
    }

    private void TryBreak(PlayerIndex player)
    {
        TransitionTo(ProblemState.Broken);
        OnProblemBroken?.Invoke(player);
        PlaySound(_definition?.brokenSound);
        StartCoroutine(DespawnAfterDelay(_definition != null ? _definition.despawnDelay : 3f));
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
        if (_definition == null) return;

        switch (State)
        {
            case ProblemState.Idle:
                SetMaterialColor(_definition.idleColor);
                break;
            case ProblemState.Found:
                SetMaterialColor(_definition.foundColor);
                PlaySound(_definition.foundSound);
                break;
            case ProblemState.Fixed:
                SetMaterialColor(_definition.minimapFixedColor);
                break;
            case ProblemState.Broken:
                SetMaterialColor(_definition.minimapBrokenColor);
                break;
        }
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ProblemManager.Instance?.OnProblemDespawned(this, _spawnPocket);
        Destroy(gameObject);
    }

    private void PlaySound(AudioClip clip)
    {
        if (_audioSource != null && clip != null)
            _audioSource.PlayOneShot(clip);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _interactionRadius);
    }
#endif
}
