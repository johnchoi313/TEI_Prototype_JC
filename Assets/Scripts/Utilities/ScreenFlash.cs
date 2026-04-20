using UnityEngine;

/// <summary>
/// Singleton screen-flash effect. Call ScreenFlash.Instance.Flash() from anywhere.
///
/// Draws a white fullscreen quad via GL immediately after the camera renders —
/// no Canvas, no Image, no UI setup required.
///
/// SCENE SETUP
///   1. Attach this component to any camera in the scene (or a dedicated
///      GameObject — it will find Camera.main automatically if no camera
///      is assigned).
///   2. Optionally assign a specific camera in the inspector.
///   3. Call ScreenFlash.Instance.Flash() from any script.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ScreenFlash : MonoBehaviour
{
    public static ScreenFlash Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Seconds to fade in from 0 → peak alpha.")]
    [SerializeField] private float _fadeInDuration  = 0.05f;

    [Tooltip("Seconds to hold at peak alpha.")]
    [SerializeField] private float _holdDuration    = 0.05f;

    [Tooltip("Seconds to fade out from peak alpha → 0.")]
    [SerializeField] private float _fadeOutDuration = 0.25f;

    [Tooltip("Peak alpha of the flash (0 = invisible, 1 = fully opaque white).")]
    [Range(0f, 1f)]
    [SerializeField] private float _peakAlpha = 0.85f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private float _timer;
    private float _currentAlpha;
    private bool  _active;

    private enum FlashPhase { FadeIn, Hold, FadeOut }
    private FlashPhase _phase;

    private static Material _glMat;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Simple unlit vertex-color shader — always available in Unity.
        _glMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        _glMat.hideFlags = HideFlags.HideAndDontSave;
        _glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _glMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _glMat.SetInt("_ZWrite",   0);
    }

    private void Update()
    {
        if (!_active) return;

        _timer += Time.deltaTime;

        switch (_phase)
        {
            case FlashPhase.FadeIn:
                _currentAlpha = Mathf.Lerp(0f, _peakAlpha, _timer / Mathf.Max(_fadeInDuration, 0.0001f));
                if (_timer >= _fadeInDuration) Advance(FlashPhase.Hold);
                break;

            case FlashPhase.Hold:
                _currentAlpha = _peakAlpha;
                if (_timer >= _holdDuration) Advance(FlashPhase.FadeOut);
                break;

            case FlashPhase.FadeOut:
                _currentAlpha = Mathf.Lerp(_peakAlpha, 0f, _timer / Mathf.Max(_fadeOutDuration, 0.0001f));
                if (_timer >= _fadeOutDuration) { _currentAlpha = 0f; _active = false; }
                break;
        }
    }

    private void OnPostRender()
    {
        if (!_active || _currentAlpha <= 0f) return;

        GL.PushMatrix();
        GL.LoadOrtho();
        _glMat.SetPass(0);

        GL.Begin(GL.QUADS);
        GL.Color(new Color(1f, 1f, 1f, _currentAlpha));
        GL.Vertex3(0f, 0f, 0f);
        GL.Vertex3(0f, 1f, 0f);
        GL.Vertex3(1f, 1f, 0f);
        GL.Vertex3(1f, 0f, 0f);
        GL.End();

        GL.PopMatrix();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Triggers a white screen flash. Safe to call mid-flash — restarts cleanly.</summary>
    public void Flash()
    {
        _phase        = FlashPhase.FadeIn;
        _timer        = 0f;
        _currentAlpha = 0f;
        _active       = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Advance(FlashPhase phase) { _phase = phase; _timer = 0f; }
}
