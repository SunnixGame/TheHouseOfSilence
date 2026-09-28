// Pouvoir "Vision" du demon, passe 2/2 : coque gonflee le long des normales,
// additive et visible a travers les murs, dessinee seulement la ou RevealMask
// n'a pas marque le stencil -> un contour lumineux autour de la silhouette.
// L'epaisseur grandit avec la distance pour rester lisible de loin.
Shader "HouseOfSilence/RevealOutline"
{
    Properties
    {
        [HDR] _Color ("Contour", Color) = (2.5, 0.25, 0.1, 1)
        _Width ("Epaisseur (m)", Float) = 0.025
        _WidthPerMeter ("Epaisseur ajoutee par metre", Float) = 0.004
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+101" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "RevealOutline"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha One

            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _WidthPerMeter;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                float distanceToCamera = distance(positionWS, _WorldSpaceCameraPos);
                positionWS += normalWS * (_Width + _WidthPerMeter * distanceToCamera);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
