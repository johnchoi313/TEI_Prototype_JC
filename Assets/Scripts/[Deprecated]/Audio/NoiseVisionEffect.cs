using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives URP post-processing effects based on AmbientNoiseSampler.BatteryLevel.
///
/// EFFECTS:
///   Depth of Field (Gaussian) — blurs the image. Primary "vision impaired" cue.
///   Chromatic Aberration       — color fringing. Secondary "sonar overwhelmed" cue.
///
/// CURVE DESIGN:
///   Speed (in FOVHighlightable) scales linearly with battery.
///   Blur scales quadratically (battery²) — nearly invisible below 0.7,
///   then ramps sharply. This creates a natural sweet spot at 40–70% battery:
///   fish are fast but vision is mostly clear. Above 70%: real visual cost.
///
/// SETUP:
///   1. Add this component to the same GameObject as a Volume component.
///   2. Set Volume.isGlobal = true, priority = 1 (overrides global profile).
///   3. Leave the Volume's profile slot empty — this script creates an
///      in-memory override profile at runtime so no asset file is needed.
///
/// TUNING:
///   maxBlurRadius          — Gaussian blur radius at battery == 1. (Default 1.0)
///                            Range 0-1.5 in URP. Values above 1.0 are heavily blurred.
///   maxChromaticAberration — Aberration intensity at battery == 1. (Default 0.6)
///                            Range 0-1 in URP. 0.6 is noticeably distorted.
///   blurCurveExponent      — Power curve applied to battery before mapping to blur.
///                            2.0 = quadratic (recommended). 1.0 = linear.
/// </summary>
[RequireComponent(typeof(Volume))]
public class NoiseVisionEffect : MonoBehaviour
{
    [Header("Blur (Depth of Field — Gaussian)")]
    [Tooltip("Gaussian blur radius at full battery. URP range: 0–1.5.")]
    [SerializeField, Range(0f, 5f)] private float maxBlurRadius = 1.0f;

    [Header("Chromatic Aberration")]
    [Tooltip("Aberration intensity at full battery. URP range: 0–1.")]
    [SerializeField, Range(0f, 1f)] private float maxChromaticAberration = 0.6f;

    [Header("Curve")]
    [Tooltip("Power curve applied to BatteryLevel before mapping to blur. " +
             "2.0 = quadratic (mostly clear until ~0.7, then sharp ramp). 1.0 = linear.")]
    [SerializeField, Range(0.5f, 4f)] private float blurCurveExponent = 2f;

    // ── Private ────────────────────────────────────────────────────────────────

    private Volume           _volume;
    private DepthOfField     _dof;
    private ChromaticAberration _ca;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        _volume = GetComponent<Volume>();

        // Create a runtime-only VolumeProfile — no asset file required.
        // This keeps scene assets clean and makes the prefab fully self-contained.
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // Add Depth of Field (Gaussian mode).
        _dof = profile.Add<DepthOfField>();
        _dof.SetAllOverridesTo(true);
        _dof.mode.Override(DepthOfFieldMode.Gaussian);
        _dof.gaussianMaxRadius.Override(0f);
        // Focus far away so the game world is always in the "blur zone" at high battery.
        _dof.gaussianStart.Override(0.1f);
        _dof.gaussianEnd.Override(0.5f);

        // Add Chromatic Aberration.
        _ca = profile.Add<ChromaticAberration>();
        _ca.SetAllOverridesTo(true);
        _ca.intensity.Override(0f);

        _volume.profile = profile;
        _volume.isGlobal = true;
        _volume.priority = 1f;
    }

    private void Update()
    {
        if (_dof == null || _ca == null) return;

        // Blur is now driven by hand depth via the FOV_Mask shader (TEIHandTrackingShaderBridge).
        // Audio-driven blur has been removed. This volume outputs zero so it has no effect.
        _dof.gaussianMaxRadius.Override(0f);
        _ca.intensity.Override(0f);
    }

    private void OnDestroy()
    {
        // Clean up the runtime profile to avoid memory leaks.
        if (_volume != null && _volume.profile != null)
            Destroy(_volume.profile);
    }
}
