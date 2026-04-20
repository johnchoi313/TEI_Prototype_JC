using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Feeds CircularCameraController's runtime state into the UI shader material
/// every frame so the GPU can composite two circular camera views with a
/// proximity-driven merge effect.
///
/// SHADER PROPERTIES THIS SCRIPT DRIVES:
///   _P1_MainTex        (Texture2D) — P1 camera render texture
///   _P2_MainTex        (Texture2D) — P2 camera render texture
///   _P1_CircleCenter   (Vector)    — screen UV centre of P1's circle (xy used)
///   _P2_CircleCenter   (Vector)    — screen UV centre of P2's circle (xy used)
///   _CircleRadius      (Float)     — normalised screen-space radius (0–1)
///   _MergeProgress     (Float)     — 0 = two separate circles, 1 = fully merged
///
/// SCENE SETUP:
///   1. Add this component to the same GameObject as CircularCameraController,
///      or any persistent manager object.
///   2. Assign targetGraphic — the RawImage / Image whose material is the
///      circular-camera composite shader.
///   3. CircularCameraController.Instance is resolved automatically at Start.
///      No direct reference needed.
///
/// SHADER AUTHORING NOTES (Unity Shader Graph or hand-written):
///   • Sample _P1_MainTex at UV, sample _P2_MainTex at UV.
///   • Compute distance from fragment UV to _P1_CircleCenter.xy and to
///     _P2_CircleCenter.xy (accounting for screen aspect: multiply X distance
///     by _ScreenParams.x/_ScreenParams.y before length()).
///   • Each circle mask = 1 - smoothstep(_CircleRadius - edge, _CircleRadius, dist).
///   • In merge mode (_MergeProgress > 0) blend the two circle masks and
///     crossfade between the two camera textures using _MergeProgress as T.
///   • Outside both circles render transparent black or a background texture.
/// </summary>
public class CircularCameraShaderBridge : MonoBehaviour
{
    [Header("Shader Target")]
    [Tooltip("The UI Graphic (RawImage) whose material is the circular-camera composite shader.")]
    [SerializeField] private Graphic _targetGraphic;

    [Tooltip("If true a new material instance is created from the graphic's shared material " +
             "on Start, so this script owns the instance. Disable if another bridge already " +
             "created the instance (e.g. when sharing with a hand-tracking bridge).")]
    [SerializeField] private bool _ownMaterialInstance = true;

    // Cached shader property IDs.
    private static readonly int _propP1Tex          = Shader.PropertyToID("_P1_MainTex");
    private static readonly int _propP2Tex          = Shader.PropertyToID("_P2_MainTex");
    private static readonly int _propP1Center       = Shader.PropertyToID("_P1_CircleCenter");
    private static readonly int _propP2Center       = Shader.PropertyToID("_P2_CircleCenter");
    private static readonly int _propCircleRadius   = Shader.PropertyToID("_CircleRadius");
    private static readonly int _propMergeProgress  = Shader.PropertyToID("_MergeProgress");

    private Material _mat;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (_targetGraphic == null)
        {
            Debug.LogError("[CircularCameraShaderBridge] targetGraphic not assigned.", this);
            enabled = false;
            return;
        }

        if (_ownMaterialInstance)
        {
            _mat = new Material(_targetGraphic.materialForRendering);
            _targetGraphic.material = _mat;
        }
        else
        {
            _mat = _targetGraphic.materialForRendering;
        }

        if (_mat == null)
        {
            Debug.LogError("[CircularCameraShaderBridge] Could not resolve material.", this);
            enabled = false;
            return;
        }

        // Bind render textures once — they are static references.
        if (CircularCameraController.Instance != null)
        {
            if (CircularCameraController.Instance.P1RenderTexture != null)
                _mat.SetTexture(_propP1Tex, CircularCameraController.Instance.P1RenderTexture);
            if (CircularCameraController.Instance.P2RenderTexture != null)
                _mat.SetTexture(_propP2Tex, CircularCameraController.Instance.P2RenderTexture);
        }
        else
        {
            Debug.LogWarning("[CircularCameraShaderBridge] CircularCameraController.Instance is null at Start. " +
                             "Textures will be bound on first Update instead.", this);
        }
    }

    private bool _texturesBound = false;

    private void Update()
    {
        if (_mat == null) return;

        var ctrl = CircularCameraController.Instance;
        if (ctrl == null) return;

        // Lazy-bind textures if they weren't available at Start.
        if (!_texturesBound)
        {
            if (ctrl.P1RenderTexture != null) _mat.SetTexture(_propP1Tex, ctrl.P1RenderTexture);
            if (ctrl.P2RenderTexture != null) _mat.SetTexture(_propP2Tex, ctrl.P2RenderTexture);
            _texturesBound = ctrl.P1RenderTexture != null && ctrl.P2RenderTexture != null;
        }

        _mat.SetVector(_propP1Center,      new Vector4(ctrl.P1CircleCenter.x, ctrl.P1CircleCenter.y, 0f, 0f));
        _mat.SetVector(_propP2Center,      new Vector4(ctrl.P2CircleCenter.x, ctrl.P2CircleCenter.y, 0f, 0f));
        _mat.SetFloat (_propCircleRadius,  ctrl.CircleRadius);
        _mat.SetFloat (_propMergeProgress, ctrl.MergeProgress);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the managed material instance. Use this if a second bridge needs
    /// to share the same material (e.g. a hand-tracking FOV bridge).
    /// </summary>
    public Material GetRuntimeMaterial() => _mat;
}
