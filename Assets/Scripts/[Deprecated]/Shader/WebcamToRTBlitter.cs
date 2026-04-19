using UnityEngine;
using Mediapipe.Unity.Sample;

namespace HandTracking
{
    public class WebcamToRTBlitter : MonoBehaviour
    {
        public RenderTexture outputRT;
        public Material processMaterial;

        void Update()
        {
            var imageSource = ImageSourceProvider.ImageSource;
            if (imageSource == null || !imageSource.isPrepared) return;

            var webcamTex = imageSource.GetCurrentTexture();
            if (webcamTex == null || outputRT == null) return;

            if (processMaterial != null)
                processMaterial.SetTexture("_MainTex", webcamTex);

            if (processMaterial != null)
                Graphics.Blit(webcamTex, outputRT, processMaterial);
            else
                Graphics.Blit(webcamTex, outputRT);
        }
    }
}
