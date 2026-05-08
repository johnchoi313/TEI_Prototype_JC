using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads microphone volume from a SimpleSpectrum instance and maps it to
/// PlayerFishController.FollowSpeed each frame.
///
/// Volume is computed as the RMS of SimpleSpectrum's processed output bars,
/// then remapped from [minVolume, maxVolume] → [minEnergy, maxEnergy].
/// "Energy" is the internal mic-driven value passed to fish controllers as FollowSpeed.
///
/// Auto-calibration
///   On start the script silently listens for CalibrationDuration seconds,
///   averages the ambient RMS, and sets:
///     minVolume = average ambient RMS
///     maxVolume = minVolume × MaxVolumeMultiplier (default 4×)
///   This means you never need to recalibrate when swapping microphones.
///   The fish stays at min energy during calibration. A manual recalibrate
///   can be triggered at runtime via RecalibrateAmbient().
///
/// SCENE SETUP
///   1. Place this component on any GameObject in the scene (e.g. an "AudioManager").
///   2. Point Spectrum at a SimpleSpectrum configured with SourceType = MicrophoneInput.
///   3. Set Min/Max Energy to taste — volume thresholds are set automatically.
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

    [Tooltip("maxVolume = minVolume × this multiplier. 4 means you need to be 4× louder than ambient to reach max energy.")]
    [SerializeField] private float _maxVolumeMultiplier = 4f;

    [Tooltip("Absolute floor for minVolume — prevents near-zero values on a very quiet mic from causing instability.")]
    [SerializeField] private float _minVolumeFloor = 0.001f;

    [Header("Energy Range")]
    [Tooltip("Energy value sent to fish controllers when volume is at or below minVolume (ambient noise floor).")]
    [SerializeField] private float _minEnergy = 1f;

    [Tooltip("Energy value sent to fish controllers when volume is at or above maxVolume.")]
    [SerializeField] private float _maxEnergy = 10f;

    [Tooltip("Image whose fill amount tracks current energy normalized from minEnergy (0) to maxEnergy (1).")]
    [SerializeField] private Image _energyFillImage;

    [Tooltip("Fill image color when energy is at minimum.")]
    public Color fillColorMin = Color.white;

    [Tooltip("Fill image color when energy is at maximum.")]
    public Color fillColorMax = Color.white;

    [Header("Smoothing")]
    [Tooltip("Seconds for energy to rise from min to max when volume spikes. Smaller = snappier attack.")]
    [SerializeField] private float _attackTime = 0.08f;

    [Tooltip("Seconds for energy to fall back to min after volume drops. Larger = energy lingers longer.")]
    [SerializeField] private float _decayTime = 2.5f;

    [Header("Fish Speed")]
    [Tooltip("Discrete mode: speed snaps to maxSpeed when smoothedEnergy >= maxEnergy, otherwise defaultSpeed. " +
             "Continuous mode: speed lerps from defaultSpeed to maxSpeed across the energy range.")]
    [SerializeField] private bool _discrete = false;

    [Tooltip("Speed sent to fish controllers when mic is disabled or energy is below threshold (discrete mode).")]
    [SerializeField] private float _defaultSpeed = 4f;

    [Tooltip("Speed sent to fish controllers when energy is at or above maxEnergy. " +
             "In continuous mode this is the high end of the speed lerp.")]
    [SerializeField] private float _maxSpeed = 10f;


    [Header("Camera Background Color")]
    [Tooltip("Enable background color lerping based on energy.")]
    public bool enableColorLerp = true;

    [Tooltip("Camera whose background solid color is lerped by energy. Leave unassigned to skip.")]
    public Camera backgroundCamera;

    [Tooltip("Background color when energy is at minimum.")]
    public Color bgColorMin = Color.black;

    [Tooltip("Background color when energy is at maximum.")]
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
    [Tooltip("Current energy value being applied.")]
    [SerializeField] private float _debugCurrentEnergy;
    [Tooltip("0–1 value fed into the background color lerp. 0 = bgColorMin, 1 = bgColorMax.")]
    [SerializeField] private float _debugColorT;

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The smoothed energy value currently being applied (passed to fish as FollowSpeed).
    /// In discrete mode: snaps to maxEnergy or disabledEnergy.
    /// Returns DisabledEnergy as a constant when mic is set to None.
    /// </summary>
    public float CurrentSpeed
    {
        get
        {
            if (_isMicDisabled) return _defaultSpeed;
            if (_discrete)      return _smoothedEnergy >= _maxEnergy ? _maxSpeed : _defaultSpeed;
            float t = Mathf.Clamp01(Mathf.InverseLerp(_minEnergy, _maxEnergy, _smoothedEnergy));
            return Mathf.Lerp(_defaultSpeed, _maxSpeed, t);
        }
    }

    /// <summary>
    /// Enables or disables mic-driven energy. When disabled, CurrentSpeed
    /// returns the constant DisabledEnergy and SimpleSpectrum is turned off.
    /// Called by SimpleSpectrumMicDebugUI when None is selected.
    /// </summary>
    public void SetMicDisabled(bool disabled)
    {
        _isMicDisabled = disabled;
        if (_spectrum != null)
            _spectrum.isEnabled = !disabled;
        if (disabled)
            _smoothedEnergy = _minEnergy;
    }

    /// <summary>Raw smoothed energy level before discrete snapping. Use this for UI readouts.</summary>
    public float CurrentEnergy => _smoothedEnergy;

    /// <summary>Raw RMS volume from the spectrum this frame (0–1).</summary>
    public float CurrentRMS => _debugCurrentVolume;

    /// <summary>The configured minimum energy (maps to silence).</summary>
    public float MinSpeed => _minEnergy;

    /// <summary>The configured maximum energy (maps to loudest input).</summary>
    public float MaxSpeed => _maxEnergy;

    /// <summary>True while the initial ambient calibration is running.</summary>
    public bool IsCalibrating => _isCalibrating;

    /// <summary>True when mic has been set to None — CurrentSpeed returns DisabledEnergy.</summary>
    public bool IsMicDisabled => _isMicDisabled;

    /// <summary>
    /// Re-runs the ambient noise calibration (e.g. after switching microphone).
    /// Fish stays at minimum energy during the sample window.
    /// </summary>
    public void RecalibrateAmbient() => StartCoroutine(CalibrateAmbient());

    // ── Runtime ───────────────────────────────────────────────────────────────

    private float _smoothedEnergy;
    private float _minVolume;
    private float _maxVolume;
    private bool  _isMicDisabled;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _smoothedEnergy = _minEnergy;

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
        if (_isMicDisabled)
        {
            _smoothedEnergy = _minEnergy;
            UpdateBackgroundColor();
            return;
        }

        if (_isCalibrating || _spectrum == null || !_spectrum.isEnabled)
        {
            _smoothedEnergy = _minEnergy;
        }
        else
        {
            float volume = ComputeRMSVolume(_spectrum.spectrumOutputData);
            float targetEnergy = MapVolumeToEnergy(volume);

            // Frame-rate-independent exponential smoothing.
            // t = 1 - exp(-dt / halfLife) approximates a proper RC filter.
            // Attack and decay use separate time constants so high energy lingers.
            float timeConstant = targetEnergy > _smoothedEnergy ? _attackTime : _decayTime;
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(timeConstant, 0.001f));
            _smoothedEnergy = Mathf.Lerp(_smoothedEnergy, targetEnergy, t);

            _debugCurrentVolume = volume;
            _debugCurrentEnergy = _smoothedEnergy;
            _debugMinVolume     = _minVolume;
            _debugMaxVolume     = _maxVolume;
        }

        // Always update — runs even during calibration so the color
        // is visible and starts at bgColorMin immediately.
        UpdateBackgroundColor();
        UpdateFillImage();
    }

    private void UpdateBackgroundColor()
    {
        if (!enableColorLerp || backgroundCamera == null) return;

        // Force Solid Color so the tint is actually visible.
        if (backgroundCamera.clearFlags != CameraClearFlags.SolidColor)
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;

        float t = Mathf.Clamp01(Mathf.InverseLerp(_minEnergy, _maxEnergy, _smoothedEnergy));
        _debugColorT = t;
        backgroundCamera.backgroundColor = Color.Lerp(bgColorMin, bgColorMax, t);
    }

    private void UpdateFillImage()
    {
        if (_energyFillImage == null) return;
        float t = Mathf.Clamp01(Mathf.InverseLerp(_minEnergy, _maxEnergy, _smoothedEnergy));
        _energyFillImage.fillAmount = t;
        _energyFillImage.color      = Color.Lerp(fillColorMin, fillColorMax, t);
    }

    // ── Calibration ───────────────────────────────────────────────────────────

    private IEnumerator CalibrateAmbient()
    {
        _isCalibrating = true;

        // Wait one frame so SimpleSpectrum has initialised its output array.
        yield return null;

        float elapsed     = 0f;
        float rmsSum      = 0f;
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

    private float MapVolumeToEnergy(float volume)
    {
        // Unclamped ratio so energy can exceed maxEnergy on very loud input.
        float range = _maxVolume - _minVolume;
        float t = range > 0f ? (volume - _minVolume) / range : 0f;
        return _minEnergy + t * (_maxEnergy - _minEnergy);
    }
}
