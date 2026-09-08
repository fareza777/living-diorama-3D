// Stylised shallow water for rivers and ponds.
//
// Depth-driven shallow/deep tinting plus a shoreline foam band does most of the work;
// the vertex ripple and the banded specular sparkle do the rest. No refraction or
// planar reflection -- both are the wrong price on a mid-range phone.
Shader "Living Diorama/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Colour", Color) = (0.35, 0.75, 0.78, 0.75)
        _DeepColor ("Deep Colour", Color) = (0.07, 0.28, 0.42, 0.95)
        _DepthFade ("Depth Fade Distance", Range(0.05, 4)) = 0.85

        [Header(Shore)]
        _FoamColor ("Foam Colour", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Width", Range(0, 1)) = 0.16
        _FoamNoiseScale ("Foam Noise Scale", Range(1, 40)) = 14
        _FoamSpeed ("Foam Speed", Range(0, 3)) = 0.6

        [Header(Waves)]
        _WaveAmplitude ("Wave Amplitude", Range(0, 0.2)) = 0.022
        _WaveFrequency ("Wave Frequency", Range(0.1, 8)) = 2.4
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 1.1

        [Header(Surface)]
        _RippleScale ("Ripple Scale", Range(1, 40)) = 9
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _SparkleColor ("Sparkle Colour", Color) = (1, 1, 1, 1)
        _SparkleStrength ("Sparkle Strength", Range(0, 2)) = 0.6
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "LDToon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half  _DepthFade;
                half4 _FoamColor;
                half  _FoamDistance;
                half  _FoamNoiseScale;
                half  _FoamSpeed;
                half  _WaveAmplitude;
                half  _WaveFrequency;
                half  _WaveSpeed;
                half  _RippleScale;
                half  _RippleStrength;
                half4 _SparkleColor;
                half  _SparkleStrength;
                half  _FresnelPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
                float2 uv          : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // Two crossing sine trains: enough motion to read as water, cheap enough
                // to run on every vertex of a large plane.
                float t = _Time.y * _WaveSpeed;
                float w1 = sin(positionWS.x * _WaveFrequency + t);
                float w2 = sin(positionWS.z * _WaveFrequency * 0.77 + t * 1.31);
                positionWS.y += (w1 + w2) * 0.5 * _WaveAmplitude;

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);

                // How much water sits between this pixel and whatever is behind it.
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceDepth = IN.screenPos.w;
                float waterDepth = max(0.0, sceneDepth - surfaceDepth);

                half depth01 = saturate(waterDepth / max(0.001h, _DepthFade));
                half4 colour = lerp(_ShallowColor, _DeepColor, depth01);

                // ---- animated surface normal ----------------------------------------
                float t = _Time.y * _WaveSpeed;
                float2 rippleUV = IN.positionWS.xz * _RippleScale;
                half n1 = LD_Noise21(rippleUV + float2(t * 0.13, t * 0.09));
                half n2 = LD_Noise21(rippleUV * 1.9 - float2(t * 0.07, t * 0.11));

                float3 normalWS = normalize(IN.normalWS + float3(n1 - 0.5h, 0, n2 - 0.5h) * _RippleStrength);
                float3 viewWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight();

                // ---- sparkle ---------------------------------------------------------
                float3 halfway = normalize(mainLight.direction + viewWS);
                half spec = pow(saturate(dot(normalWS, halfway)), 64.0h);
                // Hard cut so highlights read as discrete glints rather than a smear.
                spec = step(0.55h, spec);
                colour.rgb += _SparkleColor.rgb * spec * _SparkleStrength * mainLight.color;

                // ---- fresnel ---------------------------------------------------------
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewWS)), _FresnelPower);
                colour.rgb += mainLight.color * fresnel * 0.18h;
                colour.a = saturate(colour.a + fresnel * 0.25h);

                // ---- shoreline foam --------------------------------------------------
                half shore = 1.0h - saturate(waterDepth / max(0.001h, _FoamDistance));
                if (shore > 0.001h)
                {
                    half foamNoise = LD_Noise21(IN.positionWS.xz * _FoamNoiseScale
                                                + float2(t * _FoamSpeed, -t * _FoamSpeed * 0.7));
                    // Noise cutting into the band gives the foam a lacy edge instead of a
                    // clean contour line.
                    half foam = step(1.0h - shore * shore, foamNoise * 0.6h + 0.4h);
                    colour.rgb = lerp(colour.rgb, _FoamColor.rgb, foam * shore);
                    colour.a = saturate(colour.a + foam * shore * 0.5h);
                }

                colour.rgb = MixFog(colour.rgb, IN.fogFactor);
                return colour;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
