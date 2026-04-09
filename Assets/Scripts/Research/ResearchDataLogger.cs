using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records gameplay telemetry to a local CSV while GameState == Playing.
///
/// DATA COLLECTED
///   Per sample row (every sampleIntervalSeconds):
///     timestamp_s          — elapsed seconds since Playing began
///     p1_fov_radius        — P1 hand FOV circle radius (0-1 screen space)
///     p2_fov_radius        — P2 hand FOV circle radius (0-1 screen space)
///     sound_battery        — ambient noise battery level (0-1; drives character speed)
///     hand_proximity_uv    — aspect-corrected distance between both hands in canvas UV
///     p1_power_activations — cumulative P1 power-up activations this round
///     p2_power_activations — cumulative P2 power-up activations this round
///     p1_in_fov            — TRUE if P1's fish is inside P1's FOV circle at this moment
///     p2_in_fov            — TRUE if P2's fish is inside P2's FOV circle at this moment
///     swap_count           — cumulative power-up swaps this round
///     swap_timestamps      — semicolon-separated timestamps of swaps since the last row
///
///   Summary section (appended after PlayingEnds):
///     total_play_time_s, p1_problems_fixed, p1_problems_broken,
///     p2_problems_fixed, p2_problems_broken, total_swaps,
///     p1_pct_in_fov, p2_pct_in_fov
///
/// SETUP
///   1. Attach to a persistent Manager GameObject (or the ResearchPrefabIntegration prefab).
///   2. Optionally drag scene fish GameObjects into _p1Fish / _p2Fish.
///      If left empty, the logger resolves them via PlayerManager.GetPlayerInstance().
///   3. Set sampleIntervalSeconds in the Inspector (default 5 s).
///   4. CSV is written to ~/Desktop/TEI_Sessions/ on session end.
/// </summary>
public class ResearchDataLogger : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Sampling")]
    [Tooltip("How often (seconds) a data row is written to the CSV buffer.")]
    public float sampleIntervalSeconds = 5f;

    [Header("Fish References (optional — resolved via PlayerManager if empty)")]
    [SerializeField] private GameObject _p1Fish;
    [SerializeField] private GameObject _p2Fish;

    [Header("Save Location")]
    [Tooltip("Folder where CSV files are saved. Leave blank to use ~/Desktop/TEI_Sessions/.")]
    [SerializeField] private string saveFolder = "";

    // ── Accumulators ──────────────────────────────────────────────────────────

    private float _sessionElapsed;
    private bool  _isRecording;

    // Counted via events
    private int _p1Activations;
    private int _p2Activations;
    private int _swapCount;
    private int _p1Broken;
    private int _p2Broken;

    // FOV presence — tracked every frame for accurate % calculation
    private long _p1InFOVFrames;
    private long _p2InFOVFrames;
    private long _p1TotalFrames;
    private long _p2TotalFrames;

    // Swap timestamps that arrived since the last RecordSample call
    private readonly List<float> _pendingSwapTimestamps = new List<float>();

    // Buffered CSV rows (written to disk all at once on flush)
    private readonly List<string> _rows = new List<string>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (string.IsNullOrEmpty(saveFolder))
            saveFolder = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
                "TEI_Sessions");
    }

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void OnEnable()
    {
        PowerUpManager.OnPowerUpActivated += HandleActivation;
        PowerUpManager.OnPowerUpSwapped   += HandleSwap;
        ProblemObject.OnProblemBroken     += HandleBroken;
    }

    private void OnDisable()
    {
        PowerUpManager.OnPowerUpActivated -= HandleActivation;
        PowerUpManager.OnPowerUpSwapped   -= HandleSwap;
        ProblemObject.OnProblemBroken     -= HandleBroken;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (!_isRecording) return;

        _sessionElapsed += Time.deltaTime;

        // Frame-accurate FOV presence tracking
        ResolveFishRefs();
        var left  = FOVWorldCollider.Instance != null ? FOVWorldCollider.Instance.LeftHand  : default;
        var right = FOVWorldCollider.Instance != null ? FOVWorldCollider.Instance.RightHand : default;

        Camera p1Cam = GetCameraForPlayer(PlayerIndex.Player1);
        Camera p2Cam = GetCameraForPlayer(PlayerIndex.Player2);

        _p1TotalFrames++;
        _p2TotalFrames++;
        if (IsCharacterInFOV(_p1Fish, left,  p1Cam)) _p1InFOVFrames++;
        if (IsCharacterInFOV(_p2Fish, right, p2Cam)) _p2InFOVFrames++;
    }

    // ── State handling ─────────────────────────────────────────────────────────

    private void HandleStateChanged(GameState prev, GameState next)
    {
        if (next == GameState.Playing)
            StartRecording();
        else if (prev == GameState.Playing)
            StopRecording();
    }

    private void StartRecording()
    {
        ResetAccumulators();
        ResolveFishRefs();
        _isRecording = true;
        InvokeRepeating(nameof(RecordSample), 0f, sampleIntervalSeconds);
        Debug.Log("[ResearchDataLogger] Recording started.");
    }

    private void StopRecording()
    {
        _isRecording = false;
        CancelInvoke(nameof(RecordSample));
        FlushCSV();
        Debug.Log("[ResearchDataLogger] Recording stopped — CSV saved.");
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleActivation(PlayerIndex player, PowerUpDefinition def)
    {
        if (!_isRecording) return;
        if (player == PlayerIndex.Player1) _p1Activations++;
        else                               _p2Activations++;
    }

    private void HandleSwap()
    {
        if (!_isRecording) return;
        _swapCount++;
        _pendingSwapTimestamps.Add(_sessionElapsed);
    }

    private void HandleBroken(PlayerIndex player)
    {
        if (!_isRecording) return;
        if (player == PlayerIndex.Player1) _p1Broken++;
        else                               _p2Broken++;
    }

    // ── Sampling ──────────────────────────────────────────────────────────────

    private void RecordSample()
    {
        if (!_isRecording) return;

        // Snapshot instantaneous values
        float p1Radius      = FOVWorldCollider.Instance != null ? FOVWorldCollider.Instance.LeftHand.ScreenRadius  : 0f;
        float p2Radius      = FOVWorldCollider.Instance != null ? FOVWorldCollider.Instance.RightHand.ScreenRadius : 0f;
        float soundBattery  = AmbientNoiseSampler.Instance != null ? AmbientNoiseSampler.Instance.BatteryLevel : 0f;
        float proximity     = ComputeHandProximity();

        var left  = FOVWorldCollider.Instance != null ? FOVWorldCollider.Instance.LeftHand  : default;
        var right = FOVWorldCollider.Instance != null ? FOVWorldCollider.Instance.RightHand : default;
        Camera p1Cam = GetCameraForPlayer(PlayerIndex.Player1);
        Camera p2Cam = GetCameraForPlayer(PlayerIndex.Player2);
        bool p1InFOV = IsCharacterInFOV(_p1Fish, left,  p1Cam);
        bool p2InFOV = IsCharacterInFOV(_p2Fish, right, p2Cam);

        // Collect swap timestamps that happened since the last sample
        string swapTs = _pendingSwapTimestamps.Count > 0
            ? string.Join(";", _pendingSwapTimestamps.ConvertAll(t => t.ToString("F1")))
            : "";
        _pendingSwapTimestamps.Clear();

        _rows.Add(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}",
            _sessionElapsed.ToString("F1"),
            p1Radius.ToString("F4"),
            p2Radius.ToString("F4"),
            soundBattery.ToString("F4"),
            proximity.ToString("F4"),
            _p1Activations,
            _p2Activations,
            p1InFOV ? "TRUE" : "FALSE",
            p2InFOV ? "TRUE" : "FALSE",
            _swapCount,
            swapTs));
    }

    // ── CSV flush ─────────────────────────────────────────────────────────────

    private void FlushCSV()
    {
        try
        {
            Directory.CreateDirectory(saveFolder);

            string scene    = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string now      = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"TEI_{now}_{scene}.csv";
            string path     = Path.Combine(saveFolder, filename);

            float roundDuration = RoundManager.Instance != null ? RoundManager.Instance.roundDuration : 0f;

            var sb = new StringBuilder();

            // ── Header comments
            sb.AppendLine("# TEI Research Session");
            sb.AppendLine($"# Date: {DateTime.Now:yyyy-MM-dd}  Time: {DateTime.Now:HH:mm:ss}");
            sb.AppendLine($"# Scene: {scene}");
            sb.AppendLine($"# Sample Interval (s): {sampleIntervalSeconds}");
            sb.AppendLine($"# Round Duration (s): {roundDuration}");
            sb.AppendLine("#");

            // ── Column headers
            sb.AppendLine("timestamp_s,p1_fov_radius,p2_fov_radius,sound_battery,hand_proximity_uv," +
                          "p1_power_activations,p2_power_activations,p1_in_fov,p2_in_fov," +
                          "swap_count,swap_timestamps");

            // ── Data rows
            foreach (string row in _rows)
                sb.AppendLine(row);

            // ── Summary section
            float p1PctFOV = _p1TotalFrames > 0 ? (_p1InFOVFrames / (float)_p1TotalFrames) * 100f : 0f;
            float p2PctFOV = _p2TotalFrames > 0 ? (_p2InFOVFrames / (float)_p2TotalFrames) * 100f : 0f;

            int p1Fixed = ScoreManager.Instance != null ? ScoreManager.Instance.P1ProblemsSolved : 0;
            int p2Fixed = ScoreManager.Instance != null ? ScoreManager.Instance.P2ProblemsSolved : 0;

            sb.AppendLine();
            sb.AppendLine("[SUMMARY]");
            sb.AppendLine("total_play_time_s,p1_problems_fixed,p1_problems_broken," +
                          "p2_problems_fixed,p2_problems_broken,total_swaps," +
                          "p1_pct_in_fov,p2_pct_in_fov");
            sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                _sessionElapsed.ToString("F1"),
                p1Fixed, _p1Broken,
                p2Fixed, _p2Broken,
                _swapCount,
                p1PctFOV.ToString("F1"),
                p2PctFOV.ToString("F1")));

            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[ResearchDataLogger] CSV saved → {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ResearchDataLogger] Failed to write CSV: {e.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResetAccumulators()
    {
        _sessionElapsed = 0f;
        _p1Activations  = 0;
        _p2Activations  = 0;
        _swapCount      = 0;
        _p1Broken       = 0;
        _p2Broken       = 0;
        _p1InFOVFrames  = 0;
        _p2InFOVFrames  = 0;
        _p1TotalFrames  = 0;
        _p2TotalFrames  = 0;
        _pendingSwapTimestamps.Clear();
        _rows.Clear();
    }

    private void ResolveFishRefs()
    {
        if (_p1Fish == null && PlayerManager.Instance != null)
            _p1Fish = PlayerManager.Instance.GetPlayerInstance(PlayerIndex.Player1);
        if (_p2Fish == null && PlayerManager.Instance != null)
            _p2Fish = PlayerManager.Instance.GetPlayerInstance(PlayerIndex.Player2);
    }

    /// <summary>
    /// Returns true if the fish's viewport position is inside the hand's FOV circle.
    /// Replicates FOVHighlightable.IsOverlappedBy() (which is private).
    /// </summary>
    private static bool IsCharacterInFOV(GameObject fish, FOVWorldCollider.HandWorldState hand, Camera cam)
    {
        if (fish == null || cam == null) return false;
        if (!hand.IsActive || hand.IsGhost) return false;

        Vector3 vp = cam.WorldToViewportPoint(fish.transform.position);
        if (vp.z < 0f) return false;

        Vector2 delta = new Vector2(vp.x, vp.y) - hand.ViewportPosition;
        delta.x *= (float)Screen.width / Screen.height;
        return delta.magnitude < hand.ScreenRadius;
    }

    /// <summary>
    /// Aspect-ratio corrected distance between both hands in canvas UV space.
    /// Mirrors PowerUpManager.TickSwap() proximity calculation.
    /// </summary>
    private static float ComputeHandProximity()
    {
        if (TEIHandTrackingShaderBridge.Instance == null) return 1f;
        Vector2 delta = TEIHandTrackingShaderBridge.Instance.CurrentLeftPosCanvas
                      - TEIHandTrackingShaderBridge.Instance.CurrentRightPosCanvas;
        delta.x *= (float)Screen.width / Screen.height;
        return delta.magnitude;
    }

    private static Camera GetCameraForPlayer(PlayerIndex player)
    {
        if (SplitScreenController.Instance != null)
            return SplitScreenController.Instance.GetCameraForPlayer(player);
        return Camera.main;
    }
}
