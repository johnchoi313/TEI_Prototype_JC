using UnityEngine;

namespace HandTracking
{
    [RequireComponent(typeof(Renderer))]
    public class HandCursorInstance : MonoBehaviour
    {
        public HandSide side;

        public int landmarkIndex = 8; // index tip
        public Camera worldCamera;
        public float depth = 2f;
        public float smooth = 20f;

        [Header("Scaling")]
        public float minScale = 0.05f;
        public float maxScale = 0.25f;
        public float scaleSensitivity = 1.5f;

        public Color openColor = Color.white;
        public Color fistColor = Color.red;

        Renderer _renderer;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        public void UpdateFromHand(in HandFrame hand)
        {
            Vector3 uv = hand.landmarks01[landmarkIndex];

            // --- Position ---
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

            // --- Fist color ---
            bool fist = HandTrackingHub.IsFist(hand);

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", fist ? fistColor : openColor);
            _renderer.SetPropertyBlock(_mpb);

            // --- Depth-based scale ---
            float handSize = EstimateHandSize(hand);

            float scaled = Mathf.Lerp(minScale, maxScale, handSize * scaleSensitivity);
            transform.localScale = Vector3.one * scaled;
        }

        float EstimateHandSize(in HandFrame hand)
        {
            // distance between wrist and middle fingertip
            Vector2 wrist = hand.landmarks01[0];
            Vector2 tip = hand.landmarks01[12];

            return Vector2.Distance(wrist, tip);
        }
    }
}
