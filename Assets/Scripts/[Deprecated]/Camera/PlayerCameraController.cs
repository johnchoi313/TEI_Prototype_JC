using UnityEngine;

/// <summary>
/// Moves this camera rig based on the player's FOV viewport position.
///
/// SPLIT MODE — both players:
///   Camera pans freely. FOV at centre = no movement. FOV at edge = pan at
///   panSpeed. Viewport coords remapped to the player's assigned half.
///
/// TOGETHER MODE — P1:
///   Pans normally. P1's rig IS the shared display camera.
///
/// TOGETHER MODE — P2:
///   Shadows P1's rig (SmoothDamp toward P1's position).
///   This keeps P2 co-located with P1 so coordinate spaces match.
///   THE SPLIT IS NOT BLOCKED by this — the split trigger in
///   SplitScreenController uses FOV world-position distance (hand distance
///   on screen), which is independent of where the camera rigs are.
///   Players simply move their hands apart → world distance exceeds threshold
///   → split fires → P2 stops shadowing and pans independently.
///   P2 starts from P1's position and pans toward P2's FOV: a smooth visual
///   split from one shared view into two independent ones.
/// </summary>
public class PlayerCameraController : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private PlayerIndex playerIndex;

    [Header("Pan")]
    [Tooltip("Max world-unit pan speed when FOV is at the screen edge.")]
    [SerializeField] private float panSpeed = 6f;

    [Tooltip("Fraction from viewport centre (0–0.5) that produces no pan.")]
    [SerializeField] private float deadZone = 0.1f;

    [Tooltip("Fraction of the camera frame edge (each side) treated as a buffer zone. " +
             "Max pan speed is reached before the player's hand reaches the camera boundary, " +
             "keeping hands away from the frame edge where MediaPipe tracking degrades. " +
             "0.15 = outer 15% of each side is the buffer.")]
    [SerializeField, Range(0f, 0.25f)] private float trackingMargin = 0.15f;

    [Header("Together Mode — P2 shadow")]
    [Tooltip("How quickly P2 catches up to P1's rig position in together mode.")]
    [SerializeField] private float shadowSmoothTime = 0.25f;

    private Vector3 _shadowVelocity;

    private void LateUpdate()
    {
        bool inSplit = SplitScreenController.Instance != null &&
                       SplitScreenController.Instance.CurrentState == SplitScreenController.SplitState.SplitScreen;

        // P2 in together mode: always shadow P1 regardless of game state.
        // Co-location is camera infrastructure — the shader compositor requires
        // P2's rig to be at P1's position so both render textures share a
        // consistent world space. Gating this on Playing caused the P1 RT to
        // go black when split fired because P2's rig drifted from P1's position.
        if (playerIndex == PlayerIndex.Player2 && !inSplit)
        {
            Camera p1Cam = SplitScreenController.Instance != null
                ? SplitScreenController.Instance.GetCameraForPlayer(PlayerIndex.Player1)
                : null;

            if (p1Cam != null)
            {
                Vector3 target = new Vector3(
                    p1Cam.transform.position.x,
                    p1Cam.transform.position.y,
                    transform.position.z);
                transform.position = Vector3.SmoothDamp(
                    transform.position, target, ref _shadowVelocity, shadowSmoothTime);
            }
            return;
        }

        // Panning is gameplay — only move the camera during active play.
        // SplitScreenStateGate ensures split cannot trigger outside Playing,
        // so the inSplit branch below is also only reachable during Playing.
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing)
            return;

        // Split mode (both players) and together mode (P1): pan toward FOV.
        Vector2 vp = GetHandViewport();
        transform.position += ComputePanDelta(vp) * Time.deltaTime;
    }

    private Vector2 GetHandViewport()
    {
        if (FOVWorldCollider.Instance == null) return new Vector2(0.5f, 0.5f);

        FOVWorldCollider.HandWorldState hand = playerIndex == PlayerIndex.Player1
            ? FOVWorldCollider.Instance.LeftHand
            : FOVWorldCollider.Instance.RightHand;

        if (!hand.IsActive) return new Vector2(0.5f, 0.5f);

        Vector2 vp = hand.ViewportPosition;

        // In split mode remap from full-screen to the player's assigned half,
        // so FOV at the edge of their half = max pan speed (not half of max).
        if (SplitScreenController.Instance != null &&
            SplitScreenController.Instance.CurrentState == SplitScreenController.SplitState.SplitScreen)
        {
            Rect bounds = SplitScreenController.Instance.GetPlayerViewportBounds(playerIndex);
            vp.x = Mathf.Clamp01((vp.x - bounds.xMin) / bounds.width);
        }

        // Tracking margin: remap [margin, 1-margin] → [0, 1] so max pan speed
        // is reachable before the hand reaches the camera frame edge, keeping
        // tracking away from the boundary where MediaPipe degrades.
        if (trackingMargin > 0f)
        {
            vp.x = Remap(vp.x, trackingMargin, 1f - trackingMargin);
            vp.y = Remap(vp.y, trackingMargin, 1f - trackingMargin);
        }

        return vp;
    }

    private Vector3 ComputePanDelta(Vector2 vp)
    {
        float dx = (vp.x - 0.5f) * 2f;
        float dy = (vp.y - 0.5f) * 2f;
        float boundary = deadZone * 2f;
        float ox = Remap(Mathf.Abs(dx), boundary, 1f) * Mathf.Sign(dx);
        float oy = Remap(Mathf.Abs(dy), boundary, 1f) * Mathf.Sign(dy);
        return new Vector3(ox, oy, 0f) * panSpeed;
    }

    private static float Remap(float v, float from, float to)
        => Mathf.Clamp01((v - from) / (to - from));
}
