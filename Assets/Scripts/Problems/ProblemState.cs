/// <summary>
/// Lifecycle states for a ProblemObject.
///
/// Idle    — exists in the world but not yet discovered (dimly lit, no minimap icon).
/// Found   — FOV has illuminated it; minimap icon appears and persists.
/// Fixed   — player used Fix power-up while nearby; scores a point, then despawns.
/// Broken  — player used Break power-up while nearby; no score, then despawns.
/// </summary>
public enum ProblemState
{
    Idle,
    Found,
    Fixed,
    Broken,
}
