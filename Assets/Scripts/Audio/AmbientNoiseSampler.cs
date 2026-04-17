using TMPro;
using UnityEngine;

/// <summary>
/// Samples the microphone each frame and produces a smoothed BatteryLevel (0–1)
/// that drives gameplay modifiers (fish speed, vision blur).
///
/// TECHNIQUE — Attack-Release (AR) Envelope Follower:
///   Raw RMS is computed from the mic clip each frame. It is NEVER used directly
///   for gameplay. Instead it charges/drains a batteryLevel via two asymmetric
///   rates — fast attack when noise is detected, slow release when silence returns.
///   Result: talking for a few seconds keeps the battery elevated for 6–8s after
///   the room goes quiet. Feels like charging a capacitor, not reacting to spikes.
///
/// TUNING GUIDE (Inspector):
///   noiseThreshold — minimum RMS to count as "active noise". Raise if battery
///                    charges from room hiss. Lower if players must speak loudly.
///   attackRate     — how fast battery charges (3.0 = full charge in ~0.4s).
///   releaseRate    — how fast battery drains (0.15 = full drain in ~7s).
///   sampleWindow   — number of mic samples averaged per frame. 256 at 44100Hz
///                    = ~6ms latency. Do not exceed 2048.
/// </summary>
public class AmbientNoiseSampler : MonoBehaviour
{
    public static AmbientNoiseSampler Instance { get; private set; }

    [Header("Microphone")]
    [Tooltip("Leave blank to use the system default microphone. Check DeviceLogger in Console on Start to see device names and indices.")]
    public string microphoneDevice = null;
    [SerializeField] private int sampleRate = 44100;

    [Header("Noise Detection")]
    [Tooltip("Minimum RMS level to count as active noise. Raise if idle room noise triggers charge.")]
    [SerializeField, Range(0.001f, 0.1f)] private float noiseThreshold = 0.02f;

    [Tooltip("Number of mic samples averaged per frame to compute RMS. 256 = ~6ms at 44100Hz.")]
    [SerializeField, Range(64, 2048)] private int sampleWindow = 256;

    [Header("Battery — AR Envelope")]
    [Tooltip("How fast the battery charges when noise exceeds threshold. 3.0 = ~0.4s to full.")]
    [SerializeField, Range(0.5f, 10f)] private float attackRate = 3f;

    [Tooltip("How fast the battery drains when noise drops below threshold. 0.15 = ~7s to empty.")]
    [SerializeField, Range(0.01f, 2f)] private float releaseRate = 0.15f;

    // ── Public output ──────────────────────────────────────────────────────────

    /// <summary>Smoothed battery level (0–1). Use this for all gameplay modifiers.</summary>
    public float BatteryLevel { get; private set; }

    /// <summary>Raw RMS from the microphone this frame. Use only for debug display.</summary>
    public float RawRMS { get; private set; }

    /// <summary>True when the microphone started successfully.</summary>
    public bool IsRunning { get; private set; }

    [Header("Debug UI")]
    [Tooltip("Optional TMP text element to display live RMS and battery values.")]
    [SerializeField] private TMP_Text _debugText;

    // ── Private ────────────────────────────────────────────────────────────────

    private AudioClip _micClip;
    private float[]   _samples;
    private string    _activeDevice; // resolved device name used for Start/GetPosition/End

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        StartMicrophone();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        StopMicrophone();
    }

    private void Update()
    {
        if (!IsRunning) return;

        RawRMS = SampleMicRMS();

        // AR envelope: charge fast when above threshold, drain slowly when below.
        if (RawRMS > noiseThreshold)
            BatteryLevel = Mathf.Lerp(BatteryLevel, 1f, attackRate  * Time.deltaTime);
        else
            BatteryLevel = Mathf.Lerp(BatteryLevel, 0f, releaseRate * Time.deltaTime);

        if (_debugText != null)
            _debugText.text = $"Mic: {_activeDevice ?? "none"} | RMS: {RawRMS:F4} | Battery: {BatteryLevel:F2} | Running: {IsRunning}";
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Instantly drains the battery to zero. Call on soft-reset.</summary>
    public void ResetBattery() => BatteryLevel = 0f;

    // ── Microphone helpers ─────────────────────────────────────────────────────

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[AmbientNoiseSampler] No microphone devices found.", this);
            return;
        }

        // Resolve to an explicit device name so Start/GetPosition/End all use the same string.
        // Prefer the Inspector override; fall back to devices[0] (system default on Mac/PC).
        _activeDevice = (microphoneDevice != null && microphoneDevice.Length > 0)
            ? microphoneDevice
            : Microphone.devices[1];

        Debug.Log($"[AmbientNoiseSampler] Available mics: [{string.Join(", ", Microphone.devices)}]", this);

        // 1-second looping clip — we only ever read the last sampleWindow frames.
        _micClip = Microphone.Start(_activeDevice, true, 1, sampleRate);

        if (_micClip == null)
        {
            Debug.LogError($"[AmbientNoiseSampler] Microphone.Start returned null for '{_activeDevice}'. " +
                           "Check macOS Privacy > Microphone permission for the Unity Editor.", this);
            return;
        }

        _samples  = new float[sampleWindow];
        IsRunning = true;

        int selectedIndex = System.Array.IndexOf(Microphone.devices, _activeDevice);
        Debug.Log($"[AmbientNoiseSampler] Selected mic [{selectedIndex}]: \"{_activeDevice}\" @ {sampleRate}Hz", this);
    }

    private void StopMicrophone()
    {
        if (!IsRunning) return;
        Microphone.End(_activeDevice);
        IsRunning = false;
    }

    private float SampleMicRMS()
    {
        if (_micClip == null) return 0f;

        // Use _activeDevice — must match the name passed to Microphone.Start.
        // Hardcoding null here (as before) would track a different device if _activeDevice is named.
        int micPos = Microphone.GetPosition(_activeDevice);
        if (micPos < sampleWindow) return 0f; // not enough data buffered yet

        int startPos = micPos - sampleWindow;
        if (!_micClip.GetData(_samples, startPos)) return 0f;

        float sum = 0f;
        for (int i = 0; i < _samples.Length; i++)
            sum += _samples[i] * _samples[i];

        return Mathf.Sqrt(sum / _samples.Length);
    }

        // ── Gizmos / debug ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        GUI.Label(new Rect(10, 10, 300, 20), $"Mic RMS: {RawRMS:F4}  Battery: {BatteryLevel:F2}");
    }
#endif
}
