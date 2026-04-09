using System.Collections.Generic;
using UnityEngine;

namespace HandTracking
{
    public class HandCursorManager : MonoBehaviour
    {
        public HandTrackingHub hub;

        [Header("Prefabs")]
        public GameObject leftCursorPrefab;
        public GameObject rightCursorPrefab;

        Dictionary<HandSide, HandCursorInstance> activeCursors = new();

        void Awake()
        {
            if (hub == null)
                hub = FindFirstObjectByType<HandTrackingHub>();
        }

        void Update()
        {
            HandleSide(HandSide.Left, leftCursorPrefab);
            HandleSide(HandSide.Right, rightCursorPrefab);
        }

        void HandleSide(HandSide side, GameObject prefab)
        {
            bool exists = hub.TryGetHand(side, out var hand);

            if (exists)
            {
                if (!activeCursors.ContainsKey(side))
                {
                    var go = Instantiate(prefab);
                    var instance = go.GetComponent<HandCursorInstance>();
                    instance.side = side;
                    activeCursors[side] = instance;
                }

                activeCursors[side].UpdateFromHand(hand);
            }
            else
            {
                if (activeCursors.ContainsKey(side))
                {
                    Destroy(activeCursors[side].gameObject);
                    activeCursors.Remove(side);
                }
            }
        }
    }
}
