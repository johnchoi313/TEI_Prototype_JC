using UnityEngine;
using UnityEngine.UI;

namespace HandTracking
{
    [RequireComponent(typeof(RawImage))]
    public class RenderTextureToRawImage : MonoBehaviour
    {
        public RenderTexture source;
        RawImage _raw;

        void Awake() => _raw = GetComponent<RawImage>();

        void OnEnable()
        {
            if (source != null) _raw.texture = source;
        }

        void Update()
        {
            if (source != null && _raw.texture != source)
                _raw.texture = source;
        }
    }
}
