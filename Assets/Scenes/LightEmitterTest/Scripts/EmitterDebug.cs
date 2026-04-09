using UnityEngine;

namespace HandTracking
{
    public class EmitterDebug : MonoBehaviour
    {
        public LightEmitterService emitters;
        public bool logOncePerSecond = true;

        float _t;

        void Awake()
        {
            if (emitters == null) emitters = FindFirstObjectByType<LightEmitterService>();
        }

        void Update()
        {
            if (!logOncePerSecond) return;
            _t += Time.deltaTime;
            if (_t < 1f) return;
            _t = 0f;

            var L = emitters.Left;
            var R = emitters.Right;

            Debug.Log($"[Emitters] L active={L.active} screen01={L.screen01}  |  R active={R.active} screen01={R.screen01}");
        }

        void OnDrawGizmos()
        {
            if (emitters == null) return;

            var cam = emitters.gameplayCamera != null ? emitters.gameplayCamera : Camera.main;
            if (cam == null) return;

            DrawOne(cam, emitters.Left);
            DrawOne(cam, emitters.Right);
        }

        void DrawOne(Camera cam, LightEmitterService.LightEmitterState e)
        {
            if (!e.active) return;

            // Draw world point
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(e.worldPos, 0.2f);

            // Draw a line from camera to world point
            Gizmos.DrawLine(cam.transform.position, e.worldPos);
        }
    }
}
