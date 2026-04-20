using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime debug panel for SimpleSpectrum microphone configuration.
///
/// Layout  [ Microphone Debug UI | Volume: 0.123 | Speed: 4.567 | [Dropdown] ]
///
/// Features
///   • Horizontal row of TMP labels — one per value — plus a mic dropdown.
///   • Volume and Speed floats shown to 3 decimal places.
///   • TMP_Dropdown auto-populated with all connected microphone devices.
///   • Selecting a device rebuilds SimpleSpectrum and re-calibrates MicVolumeToFishSpeed.
///   • Selected device persisted via PlayerPrefs and restored on start.
///
/// SCENE SETUP
///   1. Create a Canvas → Horizontal Layout Group panel; attach this component.
///   2. Inside add (left to right):
///        • TMP_Text  "Microphone Debug UI"  (static label) — assign to Header Label
///        • TMP_Text  (volume readout)        — assign to Volume Label
///        • TMP_Text  (speed readout)         — assign to Speed Label  (optional)
///        • TMP_Dropdown                      — assign to Mic Dropdown
///   3. Assign SimpleSpectrum (SourceType = MicrophoneInput).
///   4. Optionally assign MicVolumeToFishSpeed for the speed cell.
/// </summary>
public class SimpleSpectrumMicDebugUI : MonoBehaviour
{
    private const string PREF_KEY_MIC = "SimpleSpectrum_SelectedMic";

    [Header("References")]
    [Tooltip("The SimpleSpectrum instance configured with SourceType = MicrophoneInput.")]
    [SerializeField] private SimpleSpectrum _spectrum;

    [Tooltip("Optional — populates the Speed cell and triggers recalibration on mic change.")]
    [SerializeField] private MicVolumeToFishSpeed _micToSpeed;

    [Header("UI — horizontal cells (left → right)")]
    [Tooltip("Static header label. Text is set once to 'Microphone Debug UI'.")]
    [SerializeField] private TMP_Text _headerLabel;

    [Tooltip("Displays live RMS volume to 3 d.p.")]
    [SerializeField] private TMP_Text _volumeLabel;

    [Tooltip("Displays current fish speed to 3 d.p. Leave unassigned if not using MicVolumeToFishSpeed.")]
    [SerializeField] private TMP_Text _speedLabel;

    [Tooltip("Dropdown populated with available microphone devices.")]
    [SerializeField] private TMP_Dropdown _micDropdown;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private string[] _deviceNames; // parallel array to dropdown options

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (_headerLabel != null)
            _headerLabel.text = "Microphone Debug UI";

        PopulateDropdown();
        RestorePersistedMic();

        if (_micDropdown != null)
            _micDropdown.onValueChanged.AddListener(OnMicSelected);
    }

    private void Update()
    {
        if (_spectrum == null) return;

        bool calibrating = _micToSpeed != null && _micToSpeed.IsCalibrating;

        // Volume cell
        if (_volumeLabel != null)
        {
            _volumeLabel.text = calibrating
                ? "Calibrating..."
                : $"Vol: {ComputeRMSVolume(_spectrum.spectrumOutputData):F3}";
        }

        // Speed cell
        if (_speedLabel != null)
        {
            _speedLabel.text = calibrating
                ? "---"
                : _micToSpeed != null
                    ? $"Spd: {_micToSpeed.CurrentSpeed:F3}"
                    : string.Empty;
        }
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    private void PopulateDropdown()
    {
        if (_micDropdown == null) return;

        string[] devices = Microphone.devices;

        var options = new List<TMP_Dropdown.OptionData>();
        var names   = new List<string>();

        options.Add(new TMP_Dropdown.OptionData("Default Microphone"));
        names.Add(null); // null = system default in SimpleSpectrum

        foreach (string dev in devices)
        {
            options.Add(new TMP_Dropdown.OptionData(dev));
            names.Add(dev);
        }

        _deviceNames = names.ToArray();

        _micDropdown.ClearOptions();
        _micDropdown.AddOptions(options);
    }

    private void RestorePersistedMic()
    {
        if (_deviceNames == null || !PlayerPrefs.HasKey(PREF_KEY_MIC)) return;

        string saved = PlayerPrefs.GetString(PREF_KEY_MIC);

        int index = 0;
        for (int i = 0; i < _deviceNames.Length; i++)
        {
            if (_deviceNames[i] == saved) { index = i; break; }
        }

        _micDropdown.SetValueWithoutNotify(index);
        ApplyMicDevice(_deviceNames[index]);
    }

    private void OnMicSelected(int index)
    {
        string deviceName = _deviceNames[index];
        ApplyMicDevice(deviceName);

        PlayerPrefs.SetString(PREF_KEY_MIC, deviceName ?? string.Empty);
        PlayerPrefs.Save();
    }

    private void ApplyMicDevice(string deviceName)
    {
        if (_spectrum == null) return;

        _spectrum.overrideMicrophoneName = deviceName;
        _spectrum.RebuildSpectrum();

        if (_micToSpeed != null)
            _micToSpeed.RecalibrateAmbient();
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
}
