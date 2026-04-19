using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime random maze generator using Randomized Prim's algorithm.
///
/// Top-down 2D layout: the maze is laid out on the XY plane (Z = 0).
/// Wall cubes are thin slabs (small Z depth) so a top-down camera sees
/// them as solid filled squares. Passages appear as gaps between slabs.
///
/// Generates an NxM grid of passage/wall cells, instantiates Unity cube
/// primitives for every solid cell, and places SpawnPointMarker and
/// ProblemSpawnPocket prefabs in randomly chosen dead-end passage cells.
///
/// SCENE SETUP
///   1. Create an empty GameObject (e.g. "MazeManager") in the scene.
///   2. Attach this component to it.
///   3. Assign spawnPointPrefab (must have SpawnPointMarker) and
///      problemPocketPrefab (must have ProblemSpawnPocket) in the Inspector.
///   4. Tune grid size, cellSize, origin, seed, and wall material.
///   5. Press Play — the maze generates automatically in Start().
///   6. Right-click the component header → "Generate Maze" to preview in Edit mode.
///      Right-click → "Clear Maze" to destroy the preview.
///
/// ALGORITHM (Randomized Prim's)
///   Start from a random seed cell marked as Passage.
///   Maintain a frontier list of Wall cells adjacent to any Passage.
///   Each step: pick a random frontier cell, carve the wall between it
///   and a random Passage neighbor, mark it as Passage, add its Wall neighbors
///   to the frontier. Repeat until the frontier is empty.
///   Result: a perfect maze (all cells reachable, no isolated areas).
///   Prim's produces an organic, branchy layout with many dead ends.
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    // ── Grid ──────────────────────────────────────────────────────────────────

    [Header("Grid")]
    [Tooltip("Number of cells along the X axis. Odd values give cleaner mazes.")]
    [SerializeField] private int _columns = 21;

    [Tooltip("Number of cells along the Y axis. Odd values give cleaner mazes.")]
    [SerializeField] private int _rows = 15;

    [Tooltip("World-space size of one cell (used for both X and Y).")]
    [SerializeField] private float _cellSize = 2f;

    [Tooltip("World-space center of the maze. (0,0,0) keeps the maze centered for a camera at (0,0,-N).")]
    [SerializeField] private Vector3 _center = Vector3.zero;

    [Tooltip("Random seed. 0 = new random maze every Play. Any other value = deterministic/repeatable.")]
    [SerializeField] private int _seed = 0;

    // ── Walls ─────────────────────────────────────────────────────────────────

    [Header("Walls")]
    [Tooltip("Z depth of each wall cube. Keep small (e.g. 0.5) for a flat top-down look. " +
             "Increase if you need the walls to act as 3D physics blockers at non-zero Z depths.")]
    [SerializeField] private float _wallDepth = 0.5f;

    [Tooltip("Wall thickness as a fraction of cellSize (0–1). " +
             "1 = wall fills the whole cell. 0.25 = thin walls with wide corridors.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _wallThicknessFraction = 0.25f;

    [Tooltip("Optional material applied to interior wall cubes. Leave null for Unity default.")]
    [SerializeField] private Material _wallMaterial = null;

    // ── Camera ────────────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("Orthographic camera to fit to the maze. Leave null to use Camera.main.")]
    [SerializeField] private Camera _mazeCamera = null;

    [Tooltip("Extra world-unit padding added on each side so walls aren't clipped by the viewport edge.")]
    [SerializeField] private float _cameraMargin = 1f;

    // ── Spawn points ──────────────────────────────────────────────────────────

    [Header("Spawn Points")]
    [Tooltip("Prefab with a SpawnPointMarker component. Two are placed automatically (P1 and P2).")]
    [SerializeField] private GameObject _spawnPointPrefab = null;

    // ── Problem pockets ───────────────────────────────────────────────────────

    [Header("Problem Pockets")]
    [Tooltip("Prefab with a ProblemSpawnPocket component.")]
    [SerializeField] private GameObject _problemPocketPrefab = null;

    [Tooltip("Number of ProblemSpawnPocket objects to place in the maze.")]
    [SerializeField] private int _problemPocketCount = 8;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private enum CellType { Wall, Passage }

    private CellType[,] _grid;
    private System.Random _rng;
    private GameObject _mazeRoot;

    // Computed bottom-left corner from _center and grid dimensions.
    private Vector3 _origin;

    private void ComputeOrigin()
    {
        float halfW = _columns * _cellSize * 0.5f;
        float halfH = _rows    * _cellSize * 0.5f;
        _origin = _center - new Vector3(halfW, halfH, 0f);
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        Generate();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears any existing maze and generates a new one.
    /// Called automatically in Start(). Also available via [ContextMenu] in the Editor.
    /// </summary>
    [ContextMenu("Generate Maze")]
    public void Generate()
    {
        Clear();
        ComputeOrigin();

        int seed = (_seed == 0) ? (int)System.DateTime.Now.Ticks : _seed;
        _rng = new System.Random(seed);

        InitGrid();
        RunPrims();
        StampBorderRing();

        _mazeRoot = new GameObject("MazeRoot");
        _mazeRoot.transform.SetParent(transform);
        _mazeRoot.transform.localPosition = Vector3.zero;

        SpawnWalls();
        SpawnBoundary();
        PlaceSpawnPoints();
        PlaceProblemPockets();
        FitCameraToMaze();

        Debug.Log($"[MazeGenerator] Maze generated — {_columns}×{_rows} cells, seed={seed}.");
    }

    /// <summary>
    /// Destroys the current maze root and all children.
    /// </summary>
    [ContextMenu("Clear Maze")]
    public void Clear()
    {
        if (_mazeRoot != null)
        {
            DestroyImmediate(_mazeRoot);
            _mazeRoot = null;
        }

        // Safety pass: also destroy any leftover MazeRoot from a previous session.
        Transform existing = transform.Find("MazeRoot");
        if (existing != null)
            DestroyImmediate(existing.gameObject);
    }

    // ── Grid init ─────────────────────────────────────────────────────────────

    private void InitGrid()
    {
        _grid = new CellType[_columns, _rows];
        for (int c = 0; c < _columns; c++)
            for (int r = 0; r < _rows; r++)
                _grid[c, r] = CellType.Wall;
    }

    // ── Prim's algorithm ──────────────────────────────────────────────────────

    private void RunPrims()
    {
        // Start from a random interior cell (not on the outer ring).
        int startC = _rng.Next(1, _columns - 1);
        int startR = _rng.Next(1, _rows - 1);

        SetPassage(startC, startR);

        List<Vector2Int> frontier = new List<Vector2Int>();
        AddWallNeighborsToFrontier(startC, startR, frontier);

        while (frontier.Count > 0)
        {
            // Pick and remove a random frontier cell.
            int idx = _rng.Next(0, frontier.Count);
            Vector2Int cell = frontier[idx];
            frontier.RemoveAt(idx);

            if (_grid[cell.x, cell.y] == CellType.Passage)
                continue; // Already carved by a previous step.

            // Find passage neighbors and pick one to connect through.
            List<Vector2Int> passageNeighbors = GetPassageNeighbors(cell.x, cell.y);
            if (passageNeighbors.Count == 0)
                continue;

            Vector2Int neighbor = passageNeighbors[_rng.Next(0, passageNeighbors.Count)];

            // Carve the cell between this frontier cell and the chosen neighbor.
            int midC = (cell.x + neighbor.x) / 2;
            int midR = (cell.y + neighbor.y) / 2;
            SetPassage(midC, midR);
            SetPassage(cell.x, cell.y);

            AddWallNeighborsToFrontier(cell.x, cell.y, frontier);
        }
    }

    private void SetPassage(int c, int r)
    {
        if (InBounds(c, r))
            _grid[c, r] = CellType.Passage;
    }

    /// <summary>
    /// Adds the four axis-aligned neighbors (two steps away) that are still Wall
    /// and inside the interior (not the outer ring) to the frontier.
    /// </summary>
    private void AddWallNeighborsToFrontier(int c, int r, List<Vector2Int> frontier)
    {
        foreach (Vector2Int dir in CardinalDirections())
        {
            int nc = c + dir.x * 2;
            int nr = r + dir.y * 2;
            if (InBounds(nc, nr) && IsInner(nc, nr) && _grid[nc, nr] == CellType.Wall)
            {
                Vector2Int n = new Vector2Int(nc, nr);
                if (!frontier.Contains(n))
                    frontier.Add(n);
            }
        }
    }

    private List<Vector2Int> GetPassageNeighbors(int c, int r)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        foreach (Vector2Int dir in CardinalDirections())
        {
            int nc = c + dir.x * 2;
            int nr = r + dir.y * 2;
            if (InBounds(nc, nr) && IsInner(nc, nr) && _grid[nc, nr] == CellType.Passage)
                result.Add(new Vector2Int(nc, nr));
        }
        return result;
    }

    // Inner = not on the outer ring (the ring is always stamped back to Wall after carving).
    private bool IsInner(int c, int r) => c > 0 && c < _columns - 1 && r > 0 && r < _rows - 1;

    /// <summary>
    /// After Prim's finishes, force the entire outer ring back to Wall regardless
    /// of what the algorithm carved. This gives us a clean grid to spawn boundary
    /// slabs from, without any per-cell border cubes.
    /// </summary>
    private void StampBorderRing()
    {
        for (int c = 0; c < _columns; c++)
        {
            _grid[c, 0]          = CellType.Wall;
            _grid[c, _rows - 1]  = CellType.Wall;
        }
        for (int r = 0; r < _rows; r++)
        {
            _grid[0, r]           = CellType.Wall;
            _grid[_columns - 1, r] = CellType.Wall;
        }
    }

    private static Vector2Int[] CardinalDirections() => new[]
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private bool InBounds(int c, int r) => c >= 0 && c < _columns && r >= 0 && r < _rows;

    // ── Wall spawning ─────────────────────────────────────────────────────────

    private void SpawnWalls()
    {
        GameObject wallParent = new GameObject("Walls");
        wallParent.transform.SetParent(_mazeRoot.transform);

        float thin = _cellSize * _wallThicknessFraction;

        for (int c = 0; c < _columns; c++)
        {
            for (int r = 0; r < _rows; r++)
            {
                // Outer ring is handled by SpawnBoundary — skip it here.
                if (!IsInner(c, r)) continue;
                if (_grid[c, r] != CellType.Wall) continue;

                bool wallLeft  = InBounds(c - 1, r) && _grid[c - 1, r] == CellType.Wall;
                bool wallRight = InBounds(c + 1, r) && _grid[c + 1, r] == CellType.Wall;
                bool wallDown  = InBounds(c, r - 1) && _grid[c, r - 1] == CellType.Wall;
                bool wallUp    = InBounds(c, r + 1) && _grid[c, r + 1] == CellType.Wall;

                float scaleX = (wallLeft || wallRight) ? _cellSize : thin;
                float scaleY = (wallDown || wallUp)    ? _cellSize : thin;

                Vector3 pos = CellToWorld(c, r);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Wall_{c}_{r}";
                cube.transform.SetParent(wallParent.transform);
                cube.transform.position = pos;
                cube.transform.localScale = new Vector3(scaleX, scaleY, _wallDepth);

                if (_wallMaterial != null)
                    cube.GetComponent<Renderer>().sharedMaterial = _wallMaterial;
            }
        }
    }

    /// <summary>
    /// Spawns four single flat slabs — one per side — to form the outer boundary.
    /// Each slab is exactly _wallThicknessFraction * _cellSize thick on its narrow axis
    /// and spans the full grid width/height on its long axis, so it looks identical
    /// to any other straight wall run in the maze.
    /// </summary>
    private void SpawnBoundary()
    {
        GameObject boundaryParent = new GameObject("Boundary");
        boundaryParent.transform.SetParent(_mazeRoot.transform);

        float thin   = _cellSize * _wallThicknessFraction;
        float totalW = _columns  * _cellSize;
        float totalH = _rows     * _cellSize;
        float cx     = _origin.x + totalW * 0.5f;
        float cy     = _origin.y + totalH * 0.5f;

        Material mat = _wallMaterial;

        // Bottom slab — sits at the bottom row's cell centre, full width, thin height.
        float bottomY = _origin.y + _cellSize * 0.5f;
        SpawnSlab("Boundary_Bottom", boundaryParent,
            new Vector3(cx, bottomY, 0f),
            new Vector3(totalW, thin, _wallDepth), mat);

        // Top slab
        float topY = _origin.y + totalH - _cellSize * 0.5f;
        SpawnSlab("Boundary_Top", boundaryParent,
            new Vector3(cx, topY, 0f),
            new Vector3(totalW, thin, _wallDepth), mat);

        // Left slab — full height, thin width.
        float leftX = _origin.x + _cellSize * 0.5f;
        SpawnSlab("Boundary_Left", boundaryParent,
            new Vector3(leftX, cy, 0f),
            new Vector3(thin, totalH, _wallDepth), mat);

        // Right slab
        float rightX = _origin.x + totalW - _cellSize * 0.5f;
        SpawnSlab("Boundary_Right", boundaryParent,
            new Vector3(rightX, cy, 0f),
            new Vector3(thin, totalH, _wallDepth), mat);
    }

    private void SpawnSlab(string slabName, GameObject parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = slabName;
        slab.transform.SetParent(parent.transform);
        slab.transform.position = pos;
        slab.transform.localScale = scale;
        if (mat != null)
            slab.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ── Spawn point placement ─────────────────────────────────────────────────

    private void PlaceSpawnPoints()
    {
        if (_spawnPointPrefab == null)
        {
            Debug.LogWarning("[MazeGenerator] No spawnPointPrefab assigned — skipping spawn point placement.", this);
            return;
        }

        List<Vector2Int> candidates = GetDeadEndCells();
        ShuffleList(candidates);

        // Fall back to all passage cells if dead-ends are scarce.
        if (candidates.Count < 2)
        {
            candidates = GetAllPassageCells();
            ShuffleList(candidates);
        }

        GameObject spawnParent = new GameObject("SpawnPoints");
        spawnParent.transform.SetParent(_mazeRoot.transform);

        PlayerIndex[] players = new[] { PlayerIndex.Player1, PlayerIndex.Player2 };

        for (int i = 0; i < 2 && i < candidates.Count; i++)
        {
            Vector3 pos = CellToWorld(candidates[i].x, candidates[i].y);
            GameObject go = Instantiate(_spawnPointPrefab, pos, Quaternion.identity, spawnParent.transform);
            go.name = $"SpawnPoint_P{i + 1}";

            SpawnPointMarker marker = go.GetComponent<SpawnPointMarker>();
            if (marker != null)
                marker.player = players[i];
        }
    }

    // ── Problem pocket placement ───────────────────────────────────────────────

    private void PlaceProblemPockets()
    {
        if (_problemPocketPrefab == null)
        {
            Debug.LogWarning("[MazeGenerator] No problemPocketPrefab assigned — skipping pocket placement.", this);
            return;
        }

        // Use dead-ends but skip the first two (reserved for spawn points).
        List<Vector2Int> deadEnds = GetDeadEndCells();
        ShuffleList(deadEnds);

        List<Vector2Int> candidates = new List<Vector2Int>(deadEnds);

        // If not enough dead-ends, pad with other passage cells.
        if (candidates.Count < _problemPocketCount + 2)
        {
            List<Vector2Int> allPassage = GetAllPassageCells();
            ShuffleList(allPassage);
            foreach (Vector2Int cell in allPassage)
            {
                if (!candidates.Contains(cell))
                    candidates.Add(cell);
            }
        }

        // Skip the first two dead-ends (used by spawn points).
        int startIndex = Mathf.Min(2, candidates.Count);

        GameObject pocketParent = new GameObject("ProblemPockets");
        pocketParent.transform.SetParent(_mazeRoot.transform);

        int placed = 0;
        for (int i = startIndex; i < candidates.Count && placed < _problemPocketCount; i++, placed++)
        {
            Vector3 pos = CellToWorld(candidates[i].x, candidates[i].y);
            GameObject go = Instantiate(_problemPocketPrefab, pos, Quaternion.identity, pocketParent.transform);
            go.name = $"ProblemPocket_{placed}";
        }
    }

    // ── Camera fit ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the target orthographic camera's size and position so the entire maze
    /// is visible with an optional margin. The camera's XY is moved to the maze
    /// center; its Z position is preserved so the near/far clip planes stay valid.
    ///
    /// Orthographic size = half the viewport height in world units.
    /// We need to fit both axes, so we take the larger of:
    ///   - half the maze height + margin
    ///   - (half the maze width + margin) / aspect
    /// </summary>
    private void FitCameraToMaze()
    {
        Camera cam = _mazeCamera != null ? _mazeCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[MazeGenerator] No camera found to fit — assign one in the Inspector or tag a camera as MainCamera.", this);
            return;
        }

        if (!cam.orthographic)
        {
            Debug.LogWarning($"[MazeGenerator] Camera '{cam.name}' is not orthographic — switching to orthographic.", cam);
            cam.orthographic = true;
        }

        float halfW = _columns * _cellSize * 0.5f + _cameraMargin;
        float halfH = _rows    * _cellSize * 0.5f + _cameraMargin;

        // Fit whichever axis is the tighter constraint given the current aspect ratio.
        float sizeFromHeight = halfH;
        float sizeFromWidth  = halfW / cam.aspect;
        cam.orthographicSize = Mathf.Max(sizeFromHeight, sizeFromWidth);

        // Center XY on the maze; keep the camera's existing Z.
        Vector3 pos = cam.transform.position;
        cam.transform.position = new Vector3(_center.x, _center.y, pos.z);

        Debug.Log($"[MazeGenerator] Camera '{cam.name}' fitted — orthoSize={cam.orthographicSize:F2}, center=({_center.x},{_center.y}).");
    }

    // ── Dead-end / passage helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns all passage cells that have exactly one passage neighbor (dead ends).
    /// Dead ends are ideal for spawn/pocket placement because they are open and easy to reach.
    /// </summary>
    private List<Vector2Int> GetDeadEndCells()
    {
        List<Vector2Int> deadEnds = new List<Vector2Int>();
        for (int c = 0; c < _columns; c++)
        {
            for (int r = 0; r < _rows; r++)
            {
                if (_grid[c, r] != CellType.Passage) continue;
                if (CountPassageNeighborsDirect(c, r) == 1)
                    deadEnds.Add(new Vector2Int(c, r));
            }
        }
        return deadEnds;
    }

    private List<Vector2Int> GetAllPassageCells()
    {
        List<Vector2Int> passages = new List<Vector2Int>();
        for (int c = 0; c < _columns; c++)
            for (int r = 0; r < _rows; r++)
                if (_grid[c, r] == CellType.Passage)
                    passages.Add(new Vector2Int(c, r));
        return passages;
    }

    /// <summary>
    /// Counts direct (1-step) passage neighbors, used for dead-end detection.
    /// </summary>
    private int CountPassageNeighborsDirect(int c, int r)
    {
        int count = 0;
        foreach (Vector2Int dir in CardinalDirections())
        {
            int nc = c + dir.x;
            int nr = r + dir.y;
            if (InBounds(nc, nr) && _grid[nc, nr] == CellType.Passage)
                count++;
        }
        return count;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private Vector3 CellToWorld(int c, int r)
    {
        return _origin + new Vector3(
            c * _cellSize + _cellSize * 0.5f,
            r * _cellSize + _cellSize * 0.5f,
            0f);
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── Scene Gizmos ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        ComputeOrigin();

        float totalWidth  = _columns * _cellSize;
        float totalHeight = _rows    * _cellSize;

        Vector3 bl = _origin;
        Vector3 br = _origin + new Vector3(totalWidth, 0f, 0f);
        Vector3 tr = _origin + new Vector3(totalWidth, totalHeight, 0f);
        Vector3 tl = _origin + new Vector3(0f, totalHeight, 0f);

        UnityEditor.Handles.color = new Color(0.8f, 0.4f, 1f, 0.8f);
        UnityEditor.Handles.DrawLine(bl, br);
        UnityEditor.Handles.DrawLine(br, tr);
        UnityEditor.Handles.DrawLine(tr, tl);
        UnityEditor.Handles.DrawLine(tl, bl);

        UnityEditor.Handles.Label(
            new Vector3(_origin.x + totalWidth * 0.5f, _origin.y + totalHeight + 0.4f, 0f),
            $"Maze  {_columns}×{_rows}  ({totalWidth:F1}×{totalHeight:F1} wu)");

        // Draw the generated grid if available (Edit-mode preview).
        if (_grid == null) return;

        float gthin  = _cellSize * _wallThicknessFraction;
        float totalW = _columns  * _cellSize;
        float totalH = _rows     * _cellSize;
        float gcx    = _origin.x + totalW * 0.5f;
        float gcy    = _origin.y + totalH * 0.5f;

        // Draw boundary slabs.
        Gizmos.color = new Color(0.6f, 0.2f, 0.8f, 0.35f);
        Gizmos.DrawCube(new Vector3(gcx, _origin.y + _cellSize * 0.5f, 0f),            new Vector3(totalW, gthin, _wallDepth));
        Gizmos.DrawCube(new Vector3(gcx, _origin.y + totalH - _cellSize * 0.5f, 0f),   new Vector3(totalW, gthin, _wallDepth));
        Gizmos.DrawCube(new Vector3(_origin.x + _cellSize * 0.5f, gcy, 0f),            new Vector3(gthin, totalH, _wallDepth));
        Gizmos.DrawCube(new Vector3(_origin.x + totalW - _cellSize * 0.5f, gcy, 0f),   new Vector3(gthin, totalH, _wallDepth));

        // Draw interior wall cells.
        for (int c = 1; c < _columns - 1; c++)
        {
            for (int r = 1; r < _rows - 1; r++)
            {
                if (_grid[c, r] != CellType.Wall) continue;

                Gizmos.color = new Color(0.6f, 0.2f, 0.8f, 0.25f);
                Vector3 pos = CellToWorld(c, r);
                bool wL = InBounds(c - 1, r) && _grid[c - 1, r] == CellType.Wall;
                bool wR = InBounds(c + 1, r) && _grid[c + 1, r] == CellType.Wall;
                bool wD = InBounds(c, r - 1) && _grid[c, r - 1] == CellType.Wall;
                bool wU = InBounds(c, r + 1) && _grid[c, r + 1] == CellType.Wall;
                float gx = (wL || wR) ? _cellSize : gthin;
                float gy = (wD || wU) ? _cellSize : gthin;
                Gizmos.DrawCube(pos, new Vector3(gx, gy, _wallDepth));
            }
        }
    }
#endif
}
