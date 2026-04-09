using UnityEngine;

/// <summary>
/// Dummy power-up: changes the fish to a unique color while active.
/// Works with any fish GameObject that has a Renderer.
/// </summary>
[CreateAssetMenu(menuName = "TEI/PowerUps/Color", fileName = "ColorPowerUp")]
public class ColorPowerUpDefinition : PowerUpDefinition
{
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");

    [Header("Effect")]
    public Color fishColor = Color.white;

    public override void Apply(GameObject fishObject)
    {
        SetColor(fishObject, fishColor);
    }

    public override void Remove(GameObject fishObject)
    {
        // Restore the original color captured on the fish's MaterialPropertyTracker,
        // or fall back to white if no tracker is present.
        var tracker = fishObject.GetComponent<OriginalColorTracker>();
        if (tracker != null)
            SetColor(fishObject, tracker.OriginalColor);
    }

    private static void SetColor(GameObject go, Color color)
    {
        // TargetRenderer resolves the correct renderer even when it lives on a child.
        var tracker = go.GetComponent<OriginalColorTracker>();
        var r = tracker != null ? tracker.TargetRenderer : go.GetComponentInChildren<Renderer>();
        if (r == null) return;

        // Use the per-instance material so we don't affect other objects.
        Material mat = Application.isPlaying ? r.material : r.sharedMaterial;
        if (mat.HasProperty(BaseColorID))      mat.SetColor(BaseColorID, color);
        else if (mat.HasProperty(ColorID))     mat.SetColor(ColorID, color);
    }
}
