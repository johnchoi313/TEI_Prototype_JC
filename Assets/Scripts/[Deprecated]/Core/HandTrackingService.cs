using System;
using UnityEngine;

/// <summary>
/// Canonical player index. Player1 defaults to left hand, Player2 to right hand.
/// The mapping is configurable in the inspector.
/// </summary>
public enum PlayerIndex { Player1 = 0, Player2 = 1 }

/// <summary>
/// Single-frame snapshot of a player's hand state.
/// Decouples all gameplay systems from MediaPipe types.
/// </summary>
public struct HandData
{
    /// <summary>True if the hand is currently visible.</summary>
    public bool IsPresent;

    /// <summary>
    /// Normalized viewport position (0-1). Raw from MediaPipe — Y is flipped
    /// relative to Unity viewport. Apply flipY before world-projecting.
    /// </summary>
    public Vector2 ViewportPosition;

    /// <summary>Apparent hand size proxy — larger = closer to camera.</summary>
    public float HandScale;

    public bool IsFist;
    public bool FistDown;   // true for exactly one frame when fist closes
    public bool FistUp;     // true for exactly one frame when fist opens
}

/// <summary>
/// Single source of truth for hand tracking data. Wraps TEIHandTrackingFilter
/// and TEIHandGestureInterpreter and exposes a clean PlayerIndex-keyed API.
///
/// Place this on the same GameObject as the TEI hand tracking stack.
/// Lives in the persistent base scene (DontDestroyOnLoad via GameManager).
/// </summary>
public class HandTrackingService : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] private TEIHandTrackingFilter filter;
    [SerializeField] private TEIHandGestureInterpreter gestures;

    [Header("Hand-to-Player Mapping")]
    [Tooltip("Assign the left hand to Player1 (right hand → Player2). Uncheck to swap.")]
    [SerializeField] private bool player1UsesLeftHand = true;

    /// <summary>Fired every frame for each player with their latest HandData.</summary>
    public event Action<PlayerIndex, HandData> OnHandUpdated;

    private HandData _p1;
    private HandData _p2;

    public HandData GetHandData(PlayerIndex index) => index == PlayerIndex.Player1 ? _p1 : _p2;

    private void Update()
    {
        if (filter == null) return;

        bool p1Left = player1UsesLeftHand;
        bool p2Left = !player1UsesLeftHand;

        _p1 = BuildHandData(
            isLeft:    p1Left,
            hasHand:   p1Left ? filter.HasLeftHand    : filter.HasRightHand,
            position:  p1Left ? filter.LeftHand       : filter.RightHand,
            scale:     p1Left ? filter.LeftHandScale  : filter.RightHandScale,
            isFist:    p1Left ? (gestures != null && gestures.IsLeftFist)  : (gestures != null && gestures.IsRightFist),
            fistDown:  p1Left ? (gestures != null && gestures.LeftFistDown) : (gestures != null && gestures.RightFistDown),
            fistUp:    p1Left ? (gestures != null && gestures.LeftFistUp)   : (gestures != null && gestures.RightFistUp)
        );

        _p2 = BuildHandData(
            isLeft:    p2Left,
            hasHand:   p2Left ? filter.HasLeftHand    : filter.HasRightHand,
            position:  p2Left ? filter.LeftHand       : filter.RightHand,
            scale:     p2Left ? filter.LeftHandScale  : filter.RightHandScale,
            isFist:    p2Left ? (gestures != null && gestures.IsLeftFist)  : (gestures != null && gestures.IsRightFist),
            fistDown:  p2Left ? (gestures != null && gestures.LeftFistDown) : (gestures != null && gestures.RightFistDown),
            fistUp:    p2Left ? (gestures != null && gestures.LeftFistUp)   : (gestures != null && gestures.RightFistUp)
        );

        OnHandUpdated?.Invoke(PlayerIndex.Player1, _p1);
        OnHandUpdated?.Invoke(PlayerIndex.Player2, _p2);
    }

    private static HandData BuildHandData(
        bool isLeft, bool hasHand, Vector2 position, float scale,
        bool isFist, bool fistDown, bool fistUp)
    {
        return new HandData
        {
            IsPresent        = hasHand,
            ViewportPosition = position,
            HandScale        = scale,
            IsFist           = isFist,
            FistDown         = fistDown,
            FistUp           = fistUp,
        };
    }
}
