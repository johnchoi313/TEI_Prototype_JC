using UnityEngine;
using UnityEngine.UI;

namespace HandTracking
{
    public class FOVManager : MonoBehaviour
    {
        [Header("Sources")]
        public LightEmitterService emitters;

        [Header("Render Textures")]
        public RenderTexture sceneRT; // camera output
        public RenderTexture maskRT;  // generated mask
        public RenderTexture finalRT; // final composite

        [Header("Materials (Shader Graph)")]
        public Material stampMaterial;     // draws a circle into maskRT!
        public Material compositeMaterial; // combines sceneRT + maskRT -> finalRT

        [Header("Output UI")]
        public RawImage outputImage; // shows finalRT
        public Color darkColor = Color.black;

        // Shader property names (must match Shader Graph property references)
        static readonly int P_Center = Shader.PropertyToID("_Center01");
        static readonly int P_Radius = Shader.PropertyToID("_Radius01");
        static readonly int P_Active = Shader.PropertyToID("_Active");
        static readonly int P_DarkColor = Shader.PropertyToID("_DarkColor");
        static readonly int P_SceneTex = Shader.PropertyToID("_SceneTex");
        static readonly int P_MaskTex = Shader.PropertyToID("_MaskTex");

        void Awake()
        {
            if (emitters == null) emitters = FindFirstObjectByType<LightEmitterService>();
            if (outputImage == null) outputImage = GetComponentInChildren<RawImage>();

            if (outputImage != null && finalRT != null)
                outputImage.texture = finalRT;
        }

        void LateUpdate()
        {
            //if (sceneRT == null || maskRT == null || finalRT == null) return;
            //if (stampMaterial == null || compositeMaterial == null) return;
            //if (emitters == null) return;

            ClearRT(maskRT);

            stampMaterial.SetVector("_Center01", new Vector4(0.5f, 0.5f, 0, 0));
            stampMaterial.SetFloat("_Radius01", 0.25f);
            stampMaterial.SetFloat("_Active", 1f);

            Graphics.Blit(null, maskRT, stampMaterial);
            Graphics.Blit(sceneRT, finalRT);


            // 4) Ensure UI still shows finalRT

            outputImage.texture = finalRT;
        }

        void StampEmitter(LightEmitterService.LightEmitterState e)
        {
            //if (!e.active) return;

            stampMaterial.SetVector(P_Center, new Vector4(e.screen01.x, e.screen01.y, 0, 0));
            stampMaterial.SetFloat(P_Radius, e.radiusScreen01);
            stampMaterial.SetFloat(P_Active, 1f);

            // IMPORTANT: we want to ADD white into maskRT, not overwrite it.
            // We'll do this by stamping using a shader that outputs white inside the circle
            // and black outside, and we use additive blending in the material.
            Graphics.Blit(null, maskRT, stampMaterial);
        }

        static void ClearRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }
    }
}
