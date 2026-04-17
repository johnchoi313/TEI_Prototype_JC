Shader "UI/SelectiveInvert"
{
    Properties
    {
        _MainTex ("UI Mask (White = Invert)", 2D) = "white" {}
        _Threshold ("White Sensitivity", Range(0, 1)) = 0.5
        _Tolerance ("Range Softness", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        
        // This 'grabs' the screen into a texture named _BackgroundTexture
        GrabPass { "_BackgroundTexture" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _BackgroundTexture;
            float _Threshold;
            float _Tolerance;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // Calculates the screen position for the GrabPass texture
                o.grabPos = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                // 1. Sample the background and the UI mask
                fixed4 bgColor = tex2Dproj(_BackgroundTexture, i.grabPos);
                fixed4 mask = tex2D(_MainTex, i.uv);

                // 2. Check how "White" the background is (Luminance)
                float luminance = dot(bgColor.rgb, float3(0.299, 0.587, 0.114));
                
                // 3. Create a mask based on the range. 
                // If luminance is > Threshold, this value approaches 1.
                float selectRange = smoothstep(_Threshold - _Tolerance, _Threshold + _Tolerance, luminance);

                // 4. Invert the color (1 - color)
                float3 invertedColor = 1.0 - bgColor.rgb;

                // 5. Combine: Only invert if (Mask is white) AND (Background is in range)
                float3 finalRGB = lerp(bgColor.rgb, invertedColor, mask.a * selectRange);

                return fixed4(finalRGB, 1.0);
            }
            ENDCG
        }
    }
}
