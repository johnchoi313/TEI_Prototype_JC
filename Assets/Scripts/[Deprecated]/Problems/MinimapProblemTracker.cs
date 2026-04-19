using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a minimap icon for a single ProblemObject.
///
/// Initialized by ProblemManager at spawn time with a pre-instantiated
/// UI Image (already parented in the minimap's icon layer). Every LateUpdate,
/// the icon's position is updated via MinimapController.WorldToMap() and its
/// sprite/color reflects the current ProblemState.
///
/// The icon is hidden while the problem is Idle (undiscovered).
/// </summary>
[RequireComponent(typeof(ProblemObject))]
public class MinimapProblemTracker : MonoBehaviour
{
    // ── Runtime ───────────────────────────────────────────────────────────────

    private ProblemObject     _problem;
    private Image             _icon;
    private MinimapController _map;
    private bool              _initialized;
    private ProblemState      _lastState = ProblemState.Idle;

    // ── Called by ProblemManager after instantiation ──────────────────────────

    public void Initialize(Image iconInstance)
    {
        _problem     = GetComponent<ProblemObject>();
        _icon        = iconInstance;
        _map         = FindAnyObjectByType<MinimapController>();
        _initialized = true;

        // Start hidden until the problem is found.
        if (_icon != null) _icon.gameObject.SetActive(false);
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        // Clean up the icon from the canvas when the problem despawns.
        if (_icon != null) Destroy(_icon.gameObject);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a world position to 0–1 UV space on MapContainer.
    /// WorldToMap() returns an anchoredPosition centered at (0,0);
    /// we remap that to (0,1) so the shader receives correct UV coordinates.
    /// </summary>
    /// <summary>
    /// Converts a world position to 0–1 UV space that matches the minimap render texture.
    /// Uses WorldBounds directly — the same reference frame MinimapController uses when
    /// setting up the minimap camera, so UV (0,0) = world (xMin,yMin) and (1,1) = (xMax,yMax).
    /// This is intentionally NOT derived from the RectTransform rect, which can be
    /// uninitialized at the time the first ripple fires.
    /// </summary>
    private Vector2 WorldToMapUV(Vector3 worldPos)
    {
        if (_map == null) return new Vector2(0.5f, 0.5f);

        Rect bounds = _map.WorldBounds;
        return new Vector2(
            Mathf.InverseLerp(bounds.xMin, bounds.xMax, worldPos.x),
            Mathf.InverseLerp(bounds.yMin, bounds.yMax, worldPos.y));
    }

    private void LateUpdate()
    {
        if (!_initialized || _icon == null || _map == null || _problem == null) return;

        ProblemState state = _problem.State;

        // Fire ripple on the first frame the problem transitions Idle → Found.
        if (state == ProblemState.Found && _lastState == ProblemState.Idle)
        {
            MinimapRippleDriver driver = MinimapRippleDriver.Instance;
            if (driver != null)
                driver.TriggerRipple(WorldToMapUV(transform.position));
        }
        _lastState = state;

        if (state == ProblemState.Idle)
        {
            _icon.gameObject.SetActive(false);
            return;
        }

        _icon.gameObject.SetActive(true);

        // Position in minimap space.
        _icon.rectTransform.anchoredPosition = _map.WorldToMap(transform.position);

        // Color and sprite based on state.
        ProblemDefinition def = _problem.Definition;
        if (def != null)
        {
            if (def.minimapIcon != null) _icon.sprite = def.minimapIcon;

            _icon.color = state switch
            {
                ProblemState.Found  => def.minimapFoundColor,
                ProblemState.Fixed  => def.minimapFixedColor,
                ProblemState.Broken => def.minimapBrokenColor,
                _                   => Color.white,
            };
        }
    }
}
