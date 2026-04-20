using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SFB;
using TMPro;
using UnityEngine;

/// <summary>
/// Records session metrics to a CSV file every second and writes a summary on stop.
///
/// SCENE SETUP
///   1. Add this component to any persistent GameObject.
///   2. Assign playerA, playerB, lightA, lightB, and micDriver in the Inspector.
///   3. Wire three UI buttons to PickSaveFolder(), StartLogging(), StopLogging().
///   4. The save folder is remembered across sessions via PlayerPrefs.
///
/// FILE NAMING
///   couch_game_YYYY-MM-DD_HH-MM_session[N].csv          — per-second rows
///   couch_game_YYYY-MM-DD_HH-MM_session[N]_summary.csv  — single-row summary
///
///   Session [N] increments if a file for that timestamp already exists, so
///   re-opening the app within the same minute starts a fresh numbered file.
///   Rows are appended, so a mid-session crash still preserves collected data.
/// </summary>
public class Logger : MonoBehaviour
{
    // ── PlayerPrefs key ───────────────────────────────────────────────────────
    private const string PrefKey = "Logger_SaveFolder";

    // ── Inspector references ──────────────────────────────────────────────────
    [Header("Player References")]
    [Tooltip("KinectPlayerController for Player A")]
    public KinectPlayerController playerA;

    [Tooltip("KinectPlayerController for Player B")]
    public KinectPlayerController playerB;

    [Header("Light / FOV References")]
    [Tooltip("PlayerLightController for Player A (provides CurrentZoom)")]
    public PlayerLightController lightA;

    [Tooltip("PlayerLightController for Player B (provides CurrentZoom)")]
    public PlayerLightController lightB;

    [Header("Audio / Fish Reference")]
    [Tooltip("MicVolumeToFishSpeed driver (provides CurrentRMS and CurrentSpeed)")]
    public MicVolumeToFishSpeed micDriver;

    [Header("Debug UI")]
    [Tooltip("Optional TMP text to display the current save path and logging status.")]
    [SerializeField] private TMP_Text _debugText;

    [Tooltip("GameObject shown when NOT recording (e.g. a 'Press Record' prompt or idle indicator). " +
             "Set active when stopped, inactive while logging.")]
    [SerializeField] private GameObject _notRecordingIndicator;

    [Header("Debug")]
    [Tooltip("Current save folder path. Set via PickSaveFolder() or type directly.")]
    [SerializeField] private string _saveFolder = "";

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>True while logging is active.</summary>
    public bool IsLogging { get; private set; }

    /// <summary>Path to the folder where CSVs will be saved. Persisted to PlayerPrefs.</summary>
    public string SaveFolder => _saveFolder;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private StreamWriter _writer;
    private float        _sessionStartTime;
    private string       _sessionDate;
    private string       _sessionTime;
    private int          _sessionNumber;

    // In-memory buffer for summary computation (numeric columns only, index matches _numericIndices)
    private readonly List<float[]> _rowBuffer = new List<float[]>();

    // Column indices that feed the summary stats (all numeric log columns)
    // Matches the order in BuildRow(): depth_A, input_x_A, input_y_A, jump_A, tracked_A, fov_A,
    //                                   depth_B, input_x_B, input_y_B, jump_B, tracked_B, fov_B,
    //                                   rms, fish_speed, calibrating
    private const int ColDepthA    = 0;
    private const int ColInputXA   = 1;
    private const int ColInputYA   = 2;
    private const int ColJumpA     = 3;
    private const int ColTrackedA  = 4;
    private const int ColFovA      = 5;
    private const int ColDepthB    = 6;
    private const int ColInputXB   = 7;
    private const int ColInputYB   = 8;
    private const int ColJumpB     = 9;
    private const int ColTrackedB  = 10;
    private const int ColFovB      = 11;
    private const int ColRMS       = 12;
    private const int ColFishSpeed = 13;
    private const int ColCalib     = 14;
    private const int NumCols      = 15;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _saveFolder = PlayerPrefs.GetString(PrefKey, "");
        UpdateRecordingIndicator();
    }

    private void OnApplicationQuit()
    {
        if (IsLogging) StopLogging();
    }

    private void OnDestroy()
    {
        if (IsLogging) StopLogging();
    }

    private void Update()
    {
        UpdateDebugText();
    }

    private void UpdateDebugText()
    {
        if (_debugText == null) return;

        _debugText.text = string.IsNullOrEmpty(_saveFolder) ? "No log folder set" : _saveFolder;
    }

    private void UpdateRecordingIndicator()
    {
        if (_notRecordingIndicator != null)
            _notRecordingIndicator.SetActive(!IsLogging);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a folder picker dialog. The selected path is saved to PlayerPrefs
    /// and remembered for future sessions.
    /// Wire this to a UI button labelled "Choose Log Folder".
    /// </summary>
    public void PickSaveFolder()
    {
        string initialPath = string.IsNullOrEmpty(_saveFolder) ? "" : _saveFolder;

        StandaloneFileBrowser.OpenFolderPanelAsync("Choose Log Folder", initialPath, false, paths =>
        {
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            _saveFolder = paths[0];
            PlayerPrefs.SetString(PrefKey, _saveFolder);
            PlayerPrefs.Save();
            Debug.Log($"[Logger] Save folder set to: {_saveFolder}");
        });
    }

    /// <summary>
    /// Begins logging one row per second to a new CSV file.
    /// Wire this to a UI button labelled "Start Logging".
    /// Does nothing if already logging or if no save folder is set.
    /// </summary>
    public void StartLogging()
    {
        if (IsLogging)
        {
            Debug.LogWarning("[Logger] StartLogging() called while already logging. Ignored.");
            return;
        }

        if (string.IsNullOrEmpty(_saveFolder) || !Directory.Exists(_saveFolder))
        {
            Debug.LogError("[Logger] No valid save folder set. Call PickSaveFolder() first.");
            return;
        }

        DateTime now = DateTime.Now;
        _sessionDate   = now.ToString("yyyy-MM-dd");
        _sessionTime   = now.ToString("HH-mm");
        _sessionNumber = ResolveSessionNumber(_saveFolder, _sessionDate, _sessionTime);

        string filePath = BuildFilePath(_saveFolder, _sessionDate, _sessionTime, _sessionNumber);

        bool writeHeader = !File.Exists(filePath);

        try
        {
            _writer = new StreamWriter(filePath, append: true, encoding: Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Logger] Failed to open log file '{filePath}': {e.Message}");
            return;
        }

        if (writeHeader)
            _writer.WriteLine(BuildHeader());

        _writer.Flush();

        _rowBuffer.Clear();
        _sessionStartTime = Time.time;
        IsLogging = true;
        UpdateRecordingIndicator();

        InvokeRepeating(nameof(LogRow), 0f, 1f);

        Debug.Log($"[Logger] Started logging session {_sessionNumber} → {filePath}");
    }

    /// <summary>
    /// Stops logging, flushes the CSV, and writes the session summary file.
    /// Wire this to a UI button labelled "Stop Logging".
    /// </summary>
    public void StopLogging()
    {
        if (!IsLogging)
        {
            Debug.LogWarning("[Logger] StopLogging() called while not logging. Ignored.");
            return;
        }

        CancelInvoke(nameof(LogRow));
        IsLogging = false;
        UpdateRecordingIndicator();

        _writer?.Flush();
        _writer?.Close();
        _writer = null;

        WriteSummary();

        Debug.Log("[Logger] Logging stopped and summary written.");
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void LogRow()
    {
        if (_writer == null) return;

        float   elapsed = (Time.time - _sessionStartTime) * 1000f;
        float[] numeric = SampleNumericColumns();

        var sb = new StringBuilder();
        sb.Append(elapsed.ToString("F0"));
        foreach (float v in numeric)
        {
            sb.Append(',');
            sb.Append(v.ToString("F4"));
        }

        _writer.WriteLine(sb.ToString());
        _writer.Flush();

        _rowBuffer.Add(numeric);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private static string BuildHeader()
    {
        return "timestamp_ms," +
               "player_A_depth,player_A_input_x,player_A_input_y,player_A_jump,player_A_tracked,fov_A_size," +
               "player_B_depth,player_B_input_x,player_B_input_y,player_B_jump,player_B_tracked,fov_B_size," +
               "ambient_sound_RMS,fish_speed,is_calibrating";
    }

    // ── Sampling ──────────────────────────────────────────────────────────────

    private float[] SampleNumericColumns()
    {
        float[] row = new float[NumCols];

        if (playerA != null)
        {
            row[ColDepthA]   = playerA.WorldZ;
            row[ColInputXA]  = playerA.InputAxis.x;
            row[ColInputYA]  = playerA.InputAxis.y;
            row[ColJumpA]    = playerA.JumpPressed ? 1f : 0f;
            row[ColTrackedA] = playerA.IsTracked   ? 1f : 0f;
        }

        if (lightA != null)
            row[ColFovA] = lightA.CurrentZoom;

        if (playerB != null)
        {
            row[ColDepthB]   = playerB.WorldZ;
            row[ColInputXB]  = playerB.InputAxis.x;
            row[ColInputYB]  = playerB.InputAxis.y;
            row[ColJumpB]    = playerB.JumpPressed ? 1f : 0f;
            row[ColTrackedB] = playerB.IsTracked   ? 1f : 0f;
        }

        if (lightB != null)
            row[ColFovB] = lightB.CurrentZoom;

        if (micDriver != null)
        {
            row[ColRMS]       = micDriver.CurrentRMS;
            row[ColFishSpeed] = micDriver.CurrentSpeed;
            row[ColCalib]     = micDriver.IsCalibrating ? 1f : 0f;
        }

        return row;
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    private void WriteSummary()
    {
        if (string.IsNullOrEmpty(_saveFolder) || !Directory.Exists(_saveFolder))
        {
            Debug.LogWarning("[Logger] Cannot write summary — save folder missing.");
            return;
        }

        string summaryPath = BuildSummaryFilePath(_saveFolder, _sessionDate);
        float  duration    = Time.time - _sessionStartTime;

        // Compute mean and max for each numeric column from the in-memory buffer.
        float[] means = new float[NumCols];
        float[] maxes = new float[NumCols];

        for (int c = 0; c < NumCols; c++)
            maxes[c] = float.MinValue;

        if (_rowBuffer.Count > 0)
        {
            foreach (float[] row in _rowBuffer)
                for (int c = 0; c < NumCols; c++)
                {
                    means[c] += row[c];
                    if (row[c] > maxes[c]) maxes[c] = row[c];
                }

            for (int c = 0; c < NumCols; c++)
                means[c] /= _rowBuffer.Count;
        }
        else
        {
            for (int c = 0; c < NumCols; c++)
                maxes[c] = 0f;
        }

        var header = new StringBuilder();
        var data   = new StringBuilder();

        header.Append("date,session_start,total_duration_s,stations_fixed,sound_condition,");
        header.Append("mean_player_A_depth,max_player_A_depth,");
        header.Append("mean_player_B_depth,max_player_B_depth,");
        header.Append("mean_fov_A_size,max_fov_A_size,");
        header.Append("mean_fov_B_size,max_fov_B_size,");
        header.Append("mean_ambient_RMS,max_ambient_RMS,");
        header.Append("mean_fish_speed,max_fish_speed");

        data.Append(_sessionDate);        data.Append(',');
        data.Append(_sessionTime);        data.Append(',');
        data.Append(duration.ToString("F1")); data.Append(',');
        data.Append("0");                 data.Append(',');   // stations_fixed placeholder
        data.Append("mic_volume");        data.Append(',');

        data.Append(means[ColDepthA].ToString("F4"));   data.Append(',');
        data.Append(maxes[ColDepthA].ToString("F4"));   data.Append(',');
        data.Append(means[ColDepthB].ToString("F4"));   data.Append(',');
        data.Append(maxes[ColDepthB].ToString("F4"));   data.Append(',');
        data.Append(means[ColFovA].ToString("F4"));     data.Append(',');
        data.Append(maxes[ColFovA].ToString("F4"));     data.Append(',');
        data.Append(means[ColFovB].ToString("F4"));     data.Append(',');
        data.Append(maxes[ColFovB].ToString("F4"));     data.Append(',');
        data.Append(means[ColRMS].ToString("F4"));      data.Append(',');
        data.Append(maxes[ColRMS].ToString("F4"));      data.Append(',');
        data.Append(means[ColFishSpeed].ToString("F4")); data.Append(',');
        data.Append(maxes[ColFishSpeed].ToString("F4"));

        try
        {
            File.WriteAllText(summaryPath, header.ToString() + "\n" + data.ToString(), Encoding.UTF8);
            Debug.Log($"[Logger] Summary written → {summaryPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Logger] Failed to write summary '{summaryPath}': {e.Message}");
        }
    }

    // ── File path helpers ─────────────────────────────────────────────────────

    private static int ResolveSessionNumber(string folder, string date, string time)
    {
        int n = 1;
        while (File.Exists(BuildFilePath(folder, date, time, n)))
            n++;
        return n;
    }

    private static string BuildFilePath(string folder, string date, string time, int n)
    {
        return Path.Combine(folder, $"couch_game_{date}_{time}_session{n}.csv");
    }

    private static string BuildSummaryFilePath(string folder, string date, string time, int n)
    {
        return Path.Combine(folder, $"couch_game_{date}_{time}_session{n}_summary.csv");
    }
}
