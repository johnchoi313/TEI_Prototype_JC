using TMPro;
using UnityEngine;

/// <summary>
/// Populates the Level Complete panel with problems-solved counts and manages
/// the "Next Level" / "Session Complete" button visibility.
/// Panel visibility is managed by UIManager — this script only handles data.
///
/// SCENE SETUP
///   Add to the LevelCompletePanel GameObject.
///   Wire _p1ResultText and _p2ResultText to your two TMP labels.
///   Wire _nextLevelButton and _sessionCompleteLabel for navigation.
///   Wire the Next Level button's OnClick → OnNextLevelPressed().
/// </summary>
public class LevelCompleteController : MonoBehaviour
{
    [Header("Results")]
    [Tooltip("TMP label that will show how many problems P1 fixed.")]
    [SerializeField] private TMP_Text _p1ResultText;

    [Tooltip("TMP label that will show how many problems P2 fixed.")]
    [SerializeField] private TMP_Text _p2ResultText;

    [SerializeField] private string _p1Label = "Player 1 fixed: ";
    [SerializeField] private string _p2Label = "Player 2 fixed: ";

    [Header("Navigation")]
    [Tooltip("GameObject containing the 'Next Level' button. Hidden on the final level.")]
    [SerializeField] private GameObject _nextLevelButton;

    [Tooltip("Label shown (instead of button) when this is the final level.")]
    [SerializeField] private GameObject _sessionCompleteLabel;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        // UIManager calls SetActive(true) here when entering LevelComplete.
        // Refresh immediately so numbers are current the moment the panel appears.
        Refresh();
    }

    // ── Button callback ───────────────────────────────────────────────────────

    /// <summary>Wire to the "Next Level" button's OnClick event.</summary>
    public void OnNextLevelPressed()
    {
        GameManager.Instance?.LoadNextLevel();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (ScoreManager.Instance != null)
        {
            if (_p1ResultText != null)
                _p1ResultText.text = $"{_p1Label}{ScoreManager.Instance.P1ProblemsSolved}";
            if (_p2ResultText != null)
                _p2ResultText.text = $"{_p2Label}{ScoreManager.Instance.P2ProblemsSolved}";
        }

        bool isLast = LevelManager.Instance == null || LevelManager.Instance.IsLastLevel;

        _nextLevelButton?.SetActive(!isLast);
        _sessionCompleteLabel?.SetActive(isLast);
    }
}
