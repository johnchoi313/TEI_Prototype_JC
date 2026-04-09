using UnityEngine;

/// <summary>
/// Power-up that breaks problem objects (and, in future, destructible walls).
///
/// When activated (fist gesture), this power-up:
///   1. Applies a red tint to the fish for the active duration.
///   2. Fires PowerUpManager.OnPowerUpActivated, which ProblemObject picks up.
///      If the player is within a problem's proximity trigger, the problem is broken.
///
/// Create via: Assets menu → TEI/PowerUps → Break Power-Up
/// Assign to PowerUpManager._p1PowerUp or _p2PowerUp in the Inspector.
/// </summary>
[CreateAssetMenu(menuName = "TEI/PowerUps/Break Power-Up", fileName = "BreakPowerUp")]
public class BreakPowerUpDefinition : PowerUpDefinition
{
    [Header("Break Visuals")]
    [Tooltip("Tint applied to the fish renderer while this power-up is active.")]
    [SerializeField] private Color activeTint = new Color(1f, 0.3f, 0.3f, 1f);

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");

    private void OnEnable()
    {
        role = PowerUpRole.Break;
    }

    public override void Apply(GameObject fishObject)
    {
        if (fishObject == null) return;

        OriginalColorTracker tracker = fishObject.GetComponent<OriginalColorTracker>();
        // TargetRenderer resolves the correct renderer even when it lives on a child.
        Renderer rend = tracker != null ? tracker.TargetRenderer : fishObject.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        SetRendererColor(rend, activeTint);
    }

    public override void Remove(GameObject fishObject)
    {
        if (fishObject == null) return;

        OriginalColorTracker tracker = fishObject.GetComponent<OriginalColorTracker>();
        Renderer rend = tracker != null ? tracker.TargetRenderer : fishObject.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        Color restore = tracker != null ? tracker.OriginalColor : Color.white;
        SetRendererColor(rend, restore);
    }

    private static void SetRendererColor(Renderer rend, Color color)
    {
        Material mat = rend.material;
        if (mat.HasProperty(BaseColorID)) mat.SetColor(BaseColorID, color);
        else if (mat.HasProperty(ColorID)) mat.SetColor(ColorID, color);
    }
}
