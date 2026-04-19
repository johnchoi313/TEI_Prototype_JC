using TMPro;
using UnityEngine;

/// <summary>
/// Updates the countdown text during GameState == PreGame.
/// Visibility is managed by UIManager.
///
/// SCENE SETUP
///   Add to the PreGamePanel. Wire _countdownText to a TMP_Text child.
///   No buttons — this panel auto-advances when RoundManager's countdown ends.
///
/// DISPLAY: tick 3 → "3", tick 2 → "2", tick 1 → "1", tick 0 → "GO!"
/// </summary>
public class PreGameCountdownController : MonoBehaviour
{
    [SerializeField] private TMP_Text _countdownText;

    private void OnEnable()
    {
        RoundManager.OnPreGameTick += HandlePreGameTick;
        if (_countdownText != null) _countdownText.text = string.Empty;
    }

    private void OnDisable()
    {
        RoundManager.OnPreGameTick -= HandlePreGameTick;
    }

    private void HandlePreGameTick(int secondsRemaining)
    {
        if (_countdownText == null) return;
        _countdownText.text = secondsRemaining == 0 ? "GO!" : secondsRemaining.ToString();
    }
}
