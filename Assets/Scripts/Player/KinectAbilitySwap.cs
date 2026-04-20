using UnityEngine;

/// <summary>
/// Swaps fish abilities between two players when their Kinect-tracked bodies
/// come within touch range of each other.
///
/// TRIGGER  — when the distance between Player 1 and Player 2's chest bones
///            drops below TouchDistance, abilities are swapped (same swap as Tab).
///
/// COOLDOWN — the swap is locked out until the players move APART again past
///            SeparateDistance. This prevents repeated rapid swaps while they
///            remain close. Only after separating can the next touch trigger
///            another swap.
///
/// SCENE SETUP
///   1. Create an empty GameObject (e.g. "KinectAbilitySwap") in the scene.
///   2. Attach this component.
///   3. Assign Kinect1 and Kinect2 (the two KinectPlayerController components).
///   4. Assign Player1Ability and Player2Ability (the two FishAbility components).
///   5. Assign Player1Renderer, Player2Renderer, BreakWallMaterial,
///      CollectStationMaterial to update visuals on swap (same objects as Hotkeys).
///   6. Tune TouchDistance and SeparateDistance to match your physical space.
/// </summary>
public class KinectAbilitySwap : MonoBehaviour
{
    [Header("Kinect Controllers")]
    [Tooltip("KinectPlayerController for Player 1.")]
    [SerializeField] private KinectPlayerController _kinect1;

    [Tooltip("KinectPlayerController for Player 2.")]
    [SerializeField] private KinectPlayerController _kinect2;

    [Header("Fish Abilities")]
    [Tooltip("FishAbility component on Player 1's fish.")]
    [SerializeField] private FishAbility _player1Ability;

    [Tooltip("FishAbility component on Player 2's fish.")]
    [SerializeField] private FishAbility _player2Ability;

    [Header("Proximity Thresholds")]
    [Tooltip("World-space distance (metres) between chests that triggers the swap.")]
    [SerializeField] private float _touchDistance = 0.6f;

    [Tooltip("Players must separate beyond this distance before the next swap can trigger. " +
             "Should be larger than TouchDistance to create a clear hysteresis gap.")]
    [SerializeField] private float _separateDistance = 1.2f;

    [Header("Visual Feedback")]
    [Tooltip("Renderer on Player 1's indicator object — material updated on swap.")]
    [SerializeField] private Renderer _player1Renderer;

    [Tooltip("Renderer on Player 2's indicator object — material updated on swap.")]
    [SerializeField] private Renderer _player2Renderer;

    [Tooltip("Material representing the Break Wall ability.")]
    [SerializeField] private Material _breakWallMaterial;

    [Tooltip("Material representing the Collect Station ability.")]
    [SerializeField] private Material _collectStationMaterial;

    // ── Runtime ───────────────────────────────────────────────────────────────

    // True once a swap has fired this "touch event" — cleared when players separate.
    private bool _swapArmed = true; // start true so first touch fires immediately

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        ApplyMaterials();
    }

    private void Update()
    {
        if (_kinect1 == null || _kinect2 == null) return;
        if (_player1Ability == null || _player2Ability == null) return;

        Transform chest1 = _kinect1.chest;
        Transform chest2 = _kinect2.chest;
        if (chest1 == null || chest2 == null) return;

        float dist = Vector3.Distance(chest1.position, chest2.position);

        if (_swapArmed && dist <= _touchDistance)
        {
            DoSwap();
            _swapArmed = false; // lock until they separate
        }
        else if (!_swapArmed && dist >= _separateDistance)
        {
            _swapArmed = true; // players separated — ready for next touch
        }
    }

    // ── Swap logic ────────────────────────────────────────────────────────────

    private void DoSwap()
    {
        FishAbility.AbilityType p1 = _player1Ability.CurrentAbilityType;
        _player1Ability.CurrentAbilityType = _player2Ability.CurrentAbilityType;
        _player2Ability.CurrentAbilityType = p1;

        ApplyMaterials();
        ScoreTracker.Instance?.AddSwap();
        ScreenFlash.Instance?.Flash();

        Debug.Log($"[KinectAbilitySwap] Abilities swapped — " +
                  $"P1: {_player1Ability.CurrentAbilityType}, " +
                  $"P2: {_player2Ability.CurrentAbilityType}");
    }

    private void ApplyMaterials()
    {
        if (_player1Ability != null)
            ApplyMaterial(_player1Renderer, _player1Ability.CurrentAbilityType);
        if (_player2Ability != null)
            ApplyMaterial(_player2Renderer, _player2Ability.CurrentAbilityType);
    }

    private void ApplyMaterial(Renderer rend, FishAbility.AbilityType abilityType)
    {
        if (rend == null) return;
        Material mat = abilityType == FishAbility.AbilityType.BreakWall
            ? _breakWallMaterial
            : _collectStationMaterial;
        if (mat != null)
            rend.sharedMaterial = mat;
    }
}
