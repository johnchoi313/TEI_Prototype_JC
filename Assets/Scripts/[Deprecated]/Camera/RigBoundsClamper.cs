using UnityEngine;

/// <summary>
/// Clamps this camera rig's XY position to the MinimapBoundsMarker world bounds
/// every LateUpdate, after PlayerCameraController has already moved it.
///
/// Execution order 100 guarantees this runs after PlayerCameraController (default 0),
/// so the clamp always wins regardless of component order in the Inspector.
///
/// SETUP
///   Add this component to the same GameObject as PlayerCameraController.
///   No inspector wiring required — bounds are discovered automatically from
///   the MinimapBoundsMarker in the scene, same as the minimap system.
/// </summary>
[DefaultExecutionOrder(100)]
public class RigBoundsClamper : MonoBehaviour
{
    [Tooltip("Fallback bounds if no MinimapBoundsMarker is found in the scene.")]
    [SerializeField] private Rect _fallbackBounds = new Rect(-10f, -7.5f, 20f, 15f);

    private Rect _bounds;
    private bool _hasBounds;

    private void Start()
    {
        // FindObjectsInactive.Include ensures we find the marker even if its
        // GameObject is disabled (e.g. hidden manager objects, prefab instances
        // that start inactive, etc.).
        MinimapBoundsMarker marker = FindAnyObjectByType<MinimapBoundsMarker>(FindObjectsInactive.Include);
        if (marker != null)
        {
            _bounds    = marker.worldBounds;
            _hasBounds = true;
            Debug.Log($"[RigBoundsClamper] {name}: using MinimapBoundsMarker '{marker.name}' bounds = {_bounds}");
        }
        else
        {
            _bounds    = _fallbackBounds;
            _hasBounds = true;
            Debug.LogWarning($"[RigBoundsClamper] {name}: no MinimapBoundsMarker found in scene — using fallback bounds {_bounds}. " +
                             "Add a MinimapBoundsMarker component to any GameObject in the scene.", this);
        }
    }

    private void LateUpdate()
    {
        if (!_hasBounds) return;

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, _bounds.xMin, _bounds.xMax);
        pos.y = Mathf.Clamp(pos.y, _bounds.yMin, _bounds.yMax);
        transform.position = pos;
    }
}
