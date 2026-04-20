using UnityEngine;

/// <summary>
/// Marks a maze station sphere as collectible.
/// Attach automatically by MazeGenerator.PlaceStations().
/// Call Collect() to award the score and destroy the object.
/// </summary>
public class Station : MonoBehaviour
{
    /// <summary>Awards the station score and destroys this GameObject.</summary>
    public void Collect()
    {
        ScoreTracker.Instance?.AddStationCollect();
        Destroy(gameObject);
    }
}
