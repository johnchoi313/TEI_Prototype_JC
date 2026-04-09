using UnityEngine;

/// <summary>
/// ScriptableObject data asset for a problem type.
///
/// Create via: Assets menu → TEI → Problem Definition
///
/// Each problem prefab holds a reference to one of these assets, defining its
/// visual appearance, scoring value, and timing behavior.
/// </summary>
[CreateAssetMenu(menuName = "TEI/Problem Definition", fileName = "ProblemDefinition")]
public class ProblemDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Readable name for editor/debug purposes.")]
    public string problemName = "Problem";

    [Header("Visuals")]
    [Tooltip("Color applied to the problem's renderer while in Idle state (undiscovered).")]
    public Color idleColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    [Tooltip("Color applied to the problem's renderer once discovered by FOV.")]
    public Color foundColor = Color.yellow;

    [Tooltip("Icon shown on the minimap when the problem is Found, Fixed, or Broken.")]
    public Sprite minimapIcon;

    [Tooltip("Minimap icon tint when Found.")]
    public Color minimapFoundColor = Color.yellow;

    [Tooltip("Minimap icon tint when Fixed.")]
    public Color minimapFixedColor = Color.green;

    [Tooltip("Minimap icon tint when Broken.")]
    public Color minimapBrokenColor = Color.red;

    [Header("Scoring")]
    [Tooltip("Points awarded to the player who fixes this problem.")]
    public int pointValue = 1;

    [Header("Timing")]
    [Tooltip("Seconds after Fixed or Broken before the object destroys itself.")]
    public float despawnDelay = 3f;

    [Tooltip("Seconds after despawn before the spawn pocket becomes available again.")]
    public float respawnDelay = 30f;

    [Header("Audio")]
    public AudioClip foundSound;
    public AudioClip fixedSound;
    public AudioClip brokenSound;
}
