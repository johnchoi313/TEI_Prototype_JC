using UnityEngine;

namespace HandTracking
{
    [RequireComponent(typeof(Renderer))]
    public class HandCursorFollower : MonoBehaviour
    {
        public HandTrackingHub hub;
        public HandSide side;

        public int landmarkIndex = 8; // index tip
        public Camera worldCamera;
        public float depth = 2f;
        public float smooth = 20f;

        public Color openColor = Color.white;
        public Color fistColor = Color.red;

        Renderer _renderer;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            if (hub == null)
                hub = FindFirstObjectByType<HandTrackingHub>();

            if (worldCamera == null)
                worldCamera = Camera.main;

            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (!hub.TryGetHand(side, out var hand))
                return;

            Vector3 uv = hand.landmarks01[landmarkIndex];

            Vector3 screen = new Vector3(
                uv.x * Screen.width,
                (1f - uv.y) * Screen.height,
                depth
            );

            Vector3 world = worldCamera.ScreenToWorldPoint(screen);

            transform.position = Vector3.Lerp(
                transform.position,
                world,
                1f - Mathf.Exp(-smooth * Time.deltaTime)
            );

            bool fist = HandTrackingHub.IsFist(hand);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", fist ? fistColor : openColor);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
