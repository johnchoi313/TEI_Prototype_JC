using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mediapipe.Tasks.Vision.HandLandmarker;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
    public class HandResultPing : MonoBehaviour
    {
        [SerializeField] private HandLandmarkerHubRunner runner;

        private bool _didDump;
        private float _lastLogTime;

        void Awake()
        {
            if (runner == null) runner = FindFirstObjectByType<HandLandmarkerHubRunner>();
            runner.OnResult += OnResult;
        }

        void OnDestroy()
        {
            if (runner != null) runner.OnResult -= OnResult;
        }

        private void OnResult(HandLandmarkerResult result)
        {
            if (result.Equals(default(HandLandmarkerResult))) return;


            // Dump once so we can see the real member names in YOUR version.
            if (!_didDump)
            {
                _didDump = true;

                var t = result.GetType();
                Debug.Log($"[HandHub] HandLandmarkerResult type = {t.FullName}");

                // Properties
                var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                             .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                             .ToArray();
                Debug.Log("[HandHub] Public Properties:\n" + string.Join("\n", props));

                // Fields
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public)
                              .Select(f => $"{f.Name}:{f.FieldType.Name}")
                              .ToArray();
                Debug.Log("[HandHub] Public Fields:\n" + string.Join("\n", fields));
            }

            // Log at most 2x per second
            if (Time.time - _lastLogTime < 0.5f) return;
            _lastLogTime = Time.time;

            // Try common variants (properties OR fields, case-insensitive)
            object lms = GetMemberValue(result, "handLandmarks", "HandLandmarks", "landmarks", "Landmarks");
            object hds = GetMemberValue(result, "handedness", "Handedness", "handednesses", "Handednesses");

            int lmsCount = TryGetCollectionCount(lms);
            int hdsCount = TryGetCollectionCount(hds);

            Debug.Log($"[HandHub] Result received | landmarks lists: {lmsCount} | handedness lists: {hdsCount}");
        }

        private static object GetMemberValue(object obj, params string[] names)
        {
            var t = obj.GetType();

            foreach (var name in names)
            {
                // property
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(obj);

                // field
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        private static int TryGetCollectionCount(object maybeCollection)
        {
            if (maybeCollection == null) return -1;

            // ICollection
            if (maybeCollection is System.Collections.ICollection ic) return ic.Count;

            // Try Count property
            var t = maybeCollection.GetType();
            var countProp = t.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            if (countProp != null && countProp.PropertyType == typeof(int))
            {
                return (int)countProp.GetValue(maybeCollection);
            }

            return -2; // exists but we couldn't count it
        }
    }
}
