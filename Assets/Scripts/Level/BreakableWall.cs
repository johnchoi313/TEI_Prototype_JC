using UnityEngine;

/// <summary>
/// Marks a maze wall segment as breakable.
/// Call Break() (or destroy the GameObject directly) to remove it at runtime.
/// Attach any physics / animation logic here as the project grows.
/// </summary>
public class BreakableWall : MonoBehaviour
{
    /// <summary>Destroys this wall piece immediately.</summary>
    public void Break()
    {
        Destroy(gameObject);
    }
}
