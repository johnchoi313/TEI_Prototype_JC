using UnityEngine;

/// <summary>
/// Teleports this camera rig's XY position to match the spawned fish for its player.
///
/// Called the moment PlayerManager fires OnPlayersSpawned — before the PreGame
/// countdown starts — so the fish is always in frame when players first see the screen.
/// Z position is preserved (camera depth must not change).
///
/// SETUP
///   Attach one instance to each camera rig GameObject (P1 rig and P2 rig).
///   Set playerIndex to match which rig this is on.
///   No other wiring needed.
/// </summary>
public class CameraSpawnAligner : MonoBehaviour
{
    [SerializeField] private PlayerIndex playerIndex;

    private void OnEnable()  => PlayerManager.OnPlayersSpawned += HandlePlayersSpawned;
    private void OnDisable() => PlayerManager.OnPlayersSpawned -= HandlePlayersSpawned;

    private void HandlePlayersSpawned(GameObject p1, GameObject p2)
    {
        GameObject fish = playerIndex == PlayerIndex.Player1 ? p1 : p2;
        if (fish == null) return;

        // Teleport XY only — preserve camera Z depth.
        Vector3 pos = transform.position;
        pos.x = fish.transform.position.x;
        pos.y = fish.transform.position.y;
        transform.position = pos;

        Debug.Log($"[CameraSpawnAligner] {playerIndex} rig aligned to spawn: {pos}");
    }
}
