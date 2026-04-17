using System.Collections;
using UnityEngine;

/// <summary>
/// Drives all visual state changes and animations for the ProblemV2 prefab (TV + CD prop).
///
/// PREFAB WIRING (drag in Inspector):
///   _cdTransform    → Cylinder child Transform
///   _cdRenderer     → Cylinder MeshRenderer
///   _screenRenderer → Screen child MeshRenderer
///   _allRenderers   → all mesh renderers [Cube, Cube(1), Screen, Cylinder]
///
/// STATE COLORS (driven by ProblemObject.UpdateVisuals via Refresh):
///   Idle   — no color override (original material colors)
///   Found  — CD turns yellow
///   Fixed  — CD turns green (PlayFixAnimation then slides CD into TV, screen turns green)
///   Broken — CD turns red then all meshes scatter (PlayBreakAnimation)
///
/// ANIMATION:
///   Fix: CD green → delay → slide CD along _cdSlideLocalOffset → screen green
///   Break: all meshes scatter outward (pure transform animation, no Rigidbodies)
///          then fade out — mirrors BreakableWallObject pattern exactly.
/// </summary>
public class ProblemVisualController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Mesh References")]
    [Tooltip("The Cylinder child Transform (CD disc). Used for slide animation.")]
    [SerializeField] private Transform _cdTransform;

    [Tooltip("The Cylinder child MeshRenderer. Turns yellow/green/red on state changes.")]
    [SerializeField] private Renderer _cdRenderer;

    [Tooltip("The Screen child MeshRenderer. Turns green when fixed.")]
    [SerializeField] private Renderer _screenRenderer;

    [Tooltip("All mesh renderers that scatter on Break: Cube, Cube(1), Screen, Cylinder.")]
    [SerializeField] private Renderer[] _allRenderers;

    [Header("Fix Animation")]
    [Tooltip("Local-space offset the CD travels to slide into the TV. " +
             "Negative X = leftward in local space. Adjust sign/magnitude in Inspector.")]
    [SerializeField] private Vector3 _cdSlideLocalOffset = new Vector3(-1.5f, 0f, 0f);

    [Tooltip("Seconds the slide animation takes.")]
    [SerializeField] private float _cdSlideDuration = 0.8f;

    [Tooltip("Seconds between CD turning green and the slide starting.")]
    [SerializeField] private float _slideDelay = 0.3f;

    [Header("Break Animation")]
    [Tooltip("World-units/sec initial outward speed of scattered meshes.")]
    [SerializeField] private float _fragmentSpeed = 6f;

    [Tooltip("Seconds for fragments to fade out after breaking.")]
    [SerializeField] private float _fadeDuration = 1.5f;

    [Tooltip("0 = fragments fly directly away from blast origin. 1 = full random scatter.")]
    [Range(0f, 1f)]
    [SerializeField] private float _fragmentSpread = 0.4f;

    [Tooltip("Downward acceleration on fragments (world units/sec²).")]
    [SerializeField] private float _fragmentGravity = 4f;

    [Header("Colors")]
    [SerializeField] private Color _foundColor  = Color.yellow;
    [SerializeField] private Color _fixedColor  = Color.green;
    [SerializeField] private Color _brokenColor = Color.red;

    [Header("Screen")]
    [Tooltip("Color the screen shows before the problem is fixed. Black by default.")]
    [SerializeField] private Color _screenIdleColor = Color.black;

    // ── Runtime ───────────────────────────────────────────────────────────────

    // Original CD local position; restored at reset/despawn.
    private Vector3 _cdStartLocalPos;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_cdTransform != null)
            _cdStartLocalPos = _cdTransform.localPosition;

        // Screen starts black regardless of material — set immediately on Awake
        // so it's dark before any state transition is triggered.
        SetColor(_screenRenderer, _screenIdleColor);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ProblemObject after every state transition to update visual appearance.
    /// Does NOT start animations — those are started explicitly via PlayFixAnimation / PlayBreakAnimation.
    /// </summary>
    public void Refresh(ProblemState state)
    {
        switch (state)
        {
            case ProblemState.Idle:
                ClearColor(_cdRenderer);
                SetColor(_screenRenderer, _screenIdleColor); // screen stays black on reset
                break;

            case ProblemState.Found:
                SetColor(_cdRenderer, _foundColor);
                break;

            case ProblemState.Fixed:
                SetColor(_cdRenderer, _fixedColor);
                break;

            case ProblemState.Broken:
                SetColor(_cdRenderer, _brokenColor);
                break;
        }
    }

    /// <summary>
    /// Plays the Fix animation: CD pauses green, then slides along _cdSlideLocalOffset,
    /// then the Screen turns green. Returns the Coroutine so ProblemObject can yield on it.
    /// </summary>
    public Coroutine PlayFixAnimation() => StartCoroutine(FixRoutine());

    /// <summary>
    /// Plays the malfunction animation: CD slides into TV (same motion as Fix),
    /// but the screen turns red. Used when a player incorrectly Fixes a dud problem.
    /// Returns the Coroutine so ProblemObject can yield on it.
    /// </summary>
    public Coroutine PlayMalfunctionAnimation() => StartCoroutine(MalfunctionRoutine());

    /// <summary>
    /// Scatters all _allRenderers outward from blastOrigin using pure transform animation
    /// (no Rigidbodies — identical approach to BreakableWallObject). Fragments fade and stop.
    /// </summary>
    public void PlayBreakAnimation(Vector3 blastOrigin) => StartCoroutine(BreakRoutine(blastOrigin));

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator FixRoutine()
    {
        // CD is already green from Refresh(Fixed) called before this runs.
        yield return new WaitForSeconds(_slideDelay);

        if (_cdTransform != null)
        {
            Vector3 startPos = _cdTransform.localPosition;
            Vector3 endPos   = startPos + _cdSlideLocalOffset;

            for (float t = 0f; t < _cdSlideDuration; t += Time.deltaTime)
            {
                if (_cdTransform == null) yield break;
                _cdTransform.localPosition = Vector3.Lerp(startPos, endPos, t / _cdSlideDuration);
                yield return null;
            }

            if (_cdTransform != null)
                _cdTransform.localPosition = endPos;
        }

        // Screen turns green once CD is fully inside.
        SetColor(_screenRenderer, _fixedColor);
    }

    private IEnumerator MalfunctionRoutine()
    {
        // CD is already red from Refresh(Broken) called before this runs.
        yield return new WaitForSeconds(_slideDelay);

        if (_cdTransform != null)
        {
            Vector3 startPos = _cdTransform.localPosition;
            Vector3 endPos   = startPos + _cdSlideLocalOffset;

            for (float t = 0f; t < _cdSlideDuration; t += Time.deltaTime)
            {
                if (_cdTransform == null) yield break;
                _cdTransform.localPosition = Vector3.Lerp(startPos, endPos, t / _cdSlideDuration);
                yield return null;
            }

            if (_cdTransform != null)
                _cdTransform.localPosition = endPos;
        }

        // Screen turns red — the "fix" made it worse.
        SetColor(_screenRenderer, _brokenColor);
    }

    private IEnumerator BreakRoutine(Vector3 blastOrigin)
    {
        if (_allRenderers == null || _allRenderers.Length == 0) yield break;

        // Direction from blast origin toward this object center; fragments fly outward.
        Vector3 blastDir = transform.position - blastOrigin;
        blastDir.z = 0f;
        if (blastDir.sqrMagnitude < 0.0001f) blastDir = Vector3.right;
        blastDir.Normalize();

        foreach (Renderer rend in _allRenderers)
        {
            if (rend == null) continue;

            // Random scatter cone blended with blast direction — same formula as BreakableWallObject.
            Vector3 scatter = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                0f).normalized;
            Vector3 dir = Vector3.Lerp(blastDir, scatter, _fragmentSpread).normalized;
            dir.z = 0f;

            StartCoroutine(ScatterMesh(rend.transform, dir * _fragmentSpeed));
            StartCoroutine(FadeMesh(rend));
        }
    }

    private IEnumerator ScatterMesh(Transform t, Vector3 initialVelocity)
    {
        Vector3 velocity = initialVelocity;
        float   elapsed  = 0f;

        while (elapsed < _fadeDuration && t != null)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            velocity.y -= _fragmentGravity * dt;
            t.position += velocity * dt;
            t.Rotate(velocity.normalized * 120f * dt, Space.World);

            yield return null;
        }
    }

    private IEnumerator FadeMesh(Renderer rend)
    {
        if (rend == null) yield break;

        // Read the current base color from the property block if one was set,
        // otherwise fall back to the shared material color.
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        Color startColor = mpb.GetColor("_BaseColor");
        if (startColor == default)
        {
            startColor = rend.sharedMaterial != null
                ? rend.sharedMaterial.GetColor("_BaseColor")
                : Color.white;
        }

        for (float elapsed = 0f; elapsed < _fadeDuration; elapsed += Time.deltaTime)
        {
            if (rend == null) yield break;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / _fadeDuration);
            mpb.SetColor("_BaseColor", new Color(startColor.r, startColor.g, startColor.b, alpha));
            rend.SetPropertyBlock(mpb);
            yield return null;
        }

        if (rend != null)
            rend.gameObject.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SetColor(Renderer rend, Color color)
    {
        if (rend == null) return;
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", color);
        rend.SetPropertyBlock(mpb);
    }

    private static void ClearColor(Renderer rend)
    {
        if (rend == null) return;
        // Setting an empty property block removes overrides, restoring shared material colors.
        rend.SetPropertyBlock(new MaterialPropertyBlock());
    }
}
