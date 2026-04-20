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
    private const string PREF_KEY_MIC  = "SimpleSpectrum_SelectedMic";
    private const string NONE_SENTINEL = "__NONE__";

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
        bool micOff       = _micToSpeed != null && _micToSpeed.IsMicDisabled;
        bool calibrating  = !micOff && _micToSpeed != null && _micToSpeed.IsCalibrating;

        // Volume cell
        if (_volumeLabel != null)
        {
            if (micOff)
                _volumeLabel.text = "Mic: OFF";
            else if (_spectrum == null)
                _volumeLabel.text = string.Empty;
            else
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

        // None — disables mic entirely, fish runs at constant default speed.
        options.Add(new TMP_Dropdown.OptionData("None (Mic Off)"));
        names.Add(NONE_SENTINEL);

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

        // Empty string was previously used for "default mic" — keep compatible.
        if (string.IsNullOrEmpty(saved)) saved = null;

        int index = 1; // fallback to Default Microphone (index 1)
        for (int i = 0; i < _deviceNames.Length; i++)
        {
            if (_deviceNames[i] == saved) { index = i; break; }
        }

        _micDropdown.SetValueWithoutNotify(index);
        ApplySelection(_deviceNames[index]);
    }

    private void OnMicSelected(int index)
    {
        string deviceName = _deviceNames[index];
        ApplySelection(deviceName);

        PlayerPrefs.SetString(PREF_KEY_MIC, deviceName ?? string.Empty);
        PlayerPrefs.Save();
    }

    private void ApplySelection(string deviceName)
    {
        bool isNone = deviceName == NONE_SENTINEL;

        if (_micToSpeed != null)
            _micToSpeed.SetMicDisabled(isNone);

        if (isNone) return;

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
