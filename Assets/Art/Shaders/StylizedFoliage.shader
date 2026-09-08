// Grass, bushes and tree canopies. Wind is driven from world position so the whole
// diorama sways as one gust, and translucency lets the sun glow through leaves.
Shader "Living Diorama/Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)
        _TipColor ("Tip Colour", Color) = (0.72, 0.88, 0.42, 1)
        _TipHeight ("Tip Blend Height", Range(0.01, 4)) = 0.6
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.4

        [Header(Wind)]
        _WindInfluence ("Wind Influence", Range(0, 1)) = 1
        _WindStrength ("Wind Strength", Range(0, 1)) = 0.13
        _WindSpeed ("Wind Speed", Range(0, 4)) = 1.1
        _WindDirection ("Wind Direction", Vector) = (1, 0, 0.35, 0)

        [Header(Toon Ramp)]
        _Bands ("Shading Bands", Range(2, 6)) = 3
        _RampSmooth ("Band Softness", Range(0.01, 0.5)) = 0.18
        _Wrap ("Light Wrap", Range(0, 1)) = 0.45
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
        _ShadowTint ("Shadow Tint", Color) = (0.32, 0.44, 0.40, 1)

        [Header(Translucency)]
        _TranslucencyColor ("Translucency Colour", Color) = (0.62, 0.92, 0.34, 1)
        _TranslucencyStrength ("Translucency Strength", Range(0, 2)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _TipColor;
            half  _TipHeight;
            half  _Cutoff;
            half  _WindInfluence;
            half  _WindStrength;
            half  _WindSpeed;
            half4 _WindDirection;
            half  _Bands;
            half  _RampSmooth;
            half  _Wrap;
            half  _ShadowStrength;
            half4 _ShadowTint;
            half4 _TranslucencyColor;
            half  _TranslucencyStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "LDToon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  heightOS   : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = LD_Wind(positionWS, IN.positionOS.xyz / max(0.001h, _TipHeight),
                                     _WindInfluence, _WindStrength, _WindSpeed, _WindDirection.xyz);

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.heightOS = IN.positionOS.y;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(sampled.a - _Cutoff);

                half3 albedo = sampled.rgb * _BaseColor.rgb;

                // Lighter towards the tips, the way real foliage catches more light higher up.
                half tip = saturate(IN.heightOS / max(0.001h, _TipHeight));
                albedo = lerp(albedo, _TipColor.rgb * sampled.rgb, tip * 0.65h);

                // Two-sided geometry: flip the normal for back faces so both sides light.
                float3 normalWS = normalize(IN.normalWS) * sign(facing);
                float3 viewWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half lighting = LD_MainLightToon(mainLight, normalWS, _Bands, _RampSmooth,
                                                 _Wrap, _ShadowStrength);

                half3 lit = albedo * mainLight.color * lighting;
                half3 shade = albedo * _ShadowTint.rgb;
                half3 colour = lerp(shade, lit, lighting);
                colour += SampleSH(normalWS) * albedo * 0.5h;

                // Backlight bleeding through the leaf.
                half through = pow(saturate(dot(viewWS, -mainLight.direction)), 3.0h);
                colour += _TranslucencyColor.rgb * albedo * through * _TranslucencyStrength
                          * mainLight.color * mainLight.shadowAttenuation;

                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "LDToon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float3 _LightDirection;
            float3 _LightPosition;

            struct SA
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct SV
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            SV ShadowVert (SA IN)
            {
                SV OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS = LD_Wind(positionWS, IN.positionOS.xyz / max(0.001h, _TipHeight),
                                     _WindInfluence, _WindStrength, _WindSpeed, _WindDirection.xyz);

                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 ShadowFrag (SV IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
