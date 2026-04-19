using UnityEngine;

/// <summary>
/// Marks the world-space spawn position for one player in a level scene.
///
/// SETUP (per level scene)
///   Create two empty GameObjects, attach SpawnPointMarker to each,
///   set the player field, and position them where you want each fish to appear.
///   LevelController.GetSpawnPoint() finds them automatically — no Inspector wiring.
///
/// Follows the same pattern as MinimapBoundsMarker: drop it in the scene and forget it.
/// </summary>
public class SpawnPointMarker : MonoBehaviour
{
    [Tooltip("Which player this spawn point belongs to.")]
    public PlayerIndex player;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Color c = player == PlayerIndex.Player1
            ? new Color(0.2f, 0.6f, 1f, 0.8f)
            : new Color(1f, 0.4f, 0.2f, 0.8f);

        Gizmos.color = c;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);

        UnityEditor.Handles.color = c;
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.8f,
            player == PlayerIndex.Player1 ? "Spawn P1" : "Spawn P2");
    }
#endif
}
