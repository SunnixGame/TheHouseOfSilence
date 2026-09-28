// Halo des yeux du demon : quad toujours face a la camera, additif, sans brouillard.
// Sa taille ne descend jamais sous un angle minimal : les yeux restent visibles de
// loin (quelques pixels) tout en etant caches par le decor (ZTest normal).
// Il s'eteint quand le demon tourne le dos (axe +Z local = direction du regard).
// File Overlay+10 : dessine apres l'effet VHS du survivant, qui ne l'efface pas.
Shader "HouseOfSilence/EyeGlow"
{
    Properties
    {
        [HDR] _Color ("Couleur", Color) = (4, 0.08, 0.04, 1)
        _Size ("Taille de pres (m)", Float) = 0.022
        _MinAngularSize ("Taille minimale (m par metre de distance)", Float) = 0.006
        _Intensity ("Intensite", Range(0, 4)) = 1
        _FadeStart ("Debut d'attenuation (m)", Float) = 90
        _FadeEnd ("Fin d'attenuation (m)", Float) = 160
        _BackFade ("Extinction de dos", Range(0, 1)) = 1
        _Side ("Cote (-1 gauche, +1 droite)", Float) = 0
        _BaseSeparation ("Ecart reel entre les yeux (m)", Float) = 0.054
        _MinSeparation ("Ecart minimal par metre de distance", Float) = 0.006
        _NearFade ("Estompe en dessous de (m)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "EyeGlow"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Size;
                float _MinAngularSize;
                float _Intensity;
                float _FadeStart;
                float _FadeEnd;
                float _BackFade;
                float _Side;
                float _BaseSeparation;
                float _MinSeparation;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fade : TEXCOORD1;
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float distanceToCamera = length(_WorldSpaceCameraPos - centerWS);

                // De loin, les deux yeux s'ecartent un peu pour rester deux points distincts.
                float3 rightWS = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                centerWS += rightWS * _Side * max(0.0, _MinSeparation * distanceToCamera - _BaseSeparation) * 0.5;

                float3 toCamera = _WorldSpaceCameraPos - centerWS;

                // Taille : fixe de pres, proportionnelle a la distance de loin.
                float size = max(_Size, _MinAngularSize * distanceToCamera);

                // Quad oriente vers la camera (axes de la vue).
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = centerWS + (right * input.positionOS.x + up * input.positionOS.y) * size;

                // Attenuation : de dos (le regard du demon est l'axe +Z de l'objet) et tres loin.
                float3 forwardWS = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
                float facing = saturate(dot(forwardWS, toCamera / max(distanceToCamera, 1e-4)) * 2.0 + 0.3);
                float back = lerp(1.0, facing, _BackFade);
                float far = 1.0 - saturate((distanceToCamera - _FadeStart) / max(_FadeEnd - _FadeStart, 1e-3));

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                // Tout pres (jumpscare), le halo s'efface : on voit les vrais yeux du modele.
                float near = saturate((distanceToCamera - _NearFade * 0.5) / max(_NearFade * 0.5, 1e-3));
                output.fade = back * far * near * _Intensity;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r2 = dot(p, p);
                // Coeur brillant + halo serre (reste sur l'oeil, ne deborde pas sur le visage).
                float glow = exp(-r2 * 10.0) + 0.12 * exp(-r2 * 4.0);
                glow *= saturate(1.0 - r2);
                return half4(_Color.rgb, saturate(glow * input.fade * _Color.a));
            }
            ENDHLSL
        }
    }
}
