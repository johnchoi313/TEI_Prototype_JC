using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Unity.Sample;

namespace HandTracking
{
    public class WebcamProcessToRawImage : MonoBehaviour
    {
        [Header("Output")]
        public RenderTexture outputRT;

        [Tooltip("Optional. If null, uses this GameObject's RawImage.")]
        public RawImage targetRawImage;

        [Header("Processing")]
        public Material processMaterial;

        [Tooltip("Property name used by your shader for the input texture.")]
        public string inputTexProperty = "_MainTex";

        void Awake()
        {
            if (targetRawImage == null)
                targetRawImage = GetComponent<RawImage>();
        }

        void OnEnable()
        {
            // Ensure the UI is pointed at the RT immediately
            if (targetRawImage != null && outputRT != null)
                targetRawImage.texture = outputRT;
        }

        void Update()
        {
            var imageSource = ImageSourceProvider.ImageSource;
            if (imageSource == null || !imageSource.isPrepared) return;

            var webcamTex = imageSource.GetCurrentTexture();
            if (webcamTex == null || outputRT == null) return;

            // Feed the shader (optional)
            if (processMaterial != null && !string.IsNullOrEmpty(inputTexProperty))
                processMaterial.SetTexture(inputTexProperty, webcamTex);

            // Blit
            if (processMaterial != null)
                Graphics.Blit(webcamTex, outputRT, processMaterial);
            else
                Graphics.Blit(webcamTex, outputRT);

            // Re-assert the RawImage binding in case something overwrote it
            if (targetRawImage != null && targetRawImage.texture != outputRT)
                targetRawImage.texture = outputRT;
        }
    }
}
