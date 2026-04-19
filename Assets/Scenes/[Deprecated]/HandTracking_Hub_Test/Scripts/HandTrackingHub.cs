using System.Collections.Generic;
using UnityEngine;
using Mediapipe.Tasks.Vision.HandLandmarker;
using mptcc = Mediapipe.Tasks.Components.Containers;

namespace HandTracking
{
    public enum HandSide { Left, Right }

    public struct HandFrame
    {
        public HandSide side;
        public Vector3[] landmarks01; // 21 landmarks
    }

    public class HandTrackingHub : MonoBehaviour
    {
        public Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerHubRunner runner;

        private readonly Dictionary<HandSide, HandFrame> _hands = new();

        void Awake()
        {
            if (runner == null)
                runner = FindFirstObjectByType<Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerHubRunner>();

            runner.OnResult += HandleResult;
        }

        void OnDestroy()
        {
            if (runner != null)
                runner.OnResult -= HandleResult;
        }

        void HandleResult(HandLandmarkerResult result)
        {
            _hands.Clear();

            // Guard default struct
            if (result.Equals(default(HandLandmarkerResult))) return;

            var landmarksList = result.handLandmarks;
            var handednessList = result.handedness;

            int count = Mathf.Min(landmarksList.Count, handednessList.Count);

            for (int i = 0; i < count; i++)
            {
                var normalizedLandmarks = landmarksList[i];
                var classifications = handednessList[i];

                string sideName = classifications.categories[0].categoryName;
                var side = sideName == "Left" ? HandSide.Left : HandSide.Right;

                Vector3[] arr = new Vector3[21];

                for (int j = 0; j < 21; j++)
                {
                    var lm = normalizedLandmarks.landmarks[j];
                    arr[j] = new Vector3(lm.x, lm.y, lm.z);
                }

                _hands[side] = new HandFrame
                {
                    side = side,
                    landmarks01 = arr
                };
            }
        }

        public bool TryGetHand(HandSide side, out HandFrame hand)
            => _hands.TryGetValue(side, out hand);

        public static bool IsFist(in HandFrame hand)
        {
            Vector2 wrist = (Vector2)hand.landmarks01[0];

            int[,] fingers = { { 6, 8 }, { 10, 12 }, { 14, 16 }, { 18, 20 } };
            int curled = 0;

            for (int i = 0; i < 4; i++)
            {
                int pip = fingers[i, 0];
                int tip = fingers[i, 1];

                float pipDist = Vector2.Distance((Vector2)hand.landmarks01[pip], wrist);
                float tipDist = Vector2.Distance((Vector2)hand.landmarks01[tip], wrist);

                if (tipDist < pipDist)
                    curled++;
            }

            return curled >= 3;
        }
    }
}
