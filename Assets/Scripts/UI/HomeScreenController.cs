using UnityEngine;

/// <summary>
/// Handles the Home Screen panel button callback.
/// Visibility is managed by UIManager — this script only drives the transition.
///
/// SCENE SETUP
///   Add to the HomeScreenPanel. Wire the Start button's OnClick to OnStartPressed().
/// </summary>
public class HomeScreenController : MonoBehaviour
{
    /// <summary>Wire to the Start button's OnClick event.</summary>
    public void OnStartPressed()
    {
        GameManager.Instance?.GoToRules();
    }
}
