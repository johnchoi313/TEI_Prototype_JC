using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Runtime random maze generator using Randomized Prim's algorithm.
///
/// Top-down 2D layout on the XY plane (Z = 0).
///
/// APPROACH — pillar + beam:
///   Prim's runs on an odd-dimensioned logical grid. Every wall cell becomes either:
///     • A square PILLAR cube (_wallThickness × _wallThickness) placed at the node center.
///     • A stretched BEAM cube connecting two adjacent wall-node pillars horizontally
///       or vertically (_cellSize long on the run axis, _wallThickness on the cross axis).
///   Passage cells produce nothing. The result is a clean thin-wall maze with no
///   overlapping geometry and no scaling heuristics.
///   _cellSize controls corridor width (node-to-node spacing).
///   _wallThickness controls how wide/tall the walls appear (~20% of cellSize looks good).
///
/// SCENE SETUP
///   1. Create an empty GameObject (e.g. "MazeManager") in the scene.
///   2. Attach this component to it.
///   3. Assign player light/fish references in the Inspector.
///   4. Tune grid size, cellSize, wallThickness, center, seed, wall material,
///      breakableWallMaterial, breakableWallRatio (fraction of carved corridors
///      re-sealed as breakable — higher values mean more paths are blocked),
///      stationMaterial, and stationCellsPerStation.
///   5. Press Play — the maze generates automatically in Start().
///   6. Right-click the component header → "Generate Maze" to preview in Edit mode.
///      Right-click → "Clear Maze" to destroy the preview.
///
/// ALGORITHM (Randomized Prim's)
///   Start from a random odd-indexed interior cell marked as Passage.
///   Maintain a frontier list of Wall cells two steps away from any Passage.
///   Each step: pick a random frontier cell, carve it and the midpoint cell between
///   it and a random Passage neighbor, add its new Wall neighbors to the frontier.
///   Result: a perfect maze (all cells reachable, no loops).
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
    [Tooltip("Cross-section size of walls in world units. ~20% of cellSize looks good.")]
    [SerializeField] private float _wallThickness = 0.4f;

    [Tooltip("Z depth of each wall cube. Keep small (e.g. 0.5) for a flat top-down look.")]
    [SerializeField] private float _wallDepth = 0.5f;

    [Tooltip("Optional material applied to solid (indestructible) wall cubes. Leave null for Unity default.")]
    [SerializeField] private Material _wallMaterial = null;

    // ── Breakable walls ───────────────────────────────────────────────────────

    [Header("Breakable Walls")]
    [Tooltip("Material applied to breakable wall segments. Leave null to fall back to the regular wall material.")]
    [SerializeField] private Material _breakableWallMaterial = null;

    [Tooltip("Fraction of carved corridor segments re-sealed as breakable walls. 0 = fully open maze, 1 = every corridor blocked. ~0.3 gives a good challenge.")]
    [Range(0f, 1f)]
    [SerializeField] private float _breakableWallRatio = 0.2f;

    // ── Stations ──────────────────────────────────────────────────────────────

    [Header("Stations")]
    [Tooltip("Material applied to station spheres. Leave null for Unity default.")]
    [SerializeField] private Material _stationMaterial = null;

    [Tooltip("One station is placed per this many passage cells. E.g. 10 = one station every 10 open cells.")]
    [SerializeField] private int _stationCellsPerStation = 10;

    [Tooltip("Station sphere diameter as a fraction of cell size. 0.3 = 30% of cell width (fits comfortably in a corridor).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _stationSizeRatio = 0.3f;

    // ── Camera ────────────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("Orthographic camera to fit to the maze. Leave null to use Camera.main.")]
    [SerializeField] private Camera _mazeCamera = null;

    [Tooltip("Extra world-unit padding added on each side so walls aren't clipped by the viewport edge.")]
    [SerializeField] private float _cameraMargin = 1f;

    // ── Players ───────────────────────────────────────────────────────────────

    [Header("Players — P1")]
    [Tooltip("PlayerLightController GameObject for Player 1. Teleported to a random open cell on generate.")]
    [SerializeField] private PlayerLightController _p1Light;

    [Tooltip("PlayerFishController GameObject for Player 1. Teleported to the same open cell as its light.")]
    [SerializeField] private PlayerFishController _p1Fish;

    [Header("Players — P2")]
    [Tooltip("PlayerLightController GameObject for Player 2.")]
    [SerializeField] private PlayerLightController _p2Light;

    [Tooltip("PlayerFishController GameObject for Player 2.")]
    [SerializeField] private PlayerFishController _p2Fish;

    // ── Debug UI ──────────────────────────────────────────────────────────────

    [Header("Debug UI — Grid Input Fields")]
    [Tooltip("TMP InputField for column count. Auto-filled from PlayerPrefs on start.")]
    [SerializeField] private TMP_InputField _columnsInputField;

    [Tooltip("TMP InputField for row count. Auto-filled from PlayerPrefs on start.")]
    [SerializeField] private TMP_InputField _rowsInputField;

    private const string PrefColumns = "MazeGenerator_Columns";
    private const string PrefRows    = "MazeGenerator_Rows";

    // ── Runtime ───────────────────────────────────────────────────────────────

    private enum CellType { Wall, Passage, BreakableWall }

    private CellType[,]   _grid;
    private System.Random _rng;
    private GameObject    _mazeRoot;

    // Computed bottom-left corner from _center and grid dimensions.
    private Vector3 _origin;

    /// <summary>
    /// World-space XY rectangle covering the full maze interior (inner passage area,
    /// excluding the outer boundary ring). Available after Generate() runs.
    /// PlayerLightController uses this to clamp light movement inside the maze.
    /// </summary>
    public Rect WorldBounds { get; private set; }

    /// <summary>Column count of the last generated maze (after odd-rounding).</summary>
    public int Columns => _columns;

    /// <summary>Row count of the last generated maze (after odd-rounding).</summary>
    public int Rows => _rows;

    /// <summary>Number of BreakableWall components currently alive under this maze root.</summary>
    public int BreakableWallCount =>
        _mazeRoot != null ? _mazeRoot.GetComponentsInChildren<BreakableWall>().Length : 0;

    /// <summary>Number of Station components currently alive under this maze root.</summary>
    public int StationCount =>
        _mazeRoot != null ? _mazeRoot.GetComponentsInChildren<Station>().Length : 0;

    private void ComputeOrigin()
    {
        float halfW = _columns * _cellSize * 0.5f;
        float halfH = _rows    * _cellSize * 0.5f;
        _origin = _center - new Vector3(halfW, halfH, 0f);

        // Playable bounds = area between the outer-ring wall nodes (one cellSize inset).
        float innerX = _origin.x + _cellSize;
        float innerY = _origin.y + _cellSize;
        float innerW = (_columns - 2) * _cellSize;
        float innerH = (_rows    - 2) * _cellSize;
        WorldBounds = new Rect(innerX, innerY, innerW, innerH);
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Restore persisted grid dimensions, then populate the input fields.
        _columns = PlayerPrefs.GetInt(PrefColumns, _columns);
        _rows    = PlayerPrefs.GetInt(PrefRows,    _rows);
        SyncInputFields();
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

        // Prim's requires odd grid dimensions so every interior cell falls on an
        // odd index and the 2-step carving reaches the full interior evenly.
        if (_columns % 2 == 0) _columns++;
        if (_rows    % 2 == 0) _rows++;

        ComputeOrigin();

        int seed = (_seed == 0) ? (int)System.DateTime.Now.Ticks : _seed;
        _rng = new System.Random(seed);

        _mazeRoot = new GameObject("MazeRoot");
        _mazeRoot.transform.SetParent(transform);
        _mazeRoot.transform.localPosition = Vector3.zero;

        InitGrid();
        RunPrims();              // carves passages into _grid
        MarkBreakableWalls();    // flags gap cells between passage nodes as breakable
        BuildWalls();            // pillars + beams for every wall cell

        ApplyBoundsToLights();
        PlaceStations();
        PlacePlayers();
        FitCameraToMaze();

        Debug.Log($"[MazeGenerator] Maze generated — {_columns}×{_rows} cells, seed={seed}.");
    }

    // ── Debug UI API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Set the column count from a UI button or TMP InputField onEndEdit event.
    /// Non-numeric or out-of-range input is ignored. Minimum value is 3.
    /// Call Regenerate() afterward (or wire the InputField's onEndEdit directly to
    /// SetColumns and a separate button to Regenerate).
    /// </summary>
    public void SetColumns(string value)
    {
        if (!int.TryParse(value, out int parsed)) return;
        _columns = Mathf.Max(3, parsed);
        PlayerPrefs.SetInt(PrefColumns, _columns);
        PlayerPrefs.Save();
        SyncInputFields();
        Debug.Log($"[MazeGenerator] Columns set to {_columns}.");
    }

    /// <summary>
    /// Set the row count from a UI button or TMP InputField onEndEdit event.
    /// Non-numeric or out-of-range input is ignored. Minimum value is 3.
    /// </summary>
    public void SetRows(string value)
    {
        if (!int.TryParse(value, out int parsed)) return;
        _rows = Mathf.Max(3, parsed);
        PlayerPrefs.SetInt(PrefRows, _rows);
        PlayerPrefs.Save();
        SyncInputFields();
        Debug.Log($"[MazeGenerator] Rows set to {_rows}.");
    }

    /// <summary>
    /// Regenerates the maze with the current settings.
    /// Wire directly to a UI Button's OnClick event.
    /// </summary>
    public void Regenerate() => Generate();

    /// <summary>Pushes the current _columns and _rows values into the input fields.</summary>
    private void SyncInputFields()
    {
        if (_columnsInputField != null) _columnsInputField.text = _columns.ToString();
        if (_rowsInputField    != null) _rowsInputField.text    = _rows.ToString();
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
        // Prim's steps 2 cells at a time, so the start cell and all carved cells
        // must be on odd indices (1, 3, 5 …). Pick a random odd interior index
        // for each axis. _columns and _rows are forced odd in Generate() so there
        // is always at least one valid odd interior index on each axis.
        int oddColCount = (_columns - 1) / 2; // number of odd indices in [1, _columns-2]
        int oddRowCount = (_rows    - 1) / 2;
        int startC = 1 + _rng.Next(0, oddColCount) * 2;
        int startR = 1 + _rng.Next(0, oddRowCount) * 2;

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

    // Inner = not on the outer ring. Prim's only carves interior cells so the
    // outer ring cubes always remain, forming the solid maze border automatically.
    private bool IsInner(int c, int r) => c > 0 && c < _columns - 1 && r > 0 && r < _rows - 1;

    private static Vector2Int[] CardinalDirections() => new[]
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private bool InBounds(int c, int r) => c >= 0 && c < _columns && r >= 0 && r < _rows;

    // ── Breakable wall marking ────────────────────────────────────────────────

    /// <summary>
    /// Seals a random fraction of carved corridor segments with BreakableWall,
    /// making the maze partially impassable until players break through.
    ///
    /// In Prim's odd-grid layout, even-indexed interior cells are "edge" cells —
    /// each one connects exactly two odd-indexed node cells along one axis.
    /// When Prim's CARVES an edge cell it becomes Passage (an open corridor segment).
    /// Re-sealing a subset of those back to BreakableWall blocks real paths, since
    /// those are the only cells that actually connect adjacent corridors.
    ///
    /// Node cells (odd-odd) and outer-ring cells are never touched so the pillar
    /// grid and maze border remain solid and visually intact.
    /// </summary>
    private void MarkBreakableWalls()
    {
        if (_breakableWallRatio <= 0f) return;

        // Collect every even-indexed interior Passage cell (carved corridor segments).
        List<Vector2Int> corridorEdges = new List<Vector2Int>();
        for (int c = 1; c < _columns - 1; c++)
        {
            for (int r = 1; r < _rows - 1; r++)
            {
                if (_grid[c, r] != CellType.Passage) continue;

                // Edge cells are even on exactly one axis.
                bool evenC = (c % 2 == 0);
                bool evenR = (r % 2 == 0);
                if (evenC == evenR) continue; // odd-odd (node) or even-even — skip

                corridorEdges.Add(new Vector2Int(c, r));
            }
        }

        ShuffleList(corridorEdges);

        int sealCount = Mathf.RoundToInt(corridorEdges.Count * _breakableWallRatio);
        for (int i = 0; i < sealCount; i++)
            _grid[corridorEdges[i].x, corridorEdges[i].y] = CellType.BreakableWall;

        Debug.Log($"[MazeGenerator] {sealCount} corridor segment(s) sealed as breakable walls from {corridorEdges.Count} candidate(s).");
    }

    // ── Wall spawning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates every wall cell and places:
    ///   • A square PILLAR at the cell center (_wallThickness × _wallThickness).
    ///   • A horizontal BEAM to the right neighbor if it is also a wall cell.
    ///   • A vertical   BEAM upward to the neighbor if it is also a wall cell.
    /// Beams span from pillar-center to pillar-center (_cellSize long) and are
    /// _wallThickness wide on the cross axis. Because we only emit beams in the
    /// +X and +Y directions, every pair of adjacent wall cells gets exactly one
    /// beam — no duplicates, no gaps, no overlapping geometry.
    /// Both solid and breakable wall cells are treated as wall for beam-connection
    /// purposes so the visual mesh stays continuous.
    /// </summary>
    private void BuildWalls()
    {
        GameObject solidParent     = new GameObject("Walls");
        GameObject breakableParent = new GameObject("BreakableWalls");
        solidParent.transform.SetParent(_mazeRoot.transform);
        breakableParent.transform.SetParent(_mazeRoot.transform);

        float t = _wallThickness;

        for (int c = 0; c < _columns; c++)
        {
            for (int r = 0; r < _rows; r++)
            {
                if (!IsWallType(_grid[c, r])) continue;

                bool isBreakable = _grid[c, r] == CellType.BreakableWall;
                GameObject parent = isBreakable ? breakableParent : solidParent;
                Material mat = isBreakable
                    ? (_breakableWallMaterial != null ? _breakableWallMaterial : _wallMaterial)
                    : _wallMaterial;

                Vector3 pos = CellToWorld(c, r);

                // Breakable corridor cells sit at even-indexed positions between two
                // odd-indexed pillar cells. Each pillar is _cellSize away in grid
                // space, so pillar-centre to pillar-centre = 2 * _cellSize. The beam
                // must span that full distance to touch both flanking pillars flush.
                if (isBreakable)
                {
                    bool evenC = (c % 2 == 0);
                    if (evenC)
                    {
                        // Corridor runs along X; breakable wall seals it on the Y axis.
                        SpawnWallCube(parent, $"BreakableBeam_{c}_{r}", pos,
                            new Vector3(t, _cellSize * 2f, _wallDepth), mat, true);
                    }
                    else
                    {
                        // Corridor runs along Y; breakable wall seals it on the X axis.
                        SpawnWallCube(parent, $"BreakableBeam_{c}_{r}", pos,
                            new Vector3(_cellSize * 2f, t, _wallDepth), mat, true);
                    }
                    continue; // No pillar or neighbor-driven beams needed.
                }

                // Pillar
                SpawnWallCube(parent, $"Pillar_{c}_{r}", pos,
                    new Vector3(t, t, _wallDepth), mat, isBreakable);

                // Beam rightward (+X) to (c+1, r)
                if (InBounds(c + 1, r) && IsWallType(_grid[c + 1, r]))
                {
                    Vector3 beamPos = pos + new Vector3(_cellSize * 0.5f, 0f, 0f);
                    SpawnWallCube(parent, $"BeamH_{c}_{r}", beamPos,
                        new Vector3(_cellSize, t, _wallDepth), mat, isBreakable);
                }

                // Beam upward (+Y) to (c, r+1)
                if (InBounds(c, r + 1) && IsWallType(_grid[c, r + 1]))
                {
                    Vector3 beamPos = pos + new Vector3(0f, _cellSize * 0.5f, 0f);
                    SpawnWallCube(parent, $"BeamV_{c}_{r}", beamPos,
                        new Vector3(t, _cellSize, _wallDepth), mat, isBreakable);
                }
            }
        }
    }

    private static bool IsWallType(CellType t) =>
        t == CellType.Wall || t == CellType.BreakableWall;

    private void SpawnWallCube(GameObject parent, string cubeName, Vector3 pos, Vector3 scale,
                               Material mat, bool breakable)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = cubeName;
        cube.transform.SetParent(parent.transform);
        cube.transform.position = pos;
        cube.transform.localScale = scale;
        if (mat != null)
            cube.GetComponent<Renderer>().sharedMaterial = mat;
        if (breakable)
            cube.AddComponent<BreakableWall>();
    }

    // ── Station placement ─────────────────────────────────────────────────────

    /// <summary>
    /// Spawns sphere-primitive stations in random passage cells.
    /// Count = passage cells / _stationCellsPerStation (minimum 1 if any cells exist).
    /// Cells already used for spawn points and problem pockets are excluded so
    /// stations never visually stack with other objects.
    /// </summary>
    private void PlaceStations()
    {
        if (_stationCellsPerStation <= 0) return;

        List<Vector2Int> passages = GetAllPassageCells();
        ShuffleList(passages);

        int stationCount = Mathf.Max(1, passages.Count / _stationCellsPerStation);

        // Reserve the first two cells for player placement so stations don't
        // spawn on the same cells PlacePlayers() will teleport players to.
        int reservedForOthers = 2;
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int i = reservedForOthers; i < passages.Count; i++)
            candidates.Add(passages[i]);

        stationCount = Mathf.Min(stationCount, candidates.Count);
        if (stationCount == 0) return;

        float sphereRadius = _cellSize * _stationSizeRatio;

        GameObject stationParent = new GameObject("Stations");
        stationParent.transform.SetParent(_mazeRoot.transform);

        for (int i = 0; i < stationCount; i++)
        {
            Vector3 pos = CellToWorld(candidates[i].x, candidates[i].y);
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"Station_{i}";
            sphere.transform.SetParent(stationParent.transform);
            sphere.transform.position = pos;
            sphere.transform.localScale = Vector3.one * (sphereRadius * 2f);
            if (_stationMaterial != null)
                sphere.GetComponent<Renderer>().sharedMaterial = _stationMaterial;
            sphere.AddComponent<Station>();
        }

        Debug.Log($"[MazeGenerator] Placed {stationCount} station(s) from {candidates.Count} candidate cells.");
    }

    // ── Boundary setup ────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes the freshly computed WorldBounds into each light controller so
    /// clamping is tight against the actual maze walls generated this run.
    /// The bounds are inset by half a wall thickness so the light center never
    /// visually overlaps the outer wall pillars.
    /// </summary>
    private void ApplyBoundsToLights()
    {
        float inset = _wallThickness * 0.5f;
        Rect bounds = new Rect(
            WorldBounds.xMin + inset,
            WorldBounds.yMin + inset,
            WorldBounds.width  - inset * 2f,
            WorldBounds.height - inset * 2f);

        _p1Light?.SetBounds(bounds);
        _p2Light?.SetBounds(bounds);
    }

    // ── Player placement ──────────────────────────────────────────────────────

    /// <summary>
    /// Moves P1 and P2 light + fish to random open (Passage) cells.
    /// P1 and P2 are guaranteed different cells.
    /// Light and fish for the same player share the same cell so the fish
    /// starts within follow range of its light.
    /// The Z coordinate of each object is preserved.
    /// </summary>
    private void PlacePlayers()
    {
        List<Vector2Int> open = GetAllPassageCells();
        ShuffleList(open);

        if (open.Count == 0)
        {
            Debug.LogWarning("[MazeGenerator] No passage cells available to place players.", this);
            return;
        }

        // Place each light in a unique cell, then snap the fish to its light's position.
        Vector3 p1Pos = open.Count > 0 ? CellToWorld(open[0].x, open[0].y) : _center;
        Vector3 p2Pos = open.Count > 1 ? CellToWorld(open[1].x, open[1].y) : p1Pos;

        MovePlayer(_p1Light != null ? _p1Light.transform : null, p1Pos);
        MovePlayer(_p2Light != null ? _p2Light.transform : null, p2Pos);

        // Fish snap to their light's final world position so they always start together.
        // Use Teleport() instead of MovePlayer() so the Rigidbody position AND
        // velocity are both reset — otherwise the physics engine fights the move.
        if (_p1Fish != null && _p1Light != null)
            _p1Fish.Teleport(_p1Light.transform.position);
        if (_p2Fish != null && _p2Light != null)
            _p2Fish.Teleport(_p2Light.transform.position);
    }

    /// <summary>Teleports a transform to worldXY while preserving its original Z.</summary>
    private static void MovePlayer(Transform t, Vector3 worldXY)
    {
        if (t == null) return;
        t.position = new Vector3(worldXY.x, worldXY.y, t.position.z);
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

}
