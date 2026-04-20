using UnityEngine;

/// <summary>
/// Gives a fish one of two abilities: break walls or collect stations.
///
/// INPUT — mirrors PlayerLightController's control scheme so no extra wiring
/// is needed:
///   Player1_WASD      → E key
///   Player2_ArrowKeys → Return key
///   Kinect            → jump (KinectPlayerController.JumpPressed)
///
/// When activated, a transparent sphere grows out from the fish, fades away,
/// and at peak radius destroys all BreakableWall or Station objects within
/// range (depending on AbilityType). Each destruction increments ScoreTracker.
///
/// SCENE SETUP
///   1. Attach to the same GameObject as PlayerFishController.
///   2. Set ControlScheme to match the paired PlayerLightController.
///   3. If using Kinect, assign the same KinectPlayerController as the light.
///   4. Set AbilityType (BreakWall or CollectStation).
///   5. Assign EffectMaterial (transparent/unlit recommended).
///   6. Tune ExplosionRadius, GrowDuration, FadeDuration, and Cooldown.
/// </summary>
public class FishAbility : MonoBehaviour
{
    // ── Types ─────────────────────────────────────────────────────────────────

    public enum AbilityType { BreakWall, CollectStation }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Control Scheme")]
    [Tooltip("Must match the paired PlayerLightController's control scheme.")]
    [SerializeField] private PlayerLightController.ControlScheme _controlScheme
        = PlayerLightController.ControlScheme.Player1_WASD;

    [Tooltip("Required when ControlScheme is Kinect. Assign the same KinectPlayerController " +
             "used by the paired PlayerLightController.")]
    [SerializeField] private KinectPlayerController _kinectController;

    /// <summary>Get or set the active control scheme at runtime (called by Hotkeys on Shift+C).</summary>
    public PlayerLightController.ControlScheme ControlScheme
    {
        get => _controlScheme;
        set => _controlScheme = value;
    }

    [Header("Ability")]
    [Tooltip("BreakWall destroys BreakableWall segments within range. " +
             "CollectStation collects Station spheres within range.")]
    [SerializeField] private AbilityType _abilityType = AbilityType.BreakWall;

    /// <summary>Get or set the current ability type at runtime (e.g. swapped by Hotkeys on Tab).</summary>
    public AbilityType CurrentAbilityType
    {
        get => _abilityType;
        set => _abilityType = value;
    }

    [Tooltip("World-unit radius of the explosion at full size.")]
    [SerializeField] private float _explosionRadius = 2.5f;

    [Header("Effect")]
    [Tooltip("Material applied to the explosion sphere. Use a transparent/unlit shader.")]
    [SerializeField] private Material _effectMaterial;

    [Tooltip("Seconds for the sphere to grow from zero to full radius.")]
    [SerializeField] private float _growDuration = 0.25f;

    [Tooltip("Seconds for the sphere to fade out after reaching full size.")]
    [SerializeField] private float _fadeDuration = 0.35f;

    [Header("Cooldown")]
    [Tooltip("Minimum seconds between ability activations.")]
    [SerializeField] private float _cooldown = 1f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private float      _cooldownTimer;
    private bool       _animating;
    private GameObject _effectSphere;
    private Renderer   _effectRenderer;
    private float      _animTime;
    private bool       _effectFired;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        _cooldownTimer -= Time.deltaTime;

        if (!_animating && _cooldownTimer <= 0f && ReadAbilityInput())
            StartAbility();

        if (_animating)
            TickExplosion();
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private bool ReadAbilityInput()
    {
        switch (_controlScheme)
        {
            case PlayerLightController.ControlScheme.Player1_WASD:
                return Input.GetKeyDown(KeyCode.E);

            case PlayerLightController.ControlScheme.Player2_ArrowKeys:
                return Input.GetKeyDown(KeyCode.Return);

            case PlayerLightController.ControlScheme.Kinect:
                return _kinectController != null && _kinectController.JumpPressed;

            default:
                return false;
        }
    }

    // ── Ability flow ──────────────────────────────────────────────────────────

    private void StartAbility()
    {
        _effectFired   = false;
        _animTime      = 0f;
        _cooldownTimer = _cooldown;

        _effectSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _effectSphere.name = "AbilityEffect";
        Destroy(_effectSphere.GetComponent<Collider>());

        _effectSphere.transform.position   = transform.position;
        _effectSphere.transform.localScale = Vector3.zero;

        _effectRenderer = _effectSphere.GetComponent<Renderer>();
        if (_effectMaterial != null)
            _effectRenderer.material = Object.Instantiate(_effectMaterial);

        SetAlpha(1f);
        _animating = true;
    }

    private void TickExplosion()
    {
        _animTime += Time.deltaTime;
        float totalDuration = _growDuration + _fadeDuration;

        if (_animTime <= _growDuration)
        {
            float t = _animTime / _growDuration;
            _effectSphere.transform.localScale = Vector3.one * Mathf.Lerp(0f, _explosionRadius * 2f, t);
            SetAlpha(1f);
        }
        else if (_animTime <= totalDuration)
        {
            float t = (_animTime - _growDuration) / _fadeDuration;
            _effectSphere.transform.localScale = Vector3.one * (_explosionRadius * 2f);
            SetAlpha(1f - t);

            if (!_effectFired)
            {
                FireOverlap();
                _effectFired = true;
            }
        }
        else
        {
            Destroy(_effectSphere);
            _effectSphere   = null;
            _effectRenderer = null;
            _animating      = false;
        }
    }

    // ── Overlap detection ─────────────────────────────────────────────────────

    private void FireOverlap()
    {
        Vector3 origin = transform.position;
        origin.z = 0f;

        Collider[] hits = Physics.OverlapSphere(origin, _explosionRadius);

        foreach (Collider col in hits)
        {
            switch (_abilityType)
            {
                case AbilityType.BreakWall:
                    BreakableWall wall = col.GetComponent<BreakableWall>();
                    if (wall != null)
                    {
                        ScoreTracker.Instance?.AddWallBreak();
                        wall.Break();
                    }
                    break;

                case AbilityType.CollectStation:
                    Station station = col.GetComponent<Station>();
                    if (station != null)
                        station.Collect();
                    break;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetAlpha(float alpha)
    {
        if (_effectRenderer == null) return;

        Material mat = _effectRenderer.material;

        if (mat.HasProperty("_Color"))
        {
            Color c = mat.color;
            c.a = alpha;
            mat.color = c;
        }
        else if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
    }
}
