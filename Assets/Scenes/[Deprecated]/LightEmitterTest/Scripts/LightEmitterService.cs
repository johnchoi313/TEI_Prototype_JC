using System;
using UnityEngine;

namespace HandTracking
{
    [DefaultExecutionOrder(-50)] // run early so camera/fish see updated data this frame
    public class LightEmitterService : MonoBehaviour
    {
        [Header("Refs")]
        public HandTrackingHub hub;
        public Camera gameplayCamera;

        [Header("Mapping")]
        public HandSide leftSide = HandSide.Left;
        public HandSide rightSide = HandSide.Right;
        public int landmarkIndex = 8; // index tip, same as your old prototype

        [Header("Swim Plane")]
        public float swimPlaneZ = 0f; // gameplay plane for fish steering

        [Header("Presence Debounce")]
        [Tooltip("Frames required to turn ON after detection.")]
        public int onFrames = 2;
        [Tooltip("Frames allowed to miss before turning OFF.")]
        public int offFrames = 5;

        [Header("Smoothing")]
        [Tooltip("Higher = snappier. Exponential smoothing.")]
        public float positionSmooth = 20f;

        [Header("Light Radii")]
        public float radiusWorld = 2.5f;
        [Range(0.02f, 0.5f)] public float radiusScreen01 = 0.12f;

        public struct LightEmitterState
        {
            public bool active;
            public Vector2 screen01;   // 0..1 viewport
            public Vector3 worldPos;   // on swim plane
            public float radiusWorld;
            public float radiusScreen01;
        }

        public LightEmitterState Left => _left;
        public LightEmitterState Right => _right;

        public event Action<HandSide, LightEmitterState> OnEmitterUpdated;

        LightEmitterState _left, _right;
        int _leftOnCount, _leftOffCount;
        int _rightOnCount, _rightOffCount;

        void Awake()
        {
            if (hub == null) hub = FindFirstObjectByType<HandTrackingHub>();
            if (gameplayCamera == null) gameplayCamera = Camera.main;

            _left.radiusWorld = radiusWorld;
            _right.radiusWorld = radiusWorld;
            _left.radiusScreen01 = radiusScreen01;
            _right.radiusScreen01 = radiusScreen01;
        }

        void Update()
        {
            UpdateSide(leftSide, ref _left, ref _leftOnCount, ref _leftOffCount);
            UpdateSide(rightSide, ref _right, ref _rightOnCount, ref _rightOffCount);
        }

        void UpdateSide(
            HandSide side,
            ref LightEmitterState state,
            ref int onCount,
            ref int offCount)
        {
            // IMPORTANT: do NOT do `hub != null && hub.TryGetHand(... out var hand)`
            // because HandFrame is a struct and the compiler can complain about definite assignment.
            if (hub == null)
            {
                HandleNoHand(side, ref state, ref onCount, ref offCount);
                return;
            }

            HandFrame hand;
            if (!hub.TryGetHand(side, out hand))
            {
                HandleNoHand(side, ref state, ref onCount, ref offCount);
                return;
            }

            // Safety: make sure the landmarks array exists and has the index we need
            var lms = hand.landmarks01;
            if (lms == null || lms.Length <= landmarkIndex)
            {
                HandleNoHand(side, ref state, ref onCount, ref offCount);
                return;
            }

            // We have a valid hand this frame
            onCount++;
            offCount = 0;

            if (!state.active && onCount >= onFrames)
                state.active = true;

            // Read landmark in normalized image coords (0..1)
            Vector3 uvw = lms[landmarkIndex];
            state.screen01 = new Vector2(uvw.x, 1f - uvw.y); // match your old ScreenToWorldPoint flip

            // Project to swim plane using current gameplay camera (camera can move)
            state.worldPos = SmoothWorldPos(state.worldPos, ProjectToSwimPlane(state.screen01), positionSmooth);

            state.radiusWorld = radiusWorld;
            state.radiusScreen01 = radiusScreen01;

            if (state.active)
                OnEmitterUpdated?.Invoke(side, state);
        }

        void HandleNoHand(
            HandSide side,
            ref LightEmitterState state,
            ref int onCount,
            ref int offCount)
        {
            onCount = 0;
            offCount++;

            if (state.active && offCount >= offFrames)
            {
                state.active = false;
                OnEmitterUpdated?.Invoke(side, state);
            }
        }

        Vector3 ProjectToSwimPlane(Vector2 viewport01)
        {
            if (gameplayCamera == null)
                return new Vector3(0, 0, swimPlaneZ);

            Ray ray = gameplayCamera.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0f));

            // Plane: z = swimPlaneZ
            float denom = ray.direction.z;
            if (Mathf.Abs(denom) < 0.0001f)
            {
                // Camera ray is parallel to plane; fallback
                Vector3 p = ray.origin + ray.direction * 5f;
                p.z = swimPlaneZ;
                return p;
            }

            float t = (swimPlaneZ - ray.origin.z) / denom;
            if (t < 0f) t = 0f;

            Vector3 hit = ray.origin + ray.direction * t;
            hit.z = swimPlaneZ;
            return hit;
        }

        static Vector3 SmoothWorldPos(Vector3 current, Vector3 target, float smooth)
        {
            float a = 1f - Mathf.Exp(-smooth * Time.deltaTime);
            return Vector3.Lerp(current, target, a);
        }
    }
}
