using UnityEngine;

/// <summary>
/// Positions a world-space quad that displays a player's camera render texture
/// through a circular shader mask.
///
/// The quad's position is set (not accumulated) each frame from the raw input
/// axis of the paired PlayerLightController:
///   position = _restPosition + (RawInput * _maxOffset)
///
/// Because it is additive from a fixed rest point the quad can never drift
/// off-screen regardless of input duration.
///
/// SCENE SETUP:
///   1. Create a Quad (3D Object → Quad). Rotate X by 90° if your scene is XY-plane.
///      Actually for a 2D/XY maze leave rotation at default and face it toward the camera.
///   2. Assign a material whose shader draws a circle (e.g. a simple Unlit shader
///      with a circular clip in the fragment stage, or a Shader Graph with a circle
///      node masking _MainTex). Set _MainTex to the player's RenderTexture.
///   3. Attach this component. Assign _lightController in the Inspector.
///   4. Tune _restPosition (world units) and _maxOffset.
/// </summary>
public class PlayerCameraCircle : MonoBehaviour
{
    [Header("Input Source")]
    [Tooltip("The PlayerLightController whose RawInput drives this quad's position.")]
    [SerializeField] private PlayerLightController _lightController;

    [Header("Layout")]
    [Tooltip("World-space XY position of the quad when input is zero.")]
    [SerializeField] private Vector2 _restPosition = new Vector2(-5f, 0f);

    [Tooltip("Maximum world-unit offset from rest position at full input (magnitude 1).")]
    [SerializeField] private float _maxOffset = 2f;

    [Tooltip("How smoothly the quad follows the input. 0 = instant snap, higher = more lag.")]
    [SerializeField] private float _smoothTime = 0.08f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Vector2 _currentPos;
    private Vector2 _smoothVelocity;
    private float   _fixedZ;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _fixedZ     = transform.position.z;
        _currentPos = _restPosition;
        transform.position = new Vector3(_restPosition.x, _restPosition.y, _fixedZ);
    }

    private void Update()
    {
        Vector2 input  = _lightController != null ? _lightController.RawInput : Vector2.zero;
        Vector2 target = _restPosition + input * _maxOffset;

        _currentPos = Vector2.SmoothDamp(_currentPos, target, ref _smoothVelocity, _smoothTime);
        transform.position = new Vector3(_currentPos.x, _currentPos.y, _fixedZ);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        Gizmos.DrawWireSphere(new Vector3(_restPosition.x, _restPosition.y, transform.position.z), _maxOffset);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawSphere(new Vector3(_restPosition.x, _restPosition.y, transform.position.z), 0.15f);
    }
#endif
}
