using UnityEngine;

/// <summary>
/// Reads the MinimapBoundsMarker in the scene and spawns four invisible
/// BoxCollider walls at runtime to keep physics objects (fish characters,
/// future collectibles) inside the playable area.
///
/// Single source of truth: resize the MinimapBoundsMarker and the walls
/// automatically match on next Play — no manual wall placement needed.
///
/// SCENE SETUP
///   Add this component to any persistent Manager GameObject.
///   No inspector wiring required — walls are spawned in Start().
///
/// WALL DEPTH
///   wallDepth (default 20) sets the BoxCollider Z size so walls catch objects
///   at any Z depth. Increase if you add elements at extreme Z values.
/// </summary>
public class MapBoundarySpawner : MonoBehaviour
{
    [Tooltip("How thick each wall is in world units. Thick enough that fast-moving objects can't tunnel through.")]
    [SerializeField] private float _wallThickness = 2f;

    [Tooltip("Z depth of each wall collider. Should exceed the full Z range of any physics object in the scene.")]
    [SerializeField] private float _wallDepth = 20f;

    [Tooltip("Fallback bounds used if no MinimapBoundsMarker is found in the scene.")]
    [SerializeField] private Rect _fallbackBounds = new Rect(-10f, -7.5f, 20f, 15f);

    private void Start()
    {
        Rect bounds = ResolveBounds();
        SpawnWalls(bounds);
    }

    private Rect ResolveBounds()
    {
        MinimapBoundsMarker marker = FindAnyObjectByType<MinimapBoundsMarker>();
        if (marker != null)
        {
            Debug.Log($"[MapBoundarySpawner] Using MinimapBoundsMarker bounds: {marker.worldBounds}");
            return marker.worldBounds;
        }

        Debug.LogWarning("[MapBoundarySpawner] No MinimapBoundsMarker found — using fallback bounds.", this);
        return _fallbackBounds;
    }

    private void SpawnWalls(Rect b)
    {
        float t = _wallThickness;
        float d = _wallDepth;

        // Left wall — sits just outside xMin
        SpawnWall("Boundary_Left",
            new Vector3(b.xMin - t * 0.5f, b.center.y, 0f),
            new Vector3(t, b.height + t * 2f, d));

        // Right wall — sits just outside xMax
        SpawnWall("Boundary_Right",
            new Vector3(b.xMax + t * 0.5f, b.center.y, 0f),
            new Vector3(t, b.height + t * 2f, d));

        // Bottom wall — sits just outside yMin
        SpawnWall("Boundary_Bottom",
            new Vector3(b.center.x, b.yMin - t * 0.5f, 0f),
            new Vector3(b.width + t * 2f, t, d));

        // Top wall — sits just outside yMax
        SpawnWall("Boundary_Top",
            new Vector3(b.center.x, b.yMax + t * 0.5f, 0f),
            new Vector3(b.width + t * 2f, t, d));

        Debug.Log("[MapBoundarySpawner] 4 boundary walls spawned.");
    }

    private void SpawnWall(string wallName, Vector3 position, Vector3 size)
    {
        GameObject wall = new GameObject(wallName);
        wall.transform.SetParent(transform);
        wall.transform.position = position;

        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = size;
        // No Rigidbody — static collider, zero physics cost.
    }
}
