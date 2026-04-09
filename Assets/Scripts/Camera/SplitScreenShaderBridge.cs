using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Feeds split-screen state into the FOV_Mask shader material each frame.
///
/// WHAT THIS DOES:
///   The FOV_Mask shader handles both the split compositor (P1/P2 camera
///   render textures) and the FOV circles in one full-screen pass. This
///   script keeps the shader's SplitProgress, P1IsOnLeft, and camera texture
///   properties in sync with SplitScreenController's runtime state.
///
/// SHADER SIDE SETUP (in FOV_Mask_v4_Split shader graph):
///   Add a Float property "_P1IsOnLeft" (default 1).
///   Before the final Lerp's T input:
///     OneMinus(step) → Lerp(A=OneMinus(step), B=step, T=_P1IsOnLeft) → final Lerp T
///   When _P1IsOnLeft=1: T=step → P1 on left, P2 on right (normal)
///   When _P1IsOnLeft=0: T=1-step → P1 on right, P2 on left (flipped)
///
/// SCENE SETUP:
///   1. Add to any persistent GameObject (e.g. SplitScreenController).
///   2. Assign targetGraphic — the RawImage that displays the FOV shader.
///   3. Assign the four render textures from P1/P2 camera rigs.
///      P1_MainTex  = P1 RevealCamera output
///      P1_CoverTex = P1 MaskCamera output
///      P2_MainTex  = P2 RevealCamera output
///      P2_CoverTex = P2 MaskCamera output
/// </summary>
public class SplitScreenShaderBridge : MonoBehaviour
{
    [Header("Shader Target")]
    [Tooltip("The RawImage whose material is the FOV_Mask shader.")]
    [SerializeField] private Graphic targetGraphic;

    [Header("P1 Render Textures")]
    [SerializeField] private RenderTexture p1MainTex;
    [SerializeField] private RenderTexture p1CoverTex;

    [Header("P2 Render Textures")]
    [SerializeField] private RenderTexture p2MainTex;
    [SerializeField] private RenderTexture p2CoverTex;

    // Cached property IDs — set once, reuse every frame.
    private static readonly int _splitProgress = Shader.PropertyToID("_SplitProgress");
    private static readonly int _p1IsOnLeft    = Shader.PropertyToID("_P1IsOnLeft");
    private static readonly int _p1MainTex     = Shader.PropertyToID("_P1_MainTex");
    private static readonly int _p1CoverTex    = Shader.PropertyToID("_P1_CoverTex");
    private static readonly int _p2MainTex     = Shader.PropertyToID("_P2_MainTex");
    private static readonly int _p2CoverTex    = Shader.PropertyToID("_P2_CoverTex");

    private Material _mat;

    private void Start()
    {
        if (targetGraphic == null)
        {
            Debug.LogError("[SplitScreenShaderBridge] targetGraphic not assigned.", this);
            enabled = false;
            return;
        }

        // TEIHandTrackingShaderBridge already created a runtime material instance
        // and assigned it to targetGraphic. Reuse that same instance so both
        // bridges write to the same material — creating a new one here would
        // replace the hand-tracking bridge's target and break FOV circles.
        var handBridge = FindAnyObjectByType<TEIHandTrackingShaderBridge>();
        if (handBridge != null)
        {
            _mat = handBridge.GetRuntimeMaterial();
        }
        else
        {
            // Fallback: no hand bridge found, create our own instance.
            Debug.LogWarning("[SplitScreenShaderBridge] TEIHandTrackingShaderBridge not found. " +
                             "Creating standalone material instance.", this);
            _mat = new Material(targetGraphic.materialForRendering);
            targetGraphic.material = _mat;
        }

        if (_mat == null)
        {
            Debug.LogError("[SplitScreenShaderBridge] Could not resolve shader material.", this);
            enabled = false;
            return;
        }

        // Set static texture references once — these never change at runtime.
        if (p1MainTex  != null) _mat.SetTexture(_p1MainTex,  p1MainTex);
        if (p1CoverTex != null) _mat.SetTexture(_p1CoverTex, p1CoverTex);
        if (p2MainTex  != null) _mat.SetTexture(_p2MainTex,  p2MainTex);
        if (p2CoverTex != null) _mat.SetTexture(_p2CoverTex, p2CoverTex);
    }

    private void Update()
    {
        if (_mat == null) return;

        float progress = SplitScreenController.Instance != null
            ? SplitScreenController.Instance.SplitProgress
            : 0f;

        float p1OnLeft = SplitScreenController.Instance != null
            ? (SplitScreenController.Instance.P1IsOnLeft ? 1f : 0f)
            : 1f;

        _mat.SetFloat(_splitProgress, progress);
        _mat.SetFloat(_p1IsOnLeft,    p1OnLeft);
    }
}
