using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central minimap orchestrator.
///
/// Attach to the Scripts child of the Minimap prefab.
/// Wire _mapContainer, _p1Tracker, and _p2Tracker in the Inspector.
///
/// LEVEL SETUP
///   Place a MinimapBoundsMarker on any GameObject in the level scene and set
///   its worldBounds to cover the playable area.  MinimapController discovers
///   it automatically at Start.  If no marker is present the controller falls
///   back to _fallbackWorldBounds, which you can set per-prefab for quick
///   testing without a dedicated level scene.
///
/// MINIMAP CAMERA
///   Optionally wire _minimapCamera and _mapBackground to get a live render of
///   level geometry on the map background.  MinimapController creates a
///   RenderTexture at runtime with the correct aspect ratio (derived from
///   worldBounds) and configures the camera to cover exactly those bounds.
///   Leave both fields empty to keep the plain background image.
///
/// COORDINATE MAPPING
///   WorldToMap  — maps a world XY position to an anchoredPosition inside
///                 _mapContainer.  Works for any map size or world size.
///   WorldRadiusToMap — maps a world-space radius to a uniform UI pixel size
///                      using the X axis as the reference scale, so FOV
///                      circles stay circular rather than becoming ovals.
/// </summary>
public class MinimapController : MonoBehaviour
{
    public static MinimapController Instance { get; private set; }

    [Header("Map UI")]
    [Tooltip("The RectTransform that represents the playable area on the map. " +
             "All icon anchoredPositions are relative to this rect's center.")]
    [SerializeField] private RectTransform _mapContainer;

    [Header("Player Trackers")]
    [SerializeField] private MinimapPlayerTracker _p1Tracker;
    [SerializeField] private MinimapPlayerTracker _p2Tracker;

    [Header("Map Sizing")]
    [Tooltip("Pixel size of the map's longest dimension. " +
             "The shorter dimension is derived automatically from the world bounds aspect ratio. " +
             "Adjust this one value to scale the whole map up or down.")]
    [SerializeField] private float _mapMaxSize = 200f;

    [Header("World Bounds Fallback")]
    [Tooltip("Used when no MinimapBoundsMarker is found in the active scene. " +
             "X/Y = bottom-left corner, Width/Height = dimensions in world units.")]
    [SerializeField] private Rect _fallbackWorldBounds = new Rect(-10f, -7.5f, 20f, 15f);

    [Header("Minimap Camera (optional)")]
    [Tooltip("Orthographic camera that renders level geometry into the map background. " +
             "Leave empty to keep a plain background. The camera's Target Texture must be " +
             "empty — this script creates and assigns the RenderTexture at runtime.")]
    [SerializeField] private Camera _minimapCamera;

    [Tooltip("RawImage on MapContainer that displays the live camera render. " +
             "Required if _minimapCamera is assigned.")]
    [SerializeField] private RawImage _mapBackground;

    [Tooltip("Pixel size of the longer RT dimension. Affects sharpness only, not alignment.")]
    [SerializeField] private int _rtMaxResolution = 512;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Rect            _worldBounds;
    private RenderTexture   _rt;

    public Rect          WorldBounds => _worldBounds;
    public RectTransform MapContainer => _mapContainer;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        ResolveWorldBounds();           // _worldBounds is ready from this point on
        ApplyBoundsToMapContainer();
        SetupMinimapCamera();           // uses _worldBounds — no execution-order dependency

        if (_p1Tracker != null) _p1Tracker.Initialize(this, PlayerIndex.Player1);
        if (_p2Tracker != null) _p2Tracker.Initialize(this, PlayerIndex.Player2);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
        }
    }

    // ── Coordinate helpers (called by MinimapPlayerTracker every LateUpdate) ──

    /// <summary>
    /// Converts a world-space XY position to an anchoredPosition inside
    /// _mapContainer.  (0,0) in map space is the center of the container.
    /// </summary>
    public Vector2 WorldToMap(Vector3 worldPos)
    {
        if (_mapContainer == null) return Vector2.zero;

        float nx = Mathf.InverseLerp(_worldBounds.xMin, _worldBounds.xMax, worldPos.x);
        float ny = Mathf.InverseLerp(_worldBounds.yMin, _worldBounds.yMax, worldPos.y);

        Rect r = _mapContainer.rect;
        return new Vector2(
            (nx - 0.5f) * r.width,
            (ny - 0.5f) * r.height);
    }

    /// <summary>
    /// Converts a world-space radius to a UI pixel diameter using the X axis
    /// scale so that circular FOVs remain circular on the map.
    /// </summary>
    public float WorldRadiusToMap(float worldRadius)
    {
        if (_mapContainer == null || _worldBounds.width <= 0f) return 0f;
        return worldRadius * (_mapContainer.rect.width / _worldBounds.width);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resizes _mapContainer so its aspect ratio matches the world bounds.
    /// The longest dimension is clamped to _mapMaxSize; the shorter dimension
    /// scales proportionally. This runs once at Start so coordinate mapping
    /// always uses the correct rect dimensions.
    /// </summary>
    private void ApplyBoundsToMapContainer()
    {
        if (_mapContainer == null || _worldBounds.width <= 0f || _worldBounds.height <= 0f) return;

        float worldAspect = _worldBounds.width / _worldBounds.height;

        Vector2 size = worldAspect >= 1f
            ? new Vector2(_mapMaxSize, _mapMaxSize / worldAspect)   // wider than tall
            : new Vector2(_mapMaxSize * worldAspect, _mapMaxSize);  // taller than wide

        _mapContainer.sizeDelta = size;
    }

    /// <summary>
    /// Creates a RenderTexture whose pixel aspect ratio matches worldBounds exactly,
    /// configures _minimapCamera to cover those bounds, and assigns the RT to
    /// _mapBackground.  Called after ResolveWorldBounds() so _worldBounds is valid.
    ///
    /// Doing this inside MinimapController.Start() (rather than in a separate script)
    /// guarantees _worldBounds is already set — no Script Execution Order fiddling needed.
    /// </summary>
    private void SetupMinimapCamera()
    {
        if (_minimapCamera == null || _mapBackground == null) return;

        // Build RT with correct aspect so Unity doesn't stretch the camera frustum.
        float aspect = _worldBounds.width / _worldBounds.height;
        int rtW, rtH;
        if (aspect >= 1f)
        {
            rtW = _rtMaxResolution;
            rtH = Mathf.Max(1, Mathf.RoundToInt(_rtMaxResolution / aspect));
        }
        else
        {
            rtH = _rtMaxResolution;
            rtW = Mathf.Max(1, Mathf.RoundToInt(_rtMaxResolution * aspect));
        }

        _rt = new RenderTexture(rtW, rtH, 16, RenderTextureFormat.Default);
        _rt.name = "MinimapRT";
        _rt.Create();

        // Configure camera to cover exactly worldBounds.
        _minimapCamera.orthographic     = true;
        _minimapCamera.orthographicSize = _worldBounds.height * 0.5f;
        _minimapCamera.targetTexture    = _rt;
        _minimapCamera.transform.position = new Vector3(
            _worldBounds.center.x,
            _worldBounds.center.y,
            _minimapCamera.transform.position.z);

        _mapBackground.texture = _rt;

        Debug.Log($"[MinimapController] Minimap RT {rtW}×{rtH}, " +
                  $"orthoSize={_minimapCamera.orthographicSize:F2}, " +
                  $"center=({_worldBounds.center.x:F2}, {_worldBounds.center.y:F2})");
    }

    private void ResolveWorldBounds()
    {
        MinimapBoundsMarker marker = FindAnyObjectByType<MinimapBoundsMarker>();
        if (marker != null)
        {
            _worldBounds = marker.worldBounds;
            Debug.Log($"[MinimapController] Using MinimapBoundsMarker bounds: {_worldBounds}");
        }
        else
        {
            _worldBounds = _fallbackWorldBounds;
            Debug.Log($"[MinimapController] No MinimapBoundsMarker found — using fallback bounds: {_worldBounds}");
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_mapContainer == null)
            Debug.LogWarning("[MinimapController] _mapContainer is not assigned.", this);
        if (_p1Tracker == null)
            Debug.LogWarning("[MinimapController] _p1Tracker is not assigned.", this);
        if (_p2Tracker == null)
            Debug.LogWarning("[MinimapController] _p2Tracker is not assigned.", this);
        if (_minimapCamera != null && _mapBackground == null)
            Debug.LogWarning("[MinimapController] _minimapCamera is assigned but _mapBackground is not.", this);
    }
#endif
}
