// Stylised toon lighting for every creature in the diorama.
//
// Generated meshes arrive with plain PBR materials that look flat and slightly grubby
// under mobile lighting. This shader re-reads their albedo through a banded ramp with
// wrapped subsurface, a rim light and a soft specular glint, which is what pulls a set
// of independently generated models into one coherent art direction.
Shader "Living Diorama/Creature"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)

        [Header(Toon Ramp)]
        _Bands ("Shading Bands", Range(2, 6)) = 3
        _RampSmooth ("Band Softness", Range(0.01, 0.5)) = 0.12
        _ShadowTint ("Shadow Tint", Color) = (0.42, 0.47, 0.62, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.62

        [Header(Subsurface)]
        _Wrap ("Light Wrap", Range(0, 1)) = 0.35
        _SSSColor ("Subsurface Colour", Color) = (1.0, 0.55, 0.45, 1)
        _SSSStrength ("Subsurface Strength", Range(0, 1)) = 0.25

        [Header(Rim)]
        _RimColor ("Rim Colour", Color) = (1, 0.97, 0.88, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.2
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.75

        [Header(Specular)]
        _SpecColor2 ("Specular Colour", Color) = (1,1,1,1)
        _Gloss ("Gloss", Range(1, 128)) = 24
        _SpecStrength ("Specular Strength", Range(0, 1)) = 0.18

        [Header(Grounding)]
        _OcclusionHeight ("Contact Darkening Height", Range(0, 1)) = 0.22
        _OcclusionStrength ("Contact Darkening", Range(0, 1)) = 0.28

        [Header(Spawn Dissolve)]
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _DissolveColor ("Dissolve Edge", Color) = (0.5, 0.9, 1, 1)
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
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Bands;
            half   _RampSmooth;
            half4  _ShadowTint;
            half   _ShadowStrength;
            half   _Wrap;
            half4  _SSSColor;
            half   _SSSStrength;
            half4  _RimColor;
            half   _RimPower;
            half   _RimStrength;
            half4  _SpecColor2;
            half   _Gloss;
            half   _SpecStrength;
            half   _OcclusionHeight;
            half   _OcclusionStrength;
            half   _Dissolve;
            half4  _DissolveColor;
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float  heightOS    : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
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
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.heightOS = IN.positionOS.y;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            // Quantise a 0..1 lighting term into soft bands. Softness is scaled by the band
            // count so raising the band count never turns the ramp back into a gradient.
            half ToonRamp (half ndl)
            {
                half bands = max(2.0h, _Bands);
                half scaled = saturate(ndl) * bands;
                half index = floor(scaled);
                half frac_ = scaled - index;
                half soft = _RampSmooth * bands * 0.5h;
                half blended = index + smoothstep(0.5h - soft, 0.5h + soft, frac_);
                return saturate(blended / bands);
            }

            // Cheap value hash, used only for the spawn dissolve.
            half Hash (float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return (half)frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // ---- spawn dissolve --------------------------------------------------
                if (_Dissolve > 0.001h)
                {
                    half noise = Hash(floor(IN.positionWS * 42.0h));
                    half cut = noise - (1.0h - _Dissolve);
                    clip(cut);
                    // Glow along the dissolving edge so creatures materialise rather than pop.
                    half edge = 1.0h - saturate(cut * 12.0h);
                    albedo.rgb = lerp(albedo.rgb, _DissolveColor.rgb * 2.0h, edge);
                }

                float3 normalWS = normalize(IN.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // ---- wrapped diffuse -------------------------------------------------
                half ndl = dot(normalWS, mainLight.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                half ramp = ToonRamp(wrapped);

                // Shadows are banded too, otherwise the cast shadow edge fights the ramp.
                half shadow = lerp(1.0h, ToonRamp(mainLight.shadowAttenuation), _ShadowStrength);
                half lighting = ramp * shadow;

                half3 lit = albedo.rgb * mainLight.color * lighting;
                half3 shade = albedo.rgb * _ShadowTint.rgb;
                half3 colour = lerp(shade, lit, lighting);

                // ---- ambient ---------------------------------------------------------
                half3 ambient = SampleSH(normalWS) * albedo.rgb;
                colour += ambient * 0.55h;

                // ---- subsurface ------------------------------------------------------
                // Light bleeding through thin parts: strongest where the surface faces away
                // from the light but towards the viewer. Sells ears, wings and jelly bodies.
                half back = saturate(dot(-normalWS, mainLight.direction));
                half through = pow(saturate(dot(viewWS, -mainLight.direction)), 2.0h);
                colour += _SSSColor.rgb * albedo.rgb * back * through * _SSSStrength * mainLight.color;

                // ---- specular --------------------------------------------------------
                float3 halfway = normalize(mainLight.direction + viewWS);
                half spec = pow(saturate(dot(normalWS, halfway)), _Gloss);
                spec = smoothstep(0.35h, 0.5h, spec);   // toon-hard highlight
                colour += _SpecColor2.rgb * spec * _SpecStrength * lighting * mainLight.color;

                // ---- rim -------------------------------------------------------------
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewWS)), _RimPower);
                // Only rim the lit side, so the effect reads as light rather than an outline.
                half rimMask = saturate(ndl * 0.5h + 0.5h);
                colour += _RimColor.rgb * fresnel * rimMask * _RimStrength;

                // ---- additional lights ----------------------------------------------
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light extra = GetAdditionalLight(i, IN.positionWS);
                    half endl = ToonRamp(saturate(dot(normalWS, extra.direction)));
                    colour += albedo.rgb * extra.color * endl * extra.distanceAttenuation * 0.7h;
                }
                #endif

                // ---- contact darkening ----------------------------------------------
                // A cheap stand-in for an occlusion map: darken near the model's base so
                // creatures feel planted instead of floating.
                half contact = 1.0h - saturate(IN.heightOS / max(0.001h, _OcclusionHeight));
                colour *= 1.0h - contact * _OcclusionStrength;

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
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // Hand-rolled rather than including URP's ShadowCasterPass.hlsl, which expects
            // the full Lit material property set (_Cutoff, _Surface and friends).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert (ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirWS));

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag (ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
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
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert (DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag (DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
