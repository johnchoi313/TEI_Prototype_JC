using UnityEngine;

/// <summary>
/// Logs all connected webcam and microphone devices to the Console on Start.
/// Assign preferred device indices in the Inspector to override the defaults
/// used by WebCamSource and AmbientNoiseSampler.
/// </summary>
public class DeviceLogger : MonoBehaviour
{
    [Header("Webcam")]
    [Tooltip("Index of the webcam to use. 0 = first device. Logged on Start so you can check names.")]
    public int preferredWebcamIndex = 0;

    [Header("Microphone")]
    [Tooltip("Index of the microphone to use. 0 = first device. Logged on Start so you can check names.")]
    public int preferredMicrophoneIndex = 0;

    private void Start()
    {
        LogWebcams();
        LogMicrophones();
    }

    private void LogWebcams()
    {
        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices.Length == 0)
        {
            Debug.LogWarning("[DeviceLogger] No webcam devices found.");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[DeviceLogger] Found {devices.Length} webcam device(s):");
        for (int i = 0; i < devices.Length; i++)
        {
            string frontFacing = devices[i].isFrontFacing ? " (front-facing)" : "";
            sb.AppendLine($"  [{i}] {devices[i].name}{frontFacing}");
        }

        if (preferredWebcamIndex >= 0 && preferredWebcamIndex < devices.Length)
            sb.AppendLine($"  → preferredWebcamIndex {preferredWebcamIndex} = \"{devices[preferredWebcamIndex].name}\"");
        else
            sb.AppendLine($"  ⚠ preferredWebcamIndex {preferredWebcamIndex} is out of range (max {devices.Length - 1})");

        Debug.Log(sb.ToString(), this);
    }

    private void LogMicrophones()
    {
        string[] devices = Microphone.devices;

        if (devices.Length == 0)
        {
            Debug.LogWarning("[DeviceLogger] No microphone devices found.");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[DeviceLogger] Found {devices.Length} microphone device(s):");
        for (int i = 0; i < devices.Length; i++)
        {
            sb.AppendLine($"  [{i}] {devices[i]}");
        }

        if (preferredMicrophoneIndex >= 0 && preferredMicrophoneIndex < devices.Length)
            sb.AppendLine($"  → preferredMicrophoneIndex {preferredMicrophoneIndex} = \"{devices[preferredMicrophoneIndex]}\"");
        else
            sb.AppendLine($"  ⚠ preferredMicrophoneIndex {preferredMicrophoneIndex} is out of range (max {devices.Length - 1})");

        Debug.Log(sb.ToString(), this);
    }
}
