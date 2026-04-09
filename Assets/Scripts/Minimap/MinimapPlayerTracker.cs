using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives one player's minimap icons (character dot + FOV ring) every frame.
///
/// Two instances live inside the Minimap prefab — one per player.
/// MinimapController calls Initialize() on both at Start and the trackers
/// are self-sufficient from that point forward.
///
/// COLOR RULES
///   Idle            : _defaultColor (set per tracker in the Inspector)
///   Power-up active : the power-up's outlineColor (from PowerUpManager)
///
/// FOV ICON
///   Position  — world position of the hand (or last known ghost position)
///   Size      — diameter derived from world-space FOV radius (its just the radius, no need to *2)
///   Alpha     — full when the hand is actively tracked, _ghostAlpha when ghosted
///   Hidden    — when the hand state is fully inactive (never been seen)
///
/// CHARACTER ICON
///   Position  — world position of the fish character, resolved via PlayerManager
///               (which handles both runtime-spawned and scene-placed characters).
///   Hidden    — only when PlayerManager has no reference for this player
/// </summary>
public class MinimapPlayerTracker : MonoBehaviour
{
    [Header("Icons")]
    [Tooltip("Filled circle image representing the player's fish character.")]
    [SerializeField] private Image _characterIcon;

    [Tooltip("Outline circle image representing the player's FOV. " +
             "Should use a ring/hollow-circle sprite so the character icon shows through.")]
    [SerializeField] private Image _fovIcon;

    [Header("Appearance")]
    [Tooltip("Base color used when no power-up is active.")]
    [SerializeField] private Color _defaultColor = Color.white;

    [Tooltip("Alpha applied to both icons when the hand is in ghost state (last known position).")]
    [Range(0f, 1f)]
    [SerializeField] private float _ghostAlpha = 0.35f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private MinimapController _map;
    private PlayerIndex       _playerIndex;
    private bool              _initialized;

    private Transform _cachedCharacterTransform;

    // ── Called by MinimapController ───────────────────────────────────────────

    public void Initialize(MinimapController map, PlayerIndex index)
    {
        _map         = map;
        _playerIndex = index;
        _initialized = true;

        // Hide both icons until we have real data.
        SetIconsActive(false);
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_initialized) return;

        UpdateCharacterIcon();
        UpdateFOVIcon();
    }

    // ── Character icon ────────────────────────────────────────────────────────

    private void UpdateCharacterIcon()
    {
        if (_characterIcon == null) return;

        // Cache the transform once — PlayerManager resolves it whether the character
        // was runtime-spawned or assigned directly as a scene reference.
        if (_cachedCharacterTransform == null && PlayerManager.Instance != null)
        {
            GameObject playerGO = PlayerManager.Instance.GetPlayerInstance(_playerIndex);
            if (playerGO != null)
                _cachedCharacterTransform = playerGO.transform;
        }

        if (_cachedCharacterTransform == null)
        {
            _characterIcon.gameObject.SetActive(false);
            return;
        }

        _characterIcon.gameObject.SetActive(true);
        _characterIcon.rectTransform.anchoredPosition = _map.WorldToMap(_cachedCharacterTransform.position);
        _characterIcon.color = CurrentColor(isGhost: false);
    }

    // ── FOV icon ──────────────────────────────────────────────────────────────

    private void UpdateFOVIcon()
    {
        if (_fovIcon == null) return;

        if (FOVWorldCollider.Instance == null)
        {
            _fovIcon.gameObject.SetActive(false);
            return;
        }

        FOVWorldCollider.HandWorldState state = GetHandState();

        if (!state.IsActive)
        {
            _fovIcon.gameObject.SetActive(false);
            return;
        }

        _fovIcon.gameObject.SetActive(true);
        _fovIcon.rectTransform.anchoredPosition = _map.WorldToMap(state.WorldPosition);


        float radius = _map.WorldRadiusToMap(state.WorldRadius);
        _fovIcon.rectTransform.sizeDelta = Vector2.one * radius;

        _fovIcon.color = CurrentColor(isGhost: state.IsGhost);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the correct display color.
    /// Active power-up overrides the default hue; ghost state reduces alpha.
    /// </summary>
    private Color CurrentColor(bool isGhost)
    {
        Color baseColor = _defaultColor;

        if (PowerUpManager.Instance != null)
        {
            bool powerUpActive = _playerIndex == PlayerIndex.Player1
                ? PowerUpManager.Instance.P1Active
                : PowerUpManager.Instance.P2Active;

            if (powerUpActive)
            {
                baseColor = _playerIndex == PlayerIndex.Player1
                    ? PowerUpManager.Instance.P1PowerUpColor
                    : PowerUpManager.Instance.P2PowerUpColor;
            }
        }

        if (isGhost)
            baseColor.a *= _ghostAlpha;

        return baseColor;
    }

    /// <summary>
    /// Returns the hand state for this player.
    /// Player1 maps to LeftHand, Player2 to RightHand — matching the
    /// assignment in FOVWorldCollider.Update().
    /// </summary>
    private FOVWorldCollider.HandWorldState GetHandState()
    {
        return _playerIndex == PlayerIndex.Player1
            ? FOVWorldCollider.Instance.LeftHand
            : FOVWorldCollider.Instance.RightHand;
    }

    private void SetIconsActive(bool active)
    {
        if (_characterIcon != null) _characterIcon.gameObject.SetActive(active);
        if (_fovIcon       != null) _fovIcon.gameObject.SetActive(active);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_characterIcon == null)
            Debug.LogWarning($"[MinimapPlayerTracker ({name})] _characterIcon is not assigned.", this);
        if (_fovIcon == null)
            Debug.LogWarning($"[MinimapPlayerTracker ({name})] _fovIcon is not assigned.", this);
    }
#endif
}
