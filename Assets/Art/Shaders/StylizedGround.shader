// Terrain surface for the diorama tiles.
//
// The mesh generator bakes the biome palette into vertex colours, so one material and
// one draw setup covers forest, snow, lava and everything else -- unlocking a biome
// repaints the ground without swapping assets.
Shader "Living Diorama/Ground"
{
    Properties
    {
        _CliffColor ("Cliff Colour", Color) = (0.38, 0.35, 0.32, 1)
        _CliffStart ("Cliff Slope Start", Range(0, 1)) = 0.55
        _CliffEnd ("Cliff Slope End", Range(0, 1)) = 0.82

        [Header(Toon Ramp)]
        _Bands ("Shading Bands", Range(2, 6)) = 3
        _RampSmooth ("Band Softness", Range(0.01, 0.5)) = 0.16
        _Wrap ("Light Wrap", Range(0, 1)) = 0.4
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.7
        _ShadowTint ("Shadow Tint", Color) = (0.36, 0.44, 0.62, 1)

        [Header(Surface Detail)]
        _DetailScale ("Detail Noise Scale", Range(0.5, 20)) = 6
        _DetailStrength ("Detail Noise Strength", Range(0, 0.4)) = 0.09
        _MacroScale ("Macro Variation Scale", Range(0.02, 2)) = 0.22
        _MacroStrength ("Macro Variation Strength", Range(0, 0.4)) = 0.12

        [Header(Edges)]
        _RimColor ("Rim Colour", Color) = (1, 0.98, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
        _EdgeDarken ("Tile Edge Darkening", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _CliffColor;
            half  _CliffStart;
            half  _CliffEnd;
            half  _Bands;
            half  _RampSmooth;
            half  _Wrap;
            half  _ShadowStrength;
            half4 _ShadowTint;
            half  _DetailScale;
            half  _DetailStrength;
            half  _MacroScale;
            half  _MacroStrength;
            half4 _RimColor;
            half  _RimStrength;
            half  _EdgeDarken;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "LDToon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 normalWS = normalize(IN.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Biome colour comes in on the vertices; slope decides where rock shows through.
                half3 albedo = IN.color.rgb;
                half slope = 1.0h - saturate(normalWS.y);
                half cliff = smoothstep(_CliffStart, _CliffEnd, slope);
                albedo = lerp(albedo, _CliffColor.rgb, cliff);

                // Two scales of variation break up the flat-shaded look without a texture.
                half macro = LD_Noise21(IN.positionWS.xz * _MacroScale);
                half detail = LD_Noise21(IN.positionWS.xz * _DetailScale);
                albedo *= 1.0h + (macro - 0.5h) * 2.0h * _MacroStrength
                                + (detail - 0.5h) * 2.0h * _DetailStrength;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half lighting = LD_MainLightToon(mainLight, normalWS, _Bands, _RampSmooth,
                                                 _Wrap, _ShadowStrength);

                half3 lit = albedo * mainLight.color * lighting;
                half3 shade = albedo * _ShadowTint.rgb;
                half3 colour = lerp(shade, lit, lighting);
                colour += SampleSH(normalWS) * albedo * 0.5h;

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light extra = GetAdditionalLight(i, IN.positionWS);
                    half e = LD_ToonRamp(saturate(dot(normalWS, extra.direction)), _Bands, _RampSmooth);
                    colour += albedo * extra.color * e * extra.distanceAttenuation * 0.8h;
                }
                #endif

                colour += _RimColor.rgb * LD_Rim(normalWS, viewWS, mainLight.direction, 4.0h) * _RimStrength;

                // UV runs 0..1 across a tile: darken the seams so individual tiles read as
                // separate slabs of world, which sells the "diorama" framing.
                half2 edge = abs(IN.uv - 0.5h) * 2.0h;
                half edgeMask = saturate(max(edge.x, edge.y) * 8.0h - 7.0h);
                colour *= 1.0h - edgeMask * _EdgeDarken;

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

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SA { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct SV { float4 positionCS : SV_POSITION; };

            SV ShadowVert (SA IN)
            {
                SV OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
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
                return OUT;
            }

            half4 ShadowFrag (SV IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DA { float4 positionOS : POSITION; };
            struct DV { float4 positionCS : SV_POSITION; };

            DV DepthVert (DA IN)
            {
                DV OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag (DV IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
