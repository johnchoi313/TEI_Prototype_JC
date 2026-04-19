using UnityEngine;

/// <summary>
/// Prevents split-screen from activating during MainMenu and Rules screens.
/// Split is allowed from PreGame onward — players should already be oriented
/// in their halves during the 3-2-1 countdown.
///
/// Camera panning is separately gated by PlayerCameraController (Playing only),
/// so removing the Playing requirement here does not let cameras pan early.
///
/// Works by calling SplitScreenController.ForceTogether() every Update during
/// MainMenu and Rules. Does NOT modify SplitScreenController.
///
/// SCENE SETUP
///   Add to any persistent Manager GameObject in GameManagerSetup.
///   No inspector wiring required.
/// </summary>
public class SplitScreenGate : MonoBehaviour
{
    private bool _allowSplit = false;

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
        // Sync with current state at boot (handles debug start states).
        if (GameManager.Instance != null)
            _allowSplit = IsSplitAllowed(GameManager.Instance.State);
    }

    private void HandleStateChanged(GameState prev, GameState next)
    {
        _allowSplit = IsSplitAllowed(next);
    }

    private void Update()
    {
        if (!_allowSplit && SplitScreenController.Instance != null)
            SplitScreenController.Instance.ForceTogether();
    }

    private static bool IsSplitAllowed(GameState state)
    {
        // Block split only on non-gameplay screens. Allow from PreGame onward
        // so the split is live during the countdown.
        return state == GameState.PreGame ||
               state == GameState.Playing ||
               state == GameState.LevelComplete;
    }
}
