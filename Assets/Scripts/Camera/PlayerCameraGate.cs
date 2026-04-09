using UnityEngine;

/// <summary>
/// Disables PlayerCameraController (and therefore all camera movement and split screen)
/// when the game is not in Playing state.
///
/// FOV circles (hand tracking shader) are unaffected — they always track hands.
/// Only the camera rig movement and viewport splitting are gated.
///
/// Does NOT modify PlayerCameraController.cs — only enables/disables the component.
///
/// SCENE SETUP
///   Add to the same GameObject as PlayerCameraController, OR any persistent
///   Manager GameObject. Wire _cameraController in the Inspector.
/// </summary>
public class PlayerCameraGate : MonoBehaviour
{
    [Tooltip("The PlayerCameraController component to enable/disable. " +
             "Drag the PlayerCameraController component reference here.")]
    [SerializeField] private MonoBehaviour _cameraController;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Start()
    {
        // Sync immediately with current state.
        if (GameManager.Instance != null)
            SetCameraActive(GameManager.Instance.State == GameState.Playing);
    }

    private void HandleStateChanged(GameState prev, GameState next)
    {
        SetCameraActive(next == GameState.Playing);
    }

    private void SetCameraActive(bool active)
    {
        if (_cameraController != null)
            _cameraController.enabled = active;
    }
}
