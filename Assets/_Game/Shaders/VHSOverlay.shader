// Effet VHS plein ecran du survivant quand le demon est proche (DemonProximityVHS).
// Un quad colle a la camera relit l'image de la scene (_CameraOpaqueTexture, activee
// dans PC_RPAsset) et la deforme : lignes qui tremblent, bande de tracking qui defile,
// aberration chromatique, couleurs delavees, lignes de balayage, grain, bruit de tete
// en bas de l'image, scintillement, vignette. _Intensity 0 = image normale.
Shader "HouseOfSilence/VHSOverlay"
{
    Properties
    {
        _Intensity ("Intensite", Range(0, 1)) = 0
        _Jitter ("Tremblement des lignes", Range(0, 0.02)) = 0.004
        _Chroma ("Aberration chromatique", Range(0, 0.02)) = 0.005
        _Scanlines ("Lignes de balayage", Range(0, 1)) = 0.18
        _Noise ("Grain", Range(0, 0.5)) = 0.14
        _Desaturate ("Couleurs delavees", Range(0, 1)) = 0.45
        _Tint ("Teinte", Color) = (1, 0.93, 0.85, 1)
        _TrackingSpeed ("Vitesse de la bande de tracking", Float) = 0.35
        _TrackingStrength ("Force de la bande de tracking", Range(0, 0.1)) = 0.03
        _HeadNoise ("Bruit de tete (bas de l'image)", Range(0, 0.1)) = 0.035
        _Vignette ("Vignette", Range(0, 2)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VHSOverlay"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Jitter;
                float _Chroma;
                float _Scanlines;
                float _Noise;
                float _Desaturate;
                half4 _Tint;
                float _TrackingSpeed;
                float _TrackingStrength;
                float _HeadNoise;
                float _Vignette;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Le quad (-0.5..0.5) couvre tout l'ecran, quelle que soit sa position ;
            // seulement pour la camera a laquelle il est colle (a quelques cm devant elle) :
            // une autre camera proche (demon, carte) ne doit jamais recevoir l'effet.
            Varyings vert (Attributes input)
            {
                Varyings output;
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                bool ownCamera = distance(centerWS, _WorldSpaceCameraPos) < 0.5;
                output.positionCS = ownCamera ? float4(input.positionOS.xy * 2.0, 0.5, 1.0) : float4(0, 0, -2, 1);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 original = SampleSceneColor(uv);
                float intensity = saturate(_Intensity);

                if (intensity <= 0.0001)
                {
                    return half4(original, 1.0);
                }

                float t = _Time.y;
                float lines = _ScreenParams.y;
                float row = floor(uv.y * lines * 0.5);

                // Lignes qui tremblent + legere ondulation.
                float jitter = (Hash(float2(row, floor(t * 30.0))) - 0.5) * _Jitter;
                jitter += sin(uv.y * 40.0 + t * 6.0) * 0.0012;

                // Bande de tracking qui descend l'ecran.
                float bandPos = frac(-t * _TrackingSpeed);
                float band = saturate(1.0 - abs(uv.y - bandPos) / 0.045);
                band *= band;
                jitter += band * _TrackingStrength * (Hash(float2(row, t)) - 0.3);

                // Bruit de changement de tete en bas de l'image.
                float bottom = saturate((_HeadNoise - uv.y) / max(_HeadNoise, 1e-4));
                jitter += bottom * (Hash(float2(row, t * 7.0)) - 0.5) * 0.05;

                float2 uvj = float2(uv.x + jitter * intensity, uv.y);

                // Aberration chromatique (plus forte dans la bande).
                float ca = _Chroma * intensity * (1.0 + band * 2.0);
                float3 col;
                col.r = SampleSceneColor(uvj + float2(ca, 0.0)).r;
                col.g = SampleSceneColor(uvj).g;
                col.b = SampleSceneColor(uvj - float2(ca, 0.0)).b;

                // Couleurs delavees et teintees.
                float luminance = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, luminance * _Tint.rgb, _Desaturate);

                // Lignes de balayage.
                float scan = 0.5 + 0.5 * sin(uv.y * lines * 3.14159);
                col *= 1.0 - _Scanlines * scan;

                // Grain, bande et bas de l'image bruites.
                float n = Hash(uv * _ScreenParams.xy + t * 60.0);
                col += (n - 0.5) * _Noise;
                col += band * 0.08 * n;
                col = lerp(col, n.xxx * 0.8, bottom * 0.6);

                // Scintillement et vignette.
                col *= 1.0 + (Hash(float2(floor(t * 24.0), 3.0)) - 0.5) * 0.08;
                float2 d = uv - 0.5;
                col *= saturate(1.0 - dot(d, d) * _Vignette);

                return half4(lerp(original, max(col, 0.0), intensity), 1.0);
            }
            ENDHLSL
        }
    }
}
