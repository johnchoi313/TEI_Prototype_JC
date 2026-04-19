using UnityEngine;

/// <summary>
/// Captures and stores a Renderer's original material color at Awake so
/// power-up definitions can restore it on Remove().
///
/// Add this to any fish root GameObject that participates in the power-up system.
/// The target Renderer can live on a child — drag it into the Inspector field,
/// or leave it empty to auto-detect via GetComponentInChildren.
/// </summary>
public class OriginalColorTracker : MonoBehaviour
{
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");

    [Tooltip("The body MeshRenderer to track. Can be on a child. Auto-detected if left empty.")]
    [SerializeField] private Renderer _renderer;

    /// <summary>
    /// The renderer being tracked (may be on a child GameObject).
    /// Power-up definitions should use this instead of GetComponent&lt;Renderer&gt;() on the root.
    /// </summary>
    public Renderer TargetRenderer { get; private set; }

    public Color OriginalColor { get; private set; }

    private void Awake()
    {
        if (_renderer == null)
            _renderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();

        TargetRenderer = _renderer;

        // Force a per-instance material copy so color changes don't bleed to other objects.
        Material mat = _renderer.material;

        if (mat.HasProperty(BaseColorID))      OriginalColor = mat.GetColor(BaseColorID);
        else if (mat.HasProperty(ColorID))     OriginalColor = mat.GetColor(ColorID);
        else                                   OriginalColor = Color.white;
    }
}
