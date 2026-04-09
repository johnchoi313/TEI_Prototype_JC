using System;
using UnityEngine;

/// <summary>
/// Central manager for the two-player power-up system.
///
/// Fist (one-shot) → activates the player's current power-up for activeDuration seconds.
/// Hands together (hold) → swaps which power-up each player owns.
///
/// Agnostic to power-up type — calls PowerUpDefinition.Apply/Remove with the fish
/// GameObject and lets the concrete SO subclass handle the effect.
/// </summary>
public class PowerUpManager : MonoBehaviour
{
    public static PowerUpManager Instance { get; private set; }

    [Header("Shader")]
    [SerializeField] private TEIHandTrackingShaderBridge shaderBridge;

    [Header("Input")]
    [SerializeField] private TEIHandGestureInterpreter gestures;

    [Header("Fish GameObjects")]
    [SerializeField] private GameObject fish1;
    [SerializeField] private GameObject fish2;

    [Header("Power-Up Assignments")]
    [SerializeField] private PowerUpDefinition p1PowerUp;
    [SerializeField] private PowerUpDefinition p2PowerUp;

    [Header("Swap Settings")]
    [Tooltip("Canvas UV-space proximity threshold (0-1, aspect-ratio corrected) for swap detection. In split mode, hands meet at x=0.5; tune until the interaction feel matches together mode.")]
    [SerializeField] private float swapProximityThreshold = 0.35f;
    [Tooltip("How long hands must stay close to trigger a swap (seconds).")]
    [SerializeField] private float swapHoldDuration = 0.75f;
    [Tooltip("Lockout after a swap fires to prevent immediately re-trigger (seconds).")]
    [SerializeField] private float swapCooldownDuration = 1.5f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired the moment a player's power-up activates (fist gesture).
    /// ProblemObject subscribes to this to handle Fix/Break interactions.
    /// </summary>
    public static event Action<PlayerIndex, PowerUpDefinition> OnPowerUpActivated;

    /// <summary>Fired when players complete a power-up swap. Used by ResearchDataLogger.</summary>
    public static event Action OnPowerUpSwapped;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool  p1Active;
    private bool  p2Active;
    private float p1Timer;
    private float p2Timer;
    private float swapHoldProgress;
    private float swapCooldown;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        PlayerManager.OnPlayersSpawned += HandlePlayersSpawned;
    }

    private void OnDisable()
    {
        PlayerManager.OnPlayersSpawned -= HandlePlayersSpawned;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Re-caches fish references when PlayerManager spawns (or re-spawns) characters.
    /// In prototype mode this fires once at Start with the scene-placed fish.
    /// In prefab mode this fires after each level load.
    /// </summary>
    private void HandlePlayersSpawned(GameObject p1, GameObject p2)
    {
        if (p1 != null) fish1 = p1;
        if (p2 != null) fish2 = p2;
        PushOutlineColors();
        Debug.Log("[PowerUpManager] Fish references updated.");
    }

    private void Start()
    {
        // Called after all Awake()s — shaderBridge.runtimeMaterial is guaranteed to exist by now.
        PushOutlineColors();
    }

    private void Update()
    {
        // Power-ups are only active during gameplay. Activations and swaps are blocked
        // in PreGame, Rules, MainMenu, and LevelComplete.
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing)
            return;

        TickActivation();
        TickSwap();
        if (shaderBridge != null)
            shaderBridge.SetMergeProgress(swapHoldProgress / swapHoldDuration);
    }

    // ── Activation ────────────────────────────────────────────────────────────

    private void TickActivation()
    {
        float dt = Time.deltaTime;

        if (gestures != null && gestures.LeftFistDown  && !p1Active) Activate(ref p1Active, ref p1Timer, p1PowerUp, fish1);
        if (gestures != null && gestures.RightFistDown && !p2Active) Activate(ref p2Active, ref p2Timer, p2PowerUp, fish2);

        if (p1Active) { p1Timer -= dt; if (p1Timer <= 0f) Deactivate(ref p1Active, p1PowerUp, fish1); }
        if (p2Active) { p2Timer -= dt; if (p2Timer <= 0f) Deactivate(ref p2Active, p2PowerUp, fish2); }
    }

    private void Activate(ref bool active, ref float timer, PowerUpDefinition def, GameObject fish)
    {
        if (def == null || fish == null) return;
        active = true;
        timer  = def.activeDuration;
        def.Apply(fish);
        PlayerIndex who = ReferenceEquals(fish, fish1) ? PlayerIndex.Player1 : PlayerIndex.Player2;
        OnPowerUpActivated?.Invoke(who, def);
    }

    private void Deactivate(ref bool active, PowerUpDefinition def, GameObject fish)
    {
        active = false;
        def?.Remove(fish);
    }

    // ── Swap ──────────────────────────────────────────────────────────────────

    private void TickSwap()
    {
        swapCooldown = Mathf.Max(0f, swapCooldown - Time.deltaTime);

        if (FOVWorldCollider.Instance == null) return;

        FOVWorldCollider.HandWorldState left  = FOVWorldCollider.Instance.LeftHand;
        FOVWorldCollider.HandWorldState right = FOVWorldCollider.Instance.RightHand;

        // Positions are compared in shared canvas UV space — _Hand1Pos/_Hand2Pos exactly.
        // Both positions are always defined regardless of active/ghost state.
        //
        // Ghost guard nuance (split mode only):
        //   "Truly absent" ghost  — MediaPipe lost the hand; canvas x is still on home side
        //                           (left ≤ 0.5, right ≥ 0.5). Block only if BOTH are this.
        //   "Boundary ghost"      — Hand crossed the split line; FOVWorldCollider bounds-rejects
        //                           it (IsGhost=true) but shader bridge keeps tracking the real
        //                           filter position. Canvas x has already passed 0.5. Treat as real.
        //
        // For a truly absent ghost, its home (x=0.25 or x=0.75) is unreachable from the other
        // half. Pin it to x=0.5 while the other hand is still in its own half. Once the other
        // hand has crossed too, use the ghost's actual home as the approach target.
        bool handsClose = false;
        if (left.IsActive && right.IsActive && TEIHandTrackingShaderBridge.Instance != null)
        {
            Vector2 leftCanvas  = TEIHandTrackingShaderBridge.Instance.CurrentLeftPosCanvas;
            Vector2 rightCanvas = TEIHandTrackingShaderBridge.Instance.CurrentRightPosCanvas;

            bool inSplit = SplitScreenController.Instance != null &&
                           SplitScreenController.Instance.CurrentState == SplitScreenController.SplitState.SplitScreen;

            bool skipCheck;
            if (inSplit)
            {
                bool leftTrulyAbsent  = left.IsGhost  && leftCanvas.x  <= 0.5f;
                bool rightTrulyAbsent = right.IsGhost && rightCanvas.x >= 0.5f;
                skipCheck = leftTrulyAbsent && rightTrulyAbsent;
                if (!skipCheck)
                {
                    if (leftTrulyAbsent  && rightCanvas.x >= 0.5f) { leftCanvas.x  = 0.5f; leftCanvas.y  = rightCanvas.y; }
                    if (rightTrulyAbsent && leftCanvas.x  <= 0.5f) { rightCanvas.x = 0.5f; rightCanvas.y =  leftCanvas.y; }
                }
            }
            else
            {
                skipCheck = left.IsGhost && right.IsGhost;
            }

            if (!skipCheck)
            {
                Vector2 delta = leftCanvas - rightCanvas;
                delta.x *= (float)Screen.width / Screen.height;
                handsClose = delta.magnitude < swapProximityThreshold;
            }
        }

        if (handsClose && swapCooldown <= 0f)
        {
            swapHoldProgress += Time.deltaTime;
            if (swapHoldProgress >= swapHoldDuration)
                ExecuteSwap();
        }
        else
        {
            swapHoldProgress = 0f;
        }
    }

    private void ExecuteSwap()
    {
        if (p1Active && p1PowerUp != null) p1PowerUp.Remove(fish1);
        if (p2Active && p2PowerUp != null) p2PowerUp.Remove(fish2);

        (p1PowerUp, p2PowerUp) = (p2PowerUp, p1PowerUp);

        if (p1Active && p1PowerUp != null) p1PowerUp.Apply(fish1);
        if (p2Active && p2PowerUp != null) p2PowerUp.Apply(fish2);

        PushOutlineColors();
        OnPowerUpSwapped?.Invoke();

        swapHoldProgress = 0f;
        swapCooldown     = swapCooldownDuration;
    }

    // ── Shader ────────────────────────────────────────────────────────────────

    private void PushOutlineColors()
    {
        if (shaderBridge == null) return;
        shaderBridge.SetOutlineColor(isLeft: true,  p1PowerUp != null ? p1PowerUp.outlineColor : Color.white);
        shaderBridge.SetOutlineColor(isLeft: false, p2PowerUp != null ? p2PowerUp.outlineColor : Color.white);
    }

    // ── Public read-only ──────────────────────────────────────────────────────

    public bool  P1Active         => p1Active;
    public bool  P2Active         => p2Active;
    public float P1TimeRemaining  => p1Timer;
    public float P2TimeRemaining  => p2Timer;
    public float SwapHoldProgress => swapHoldProgress;
    public float SwapHoldDuration => swapHoldDuration;

    /// <summary>Returns the currently equipped power-up definition for the given player.</summary>
    public PowerUpDefinition GetActiveDefinition(PlayerIndex player)
        => player == PlayerIndex.Player1 ? p1PowerUp : p2PowerUp;

    /// <summary>Current power-up outline color for Player 1 (white when none assigned).</summary>
    public Color P1PowerUpColor => p1PowerUp != null ? p1PowerUp.outlineColor : Color.white;

    /// <summary>Current power-up outline color for Player 2 (white when none assigned).</summary>
    public Color P2PowerUpColor => p2PowerUp != null ? p2PowerUp.outlineColor : Color.white;

    /// <summary>
    /// Returns the fish GameObject for the given player index.
    /// This is the same reference used by Activate() — guaranteed correct
    /// even after prefab rebuilds that may have invalidated PlayerManager's
    /// scene-reference fields. Use this for distance checks in power-up handlers.
    /// </summary>
    public GameObject GetFish(PlayerIndex index)
        => index == PlayerIndex.Player1 ? fish1 : fish2;
}
