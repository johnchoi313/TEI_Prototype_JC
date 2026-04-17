using UnityEngine;

/// <summary>
/// Centralized audio playback singleton.
///
/// Two playback modes:
///   PlayAt(clip, pos)  — 3D world sound, attenuates with distance. Use for
///                        problem interactions (found, fixed, broken) where
///                        spatial origin matters.
///   PlayUI(clip)       — Flat 2D sound, no attenuation. Use for button clicks,
///                        power-up swap confirmation, etc.
///
/// SCENE SETUP
///   • Place a root GameObject named "SoundManager" in your persistent scene.
///   • Attach this script to it.
///   • Add a child AudioSource (spatialBlend = 0, playOnAwake = false) and drag
///     it into the _uiSource Inspector field.
///   • Ensure exactly one AudioListener exists in the scene (usually on P1's camera).
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private AudioSource _uiSource;
    [SerializeField][Range(0f, 1f)] private float _masterVolume = 1f;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Play a sound at a world position. Attenuates with distance from the AudioListener.
    /// Use for problem interactions — found, fixed, broken.
    /// </summary>
    public void PlayAt(AudioClip clip, Vector3 worldPos)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, worldPos, _masterVolume);
    }

    /// <summary>
    /// Play a flat 2D sound with no spatial falloff.
    /// Use for UI buttons, power-up swap, and other non-diegetic events.
    /// </summary>
    public void PlayUI(AudioClip clip)
    {
        if (clip == null || _uiSource == null) return;
        _uiSource.PlayOneShot(clip, _masterVolume);
    }
}
