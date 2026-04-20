using System.Collections;
using UnityEngine;

/// <summary>
/// Reads microphone volume from a SimpleSpectrum instance and maps it to
/// PlayerFishController.FollowSpeed each frame.
///
/// Volume is computed as the RMS of SimpleSpectrum's processed output bars,
/// then remapped from [minVolume, maxVolume] → [minSpeed, maxSpeed].
///
/// Auto-calibration
///   On start the script silently listens for CalibrationDuration seconds,
///   averages the ambient RMS, and sets:
///     minVolume = average ambient RMS
///     maxVolume = minVolume × MaxVolumeMultiplier (default 4×)
///   This means you never need to recalibrate when swapping microphones.
///   The fish stays at min speed during calibration. A manual recalibrate
///   can be triggered at runtime via RecalibrateAmbient().
///
/// SCENE SETUP
///   1. Place this component on any GameObject in the scene (e.g. an "AudioManager").
///   2. Point Spectrum at a SimpleSpectrum configured with SourceType = MicrophoneInput.
///   3. Set Min/Max Speed to taste — volume thresholds are set automatically.
///   4. Assign this component to the Mic Volume Driver field on each PlayerFishController
///      that should be driven by mic volume.
/// </summary>
public class MicVolumeToFishSpeed : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The SimpleSpectrum instance set to MicrophoneInput. Must have isEnabled = true.")]
    [SerializeField] private SimpleSpectrum _spectrum;

    [Header("Auto-Calibration")]
    [Tooltip("Seconds of ambient silence to sample at startup for noise-floor detection.")]
    [SerializeField] private float _calibrationDuration = 1.5f;

    [Tooltip("maxVolume = minVolume × this multiplier. 4 means you need to be 4× louder than ambient to reach full speed.")]
    [SerializeField] private float _maxVolumeMultiplier = 4f;

    [Tooltip("Absolute floor for minVolume — prevents near-zero values on a very quiet mic from causing instability.")]
    [SerializeField] private float _minVolumeFloor = 0.001f;

    [Header("Speed Range")]
    [Tooltip("Fish speed when volume is at or below minVolume (ambient noise floor).")]
    [SerializeField] private float _minSpeed = 1f;

    [Tooltip("Fish speed when volume is at or above maxVolume.")]
    [SerializeField] private float _maxSpeed = 10f;

    [Header("Smoothing")]
    [Tooltip("Seconds for speed to rise from min to max when volume spikes. Smaller = snappier attack.")]
    [SerializeField] private float _attackTime = 0.08f;

    [Tooltip("Seconds for speed to fall back to min after volume drops. Larger = speed lingers longer.")]
    [SerializeField] private float _decayTime = 2.5f;

    [Header("Camera Background Color")]
    [Tooltip("Enable background color lerping based on speed.")]
    public bool enableColorLerp = true;

    [Tooltip("Camera whose background solid color is lerped by speed. Leave unassigned to skip.")]
    public Camera backgroundCamera;

    [Tooltip("Background color when fish speed is at minimum.")]
    public Color bgColorMin = Color.black;

    [Tooltip("Background color when fish speed is at maximum.")]
    public Color bgColorMax = new Color(0.1f, 0.2f, 0.5f);

    [Header("Debug (read-only)")]
    [Tooltip("True while the startup ambient sample is being collected.")]
    [SerializeField] private bool  _isCalibrating;
    [Tooltip("Noise floor measured during calibration. minVolume is set to this.")]
    [SerializeField] private float _debugAmbientRMS;
    [Tooltip("Current RMS volume from the spectrum.")]
    [SerializeField] private float _debugCurrentVolume;
    [Tooltip("Calibrated minimum volume threshold.")]
    [SerializeField] private float _debugMinVolume;
    [Tooltip("Calibrated maximum volume threshold.")]
    [SerializeField] private float _debugMaxVolume;
    [Tooltip("Current fish speed being applied.")]
    [SerializeField] private float _debugCurrentSpeed;
    [Tooltip("0–1 value fed into the background color lerp. 0 = bgColorMin, 1 = bgColorMax.")]
    [SerializeField] private float _debugColorT;

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>The smoothed fish speed currently being applied.</summary>
    public float CurrentSpeed => _smoothedSpeed;

    /// <summary>Raw RMS volume from the spectrum this frame (0–1).</summary>
    public float CurrentRMS => _debugCurrentVolume;

    /// <summary>The configured minimum fish speed (maps to silence).</summary>
    public float MinSpeed => _minSpeed;

    /// <summary>The configured maximum fish speed (maps to loudest input).</summary>
    public float MaxSpeed => _maxSpeed;

    /// <summary>True while the initial ambient calibration is running.</summary>
    public bool IsCalibrating => _isCalibrating;

    /// <summary>
    /// Re-runs the ambient noise calibration (e.g. after switching microphone).
    /// Fish stays at minimum speed during the sample window.
    /// </summary>
    public void RecalibrateAmbient() => StartCoroutine(CalibrateAmbient());

    // ── Runtime ───────────────────────────────────────────────────────────────

    private float _smoothedSpeed;
    private float _minVolume;
    private float _maxVolume;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _smoothedSpeed = _minSpeed;

        // Safe fallback values until calibration finishes.
        _minVolume = _minVolumeFloor;
        _maxVolume = _minVolumeFloor * _maxVolumeMultiplier;
    }

    private void Start()
    {
        StartCoroutine(CalibrateAmbient());
    }

    private void Update()
    {
        if (_isCalibrating || _spectrum == null || !_spectrum.isEnabled)
        {
            _smoothedSpeed = _minSpeed;
        }
        else
        {
            float volume = ComputeRMSVolume(_spectrum.spectrumOutputData);
            float targetSpeed = MapVolumeToSpeed(volume);

            // Frame-rate-independent exponential smoothing.
            // t = 1 - exp(-dt / halfLife) approximates a proper RC filter.
            // Attack and decay use separate time constants so high speed lingers.
            float timeConstant = targetSpeed > _smoothedSpeed ? _attackTime : _decayTime;
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(timeConstant, 0.001f));
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, targetSpeed, t);

            _debugCurrentVolume = volume;
            _debugCurrentSpeed  = _smoothedSpeed;
            _debugMinVolume     = _minVolume;
            _debugMaxVolume     = _maxVolume;
        }

        // Always update — runs even during calibration so the color
        // is visible and starts at bgColorMin immediately.
        UpdateBackgroundColor();
    }

    private void UpdateBackgroundColor()
    {
        if (!enableColorLerp || backgroundCamera == null) return;

        // Force Solid Color so the tint is actually visible.
        if (backgroundCamera.clearFlags != CameraClearFlags.SolidColor)
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;

        float t = Mathf.InverseLerp(_minSpeed, _maxSpeed, _smoothedSpeed);
        _debugColorT = t;
        backgroundCamera.backgroundColor = Color.Lerp(bgColorMin, bgColorMax, t);
    }

    // ── Calibration ───────────────────────────────────────────────────────────

    private IEnumerator CalibrateAmbient()
    {
        _isCalibrating = true;

        // Wait one frame so SimpleSpectrum has initialised its output array.
        yield return null;

        float elapsed   = 0f;
        float rmsSum    = 0f;
        int   sampleCount = 0;

        while (elapsed < _calibrationDuration)
        {
            if (_spectrum != null && _spectrum.isEnabled)
            {
                rmsSum += ComputeRMSVolume(_spectrum.spectrumOutputData);
                sampleCount++;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        float ambientRMS = sampleCount > 0 ? rmsSum / sampleCount : _minVolumeFloor;
        ambientRMS = Mathf.Max(ambientRMS, _minVolumeFloor); // guard against silent mic

        _minVolume = ambientRMS;
        _maxVolume = ambientRMS * _maxVolumeMultiplier;

        _debugAmbientRMS = ambientRMS;
        _debugMinVolume  = _minVolume;
        _debugMaxVolume  = _maxVolume;

        _isCalibrating = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float ComputeRMSVolume(float[] bars)
    {
        if (bars == null || bars.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < bars.Length; i++)
            sum += bars[i] * bars[i];

        return Mathf.Sqrt(sum / bars.Length);
    }

    private float MapVolumeToSpeed(float volume)
    {
        float t = Mathf.InverseLerp(_minVolume, _maxVolume, volume);
        return Mathf.Lerp(_minSpeed, _maxSpeed, t);
    }
}
