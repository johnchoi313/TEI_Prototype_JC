using UnityEngine;

/// <summary>
/// Identifies whether a power-up can interact with Problem objects.
/// Fix = resolves a found problem and scores a point.
/// Break = destroys a found problem with no score.
/// None = no problem interaction (e.g. color or movement power-ups).
/// </summary>
public enum PowerUpRole { None, Fix, Break }

/// <summary>
/// Abstract base for all power-up definitions.
///
/// Each concrete subclass IS the power-up's script — it carries both configuration
/// data (duration, name) and activation logic (Apply/Remove).
///
/// PowerUpManager holds a PowerUpDefinition reference per player and calls
/// Apply/Remove with the fish's GameObject. The concrete subclass fetches whatever
/// component it needs (Renderer, FishCharacter, Rigidbody, etc.) from that GameObject.
/// Swapping power-ups between players is just swapping these SO references.
///
/// To add a new power-up type:
///   1. Create a new class : PowerUpDefinition
///   2. Override Apply and Remove (get your needed component via go.GetComponent<T>())
///   3. [CreateAssetMenu] so you can create assets in the Project window
///   4. No changes needed to PowerUpManager
/// </summary>
public abstract class PowerUpDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Power-Up";

    [Header("Outline Color")]
    [Tooltip("Color shown on the FOV mask ring while this power-up is assigned.")]
    public Color outlineColor = Color.white;

    [Header("Problem Interaction")]
    [Tooltip("Whether this power-up fixes, breaks, or ignores problem objects.")]
    public PowerUpRole role = PowerUpRole.None;

    [Header("Timing")]
    [Tooltip("How long the power-up stays active after a fist trigger (seconds).")]
    public float activeDuration = 5f;

    /// <summary>Called by PowerUpManager the moment this power-up activates on a fish.</summary>
    public abstract void Apply(GameObject fishObject);

    /// <summary>Called by PowerUpManager when this power-up expires or is swapped away.</summary>
    public abstract void Remove(GameObject fishObject);
}
