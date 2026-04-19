using UnityEngine;

/// <summary>
/// Debug navigation utility. Safe with MediaPipe (no scene reload).
///
/// SPACE  — Snap each fish to its player's current FOV center so it's
///          immediately in view, wherever the hands are on screen.
///
/// ENTER  — Force both camera rigs back into together/single-screen mode.
///          Useful when split screen fired and you want to regroup.
///
/// Remove this component before shipping.
/// </summary>
public class SceneRestarter : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            SnapFishToFOV();

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ForceTogether();
    }

    // ── Space — snap fish to their FOV ────────────────────────────────────────

    private static void SnapFishToFOV()
    {
        if (FOVWorldCollider.Instance == null) return;

        var all = FindObjectsByType<FOVHighlightable>(FindObjectsSortMode.None);

        foreach (var fish in all)
        {
            Vector3 target = GetTargetForFish(fish);

            fish.transform.position = target;

            // Zero rigidbody velocity so it doesn't fly off after the snap.
            var rb = fish.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private static Vector3 GetTargetForFish(FOVHighlightable fish)
    {
        var left  = FOVWorldCollider.Instance.LeftHand;
        var right = FOVWorldCollider.Instance.RightHand;

        switch (fish.RespondTo)
        {
            case FOVHighlightable.FOVOwner.Player1Only:
                return ProjectViewportAtFishZ(
                    left.IsActive ? left.ViewportPosition : new Vector2(0.5f, 0.5f),
                    PlayerIndex.Player1, fish);

            case FOVHighlightable.FOVOwner.Player2Only:
                return ProjectViewportAtFishZ(
                    right.IsActive ? right.ViewportPosition : new Vector2(0.5f, 0.5f),
                    PlayerIndex.Player2, fish);

            default: // Any
                if (left.IsActive)
                    return ProjectViewportAtFishZ(left.ViewportPosition, PlayerIndex.Player1, fish);
                if (right.IsActive)
                    return ProjectViewportAtFishZ(right.ViewportPosition, PlayerIndex.Player2, fish);
                return ProjectViewportAtFishZ(new Vector2(0.5f, 0.5f), PlayerIndex.Player1, fish);
        }
    }

    /// <summary>
    /// Converts a viewport-space position (0-1) into a world point at the fish's Z depth,
    /// using the correct player camera. This mirrors FOVHighlightable.ViewportToWorldAtObjectZ.
    /// </summary>
    private static Vector3 ProjectViewportAtFishZ(Vector2 vp, PlayerIndex player, FOVHighlightable fish)
    {
        Camera cam = SplitScreenController.Instance != null
            ? SplitScreenController.Instance.GetCameraForPlayer(player)
            : Camera.main;
        if (cam == null) return fish.transform.position;

        float fishZ = fish.transform.position.z;

        if (cam.orthographic)
        {
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            float x = Mathf.Lerp(-w, w, vp.x) + cam.transform.position.x;
            float y = Mathf.Lerp(-h, h, vp.y) + cam.transform.position.y;
            return new Vector3(x, y, fishZ);
        }
        else
        {
            float dist = Mathf.Abs(cam.transform.position.z - fishZ);
            Vector3 world = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, dist));
            return new Vector3(world.x, world.y, fishZ);
        }
    }

    // ── Enter — force together mode ───────────────────────────────────────────

    private static void ForceTogether()
    {
        if (SplitScreenController.Instance != null)
            SplitScreenController.Instance.ForceTogether();
    }
}
