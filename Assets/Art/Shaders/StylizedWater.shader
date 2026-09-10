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
        _DepthFade ("Depth Fade Distance", Range(0.05, 4)) = 0.32

        [Header(Shore)]
        _FoamColor ("Foam Colour", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Width", Range(0, 1)) = 0.055
        _FoamNoiseScale ("Foam Noise Scale", Range(1, 40)) = 6
        _FoamSpeed ("Foam Speed", Range(0, 3)) = 0.6

        [Header(Waves)]
        _WaveAmplitude ("Wave Amplitude", Range(0, 0.2)) = 0.022
        _WaveFrequency ("Wave Frequency", Range(0.1, 8)) = 2.4
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 1.1

        [Header(Surface)]
        _RippleScale ("Ripple Scale", Range(1, 40)) = 9
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.14
        _SparkleColor ("Sparkle Colour", Color) = (1, 1, 1, 1)
        _SparkleStrength ("Sparkle Strength", Range(0, 2)) = 0.5
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
        _SkyColor ("Sky Reflection", Color) = (0.62, 0.78, 0.95, 1)
        _SkyStrength ("Sky Reflection Strength", Range(0, 1)) = 0.45
        _Refraction ("Refraction", Range(0, 0.12)) = 0.018
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // Two layers of noise, scrolling against each other. Returned as a height so
            // the surface normal can be taken as its slope: sampling noise straight into
            // the x and z of a normal, as this used to, tilts each axis independently and
            // gives a fizz rather than a surface.
            half WaveHeight(float2 uv, float t)
            {
                half a = LD_Noise21(uv + float2(t * 0.11, t * 0.07));
                half b = LD_Noise21(uv * 2.1 - float2(t * 0.06, t * 0.13));
                return a + b * 0.5h;
            }

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
                half4 _SkyColor;
                half  _SkyStrength;
                half  _Refraction;
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
                float waterDepth = sceneDepth - surfaceDepth;

                // The depth texture is not always there: an offscreen render, a renderer
                // configured without a depth pass, or a device that quietly dropped it.
                // Without a guard the sample reads as zero depth, every pixel counts as
                // shoreline, and the whole river turns into a sheet of white foam. Falling
                // back to a plausible mid-depth keeps it looking like water instead.
                if (!(waterDepth > 0.0) || waterDepth > 1000.0)
                {
                    waterDepth = _DepthFade;
                }

                half depth01 = saturate(waterDepth / max(0.001h, _DepthFade));

                // ---- animated surface normal ----------------------------------------
                float t = _Time.y * _WaveSpeed;
                float2 rippleUV = IN.positionWS.xz * _RippleScale;

                // Slope of the wave field, by finite difference. The offset is in noise
                // units, so it scales with the ripples rather than with the world.
                //
                // The offset has to be a fraction of a noise cell, not most of one, and
                // the slope has to be held down afterwards. Left unbounded it reaches
                // several units against a vertical of one, which tips the normal almost
                // flat from one pixel to the next -- and a normal that noisy turns the
                // specular test on and off per pixel, dusting the whole river with white
                // speckles that read as static rather than as water.
                const float e = 0.12;
                half h0 = WaveHeight(rippleUV, t);
                half hx = WaveHeight(rippleUV + float2(e, 0), t);
                half hz = WaveHeight(rippleUV + float2(0, e), t);
                float2 slope = clamp(float2(hx - h0, hz - h0) / e, -3.0, 3.0);

                float3 normalWS = normalize(float3(-slope.x * _RippleStrength,
                                                   1.0,
                                                  -slope.y * _RippleStrength));
                float3 viewWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // ---- what is underneath ---------------------------------------------
                //
                // Bending the view through the surface is the single thing that separates
                // water from a tinted pane of glass. The offset is scaled by depth so the
                // shoreline, where the bed is inches away, stays put -- dragging it would
                // pull dry ground in over the water and read as a tear.
                float2 bend = normalWS.xz * _Refraction * saturate(waterDepth);

                // Only refract what is actually below the surface, or the bank next to
                // the river gets dragged out over the water. Fading the offset out rather
                // than switching it off matters: a hard test turns a noisy surface normal
                // into hard-edged patches of dry ground scattered across the river, which
                // is exactly how it looked.
                float behind = LinearEyeDepth(SampleSceneDepth(screenUV + bend), _ZBufferParams);
                half valid = saturate((behind - surfaceDepth) * 14.0h);

                half3 bed = SampleSceneColor(screenUV + bend * valid);

                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                half opacity = lerp(_ShallowColor.a, _DeepColor.a, depth01);
                half4 colour = half4(lerp(bed, water, opacity), 1.0h);

                Light mainLight = GetMainLight();

                // ---- sparkle ---------------------------------------------------------
                float3 halfway = normalize(mainLight.direction + viewWS);
                half spec = pow(saturate(dot(normalWS, halfway)), 48.0h);
                // Shaped rather than cut: a hard step on a rippling normal turns every
                // glint into an aliased dot that crawls as the camera moves.
                spec = smoothstep(0.35h, 0.8h, spec);
                colour.rgb += _SparkleColor.rgb * spec * _SparkleStrength * mainLight.color;

                // ---- the sky in the surface ------------------------------------------
                //
                // A grazing view of water shows the sky, not a white sheen. Tinting the
                // fresnel with the sky colour is what stops it looking like plastic.
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewWS)), _FresnelPower);
                colour.rgb = lerp(colour.rgb, _SkyColor.rgb, saturate(fresnel * _SkyStrength));

                // ---- shoreline foam --------------------------------------------------
                half shore = 1.0h - saturate(waterDepth / max(0.001h, _FoamDistance));
                if (shore > 0.001h)
                {
                    half foamNoise = LD_Noise21(IN.positionWS.xz * _FoamNoiseScale
                                                + float2(t * _FoamSpeed, -t * _FoamSpeed * 0.7));
                    // Noise cutting into the band gives the foam a lacy edge instead of a
                    // clean contour line.
                    // Softened rather than cut. A hard step scatters the whole river with
                    // detached white blobs the moment the water is shallow throughout,
                    // which is what a stream in a diorama is.
                    half edge = 1.0h - shore * shore;
                    half foam = smoothstep(edge - 0.09h, edge + 0.09h, foamNoise * 0.6h + 0.4h);
                    colour.rgb = lerp(colour.rgb, _FoamColor.rgb, foam * shore * shore);
                }

                colour.rgb = MixFog(colour.rgb, IN.fogFactor);
                return colour;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
