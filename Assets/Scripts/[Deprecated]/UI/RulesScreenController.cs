using UnityEngine;

/// <summary>
/// Handles the Rules Screen panel button callback.
/// Visibility is managed by UIManager — this script only drives the transition.
///
/// SCENE SETUP
///   Add to the RulesPanel. Wire the "Start Game" button's OnClick to OnStartGamePressed().
/// </summary>
public class RulesScreenController : MonoBehaviour
{
    /// <summary>Wire to the "Start Game" button's OnClick event.</summary>
    public void OnStartGamePressed()
    {
        GameManager.Instance?.GoToPreGame();
    }
}
