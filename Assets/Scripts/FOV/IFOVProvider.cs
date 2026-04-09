using UnityEngine;

/// <summary>
/// World-space and screen-space representation of an active FOV circle at a single point in time.
/// Produced by FOVController; consumed by FishCharacter and other gameplay systems.
/// </summary>
public struct FOVState
{
    /// <summary>Whether the hand is currently tracked and the FOV is visible.</summary>
    public bool IsActive;

    /// <summary>
    /// Viewport position passed to the shader (0-1, Y-flipped from raw MediaPipe coords).
    /// </summary>
    public Vector2 ViewportPosition;

    /// <summary>World-space center of the FOV circle, projected onto the game plane.</summary>
    public Vector3 WorldPosition;

    /// <summary>
    /// Screen-space radius as a fraction of screen width (0-1).
    /// Mirrors exactly what the FOV shader uses — so the visual circle matches gameplay.
    /// </summary>
    public float ScreenRadius;

    /// <summary>
    /// World-space radius in scene units. Derived from ScreenRadius + camera frustum at GamePlaneZ.
    /// Use this for all gameplay distance checks (e.g., is the fish inside the FOV?).
    /// </summary>
    public float WorldRadius;
}

/// <summary>
/// Implemented by FOVController. Gives gameplay systems read access to FOV state
/// without depending on FOVController directly.
/// </summary>
public interface IFOVProvider
{
    FOVState GetFOVState();
}
