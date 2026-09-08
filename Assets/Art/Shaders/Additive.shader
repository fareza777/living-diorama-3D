// Unlit additive glow, used for the chest seam, the reveal light shaft and the
// unboxing particles. Additive rather than alpha-blended so overlapping layers build
// up into a hot core, which is what makes a burst feel like light instead of paint.
Shader "Living Diorama/Additive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0, 8)) = 1
        [Toggle] _UseVertexColor ("Use Vertex Colour", Float) = 1
        _SoftFade ("Vertical Soft Fade", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Additive"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Intensity;
                half _UseVertexColor;
                half _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float  heightOS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.heightOS = IN.positionOS.y;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half3 tint = _Color.rgb;
                if (_UseVertexColor > 0.5h) tint *= IN.color.rgb;

                half3 colour = tex.rgb * tint * _Intensity * _Color.a;

                // Optional taper up the object's local Y, so a light shaft dissolves into
                // the air instead of ending on a hard edge.
                if (_SoftFade > 0.001h)
                {
                    colour *= saturate(1.0h - IN.heightOS * _SoftFade);
                }

                return half4(colour * tex.a, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
