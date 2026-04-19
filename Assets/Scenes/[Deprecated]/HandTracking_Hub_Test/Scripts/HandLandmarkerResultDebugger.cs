using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mediapipe.Tasks.Vision.HandLandmarker;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
    public class HandLandmarkerResultDebugger : MonoBehaviour
    {
        [SerializeField] private HandLandmarkerHubRunner runner;

        [Header("Logging")]
        public bool dumpResultTypeOnce = true;
        public bool dumpResultMembersOnce = true;
        public bool dumpNestedTypesOnce = true;
        public float logIntervalSeconds = 0.5f;

        private bool _didDumpType;
        private bool _didDumpMembers;
        private bool _didDumpNested;
        private float _lastLogTime;

        // cache resolved getters so we can print counts even if names differ
        private Func<object, object> _getHandLandmarks;
        private Func<object, object> _getHandedness;

        void Awake()
        {
            if (runner == null) runner = FindFirstObjectByType<HandLandmarkerHubRunner>();
            if (runner == null)
            {
                Debug.LogError("[HandDebug] Could not find HandLandmarkerHubRunner in scene.");
                enabled = false;
                return;
            }

            runner.OnResult += OnResult;
        }

        void OnDestroy()
        {
            if (runner != null) runner.OnResult -= OnResult;
        }

        private void OnResult(HandLandmarkerResult result)
        {
            // HandLandmarkerResult is a struct in your build, so no null checks.
            if (result.Equals(default(HandLandmarkerResult))) return;

            object boxed = result; // box struct to object for reflection

            if (dumpResultTypeOnce && !_didDumpType)
            {
                _didDumpType = true;
                Debug.Log($"[HandDebug] Result type: {boxed.GetType().FullName}");
            }

            if (dumpResultMembersOnce && !_didDumpMembers)
            {
                _didDumpMembers = true;
                DumpPublicMembers(boxed.GetType(), "[HandDebug] HandLandmarkerResult members");
            }

            // Resolve landmark/handedness getters once (try common names)
            if (_getHandLandmarks == null || _getHandedness == null)
            {
                _getHandLandmarks = BuildGetter(boxed.GetType(),
                    "handLandmarks", "HandLandmarks", "landmarks", "Landmarks");
                _getHandedness = BuildGetter(boxed.GetType(),
                    "handedness", "Handedness", "handednesses", "Handednesses");

                Debug.Log($"[HandDebug] Resolved getters: Landmarks={(_getHandLandmarks != null)} Handedness={(_getHandedness != null)}");
            }

            // Throttle periodic logs
            if (Time.time - _lastLogTime < logIntervalSeconds) return;
            _lastLogTime = Time.time;

            object lmObj = _getHandLandmarks != null ? _getHandLandmarks(boxed) : null;
            object hdObj = _getHandedness != null ? _getHandedness(boxed) : null;

            int lmCount = GetCount(lmObj);
            int hdCount = GetCount(hdObj);

            Debug.Log($"[HandDebug] counts | landmarksList={lmCount} handednessList={hdCount}");

            if (dumpNestedTypesOnce && !_didDumpNested && (lmObj != null || hdObj != null))
            {
                _didDumpNested = true;

                if (lmObj != null)
                {
                    var lmType = lmObj.GetType();
                    Debug.Log($"[HandDebug] Landmarks container type: {lmType.FullName}");
                    DumpPublicMembers(lmType, "[HandDebug] Landmarks container members");

                    // Try to inspect first element type if enumerable
                    var first = GetFirstElement(lmObj);
                    if (first != null)
                    {
                        Debug.Log($"[HandDebug] First landmark element type: {first.GetType().FullName}");
                        DumpPublicMembers(first.GetType(), "[HandDebug] First landmark element members");
                    }
                }

                if (hdObj != null)
                {
                    var hdType = hdObj.GetType();
                    Debug.Log($"[HandDebug] Handedness container type: {hdType.FullName}");
                    DumpPublicMembers(hdType, "[HandDebug] Handedness container members");

                    var first = GetFirstElement(hdObj);
                    if (first != null)
                    {
                        Debug.Log($"[HandDebug] First handedness element type: {first.GetType().FullName}");
                        DumpPublicMembers(first.GetType(), "[HandDebug] First handedness element members");
                    }
                }
            }
        }

        // ---------- Helpers ----------

        private static Func<object, object> BuildGetter(Type t, params string[] names)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p != null) return (obj) => p.GetValue(obj);

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (f != null) return (obj) => f.GetValue(obj);
            }
            return null;
        }

        private static int GetCount(object maybeList)
        {
            if (maybeList == null) return -1;

            if (maybeList is System.Collections.ICollection ic) return ic.Count;

            var t = maybeList.GetType();
            var countProp = t.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            if (countProp != null && countProp.PropertyType == typeof(int))
                return (int)countProp.GetValue(maybeList);

            return -2; // exists but no Count
        }

        private static object GetFirstElement(object enumerable)
        {
            if (enumerable == null) return null;

            if (enumerable is IEnumerable e)
            {
                foreach (var item in e)
                    return item;
            }
            return null;
        }

        private static void DumpPublicMembers(Type t, string header)
        {
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Select(p => $"P {p.Name} : {p.PropertyType.Name}")
                         .OrderBy(s => s)
                         .ToArray();

            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public)
                          .Select(f => $"F {f.Name} : {f.FieldType.Name}")
                          .OrderBy(s => s)
                          .ToArray();

            Debug.Log(header + "\n" + string.Join("\n", props.Concat(fields)));
        }
    }
}
