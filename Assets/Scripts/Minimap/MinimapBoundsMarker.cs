using UnityEngine;

/// <summary>
/// Place one of these on any empty GameObject in a level scene to define the
/// world-space XY playable area for the minimap.
///
/// MinimapController searches for this component at runtime (FindAnyObjectByType).
/// If none is found it falls back to its own _fallbackWorldBounds.
///
/// The Gizmo draws the bounds rectangle in Scene view so you can visually
/// confirm coverage before entering Play mode.
/// </summary>
public class MinimapBoundsMarker : MonoBehaviour
{
    [Tooltip("World-space XY rectangle covering the full playable area. " +
             "X = left edge, Y = bottom edge, Width = total width, Height = total height.")]
    public Rect worldBounds = new Rect(-10f, -7.5f, 20f, 15f);

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        UnityEditor.Handles.color = new Color(0f, 1f, 0.4f, 0.6f);

        Vector3 bl = new Vector3(worldBounds.xMin, worldBounds.yMin, 0f);
        Vector3 br = new Vector3(worldBounds.xMax, worldBounds.yMin, 0f);
        Vector3 tr = new Vector3(worldBounds.xMax, worldBounds.yMax, 0f);
        Vector3 tl = new Vector3(worldBounds.xMin, worldBounds.yMax, 0f);

        UnityEditor.Handles.DrawLine(bl, br);
        UnityEditor.Handles.DrawLine(br, tr);
        UnityEditor.Handles.DrawLine(tr, tl);
        UnityEditor.Handles.DrawLine(tl, bl);

        // Label the bounds so level designers can verify dimensions at a glance.
        UnityEditor.Handles.Label(
            new Vector3(worldBounds.center.x, worldBounds.yMax + 0.3f, 0f),
            $"Minimap Bounds  {worldBounds.width:F1} × {worldBounds.height:F1}");
    }
#endif
}
