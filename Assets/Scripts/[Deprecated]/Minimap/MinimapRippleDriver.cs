using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the MinimapRipple shader on the map background RawImage.
///
/// Attach to the Scripts child of the Minimap prefab (same GameObject as MinimapController).
///
/// INSPECTOR SETUP
///   _mapBackground  — the RawImage on MapContainer
///   _rippleMaterial — a duplicated Material asset using the MinimapRipple shader.
///                     All visual properties (_RippleColor, _RippleWidth, _MaxRadius, etc.)
///                     are read directly from this material — do not set them here.
///
/// USAGE
///   MinimapRippleDriver.Instance.TriggerRipple(mapUV)
///   where mapUV is a 0–1 UV position on the map (use WorldToMapUV() in MinimapProblemTracker).
///   If a ripple is already playing it restarts at the new origin.
/// </summary>
public class MinimapRippleDriver : MonoBehaviour
{
    public static MinimapRippleDriver Instance { get; private set; }

    [SerializeField] private RawImage  _mapBackground;
    [SerializeField] private Material  _rippleMaterial;

    // Animation duration lives here because it drives a C# coroutine, not a shader property.
    // Change this value in code if you need a different speed.
    private const float RippleDuration = 1.5f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Material  _mat;
    private Coroutine _rippleCoroutine;

    // Cached shader property IDs for the three values this script writes at runtime.
    private static readonly int PropOrigin   = Shader.PropertyToID("_RippleOrigin");
    private static readonly int PropProgress = Shader.PropertyToID("_RippleProgress");
    private static readonly int PropAspect   = Shader.PropertyToID("_AspectRatio");

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        if (_mapBackground == null || _rippleMaterial == null)
        {
            Debug.LogWarning("[MinimapRippleDriver] _mapBackground or _rippleMaterial not assigned — ripple disabled.", this);
            enabled = false;
            return;
        }

        // Runtime copy — all blackboard property values are carried over from the asset.
        // This script only ever writes _RippleOrigin, _RippleProgress, and _AspectRatio.
        _mat = new Material(_rippleMaterial);
        _mapBackground.material = _mat;

        // Ring is invisible at rest (_RippleProgress=1 → fadeOut=0).
        _mat.SetFloat(PropProgress, 1f);

        // Aspect ratio is pushed by MinimapController once world bounds are resolved.
        _mat.SetFloat(PropAspect, 1f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_mat != null) Destroy(_mat);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by MinimapController after world bounds are resolved.
    /// aspect = worldBounds.width / worldBounds.height — the same ratio used to
    /// configure the minimap camera RT, so UV distance is circular not oval.
    /// </summary>
    public void SetAspectRatio(float aspect)
    {
        if (_mat != null)
            _mat.SetFloat(PropAspect, aspect);
    }

    /// <summary>
    /// Triggers a ripple expanding from the given UV position on the map.
    /// mapUV is in 0–1 space where (0,0) = bottom-left of MapContainer.
    /// Any in-progress ripple is interrupted and replaced.
    /// </summary>
    public void TriggerRipple(Vector2 mapUV)
    {
        if (_mat == null) return;

        if (_rippleCoroutine != null)
            StopCoroutine(_rippleCoroutine);

        _mat.SetVector(PropOrigin, new Vector2(mapUV.x, mapUV.y));
        _rippleCoroutine = StartCoroutine(AnimateRipple());
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private IEnumerator AnimateRipple()
    {
        float elapsed = 0f;

        while (elapsed < RippleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / RippleDuration);

            // Ease-out quad: fast start, gradual finish — feels snappy on discovery.
            float progress = 1f - (1f - t) * (1f - t);
            _mat.SetFloat(PropProgress, progress);

            yield return null;
        }

        _mat.SetFloat(PropProgress, 1f); // snap to fully faded
        _rippleCoroutine = null;
    }
}
