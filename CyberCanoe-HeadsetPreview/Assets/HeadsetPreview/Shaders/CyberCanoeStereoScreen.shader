Shader "Headset Preview/Cyber Canoe Stereo Screen"
{
    Properties
    {
        [NoScaleOffset] _LeftTex("Left Eye Content", 2D) = "black" {}
        [NoScaleOffset] _RightTex("Right Eye Content", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "StereoScreen"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Each source is a mono RenderTexture, even when the headset uses
            // single-pass instancing. These therefore remain ordinary 2D textures.
            TEXTURE2D(_LeftTex);
            SAMPLER(sampler_LeftTex);
            TEXTURE2D(_RightTex);
            SAMPLER(sampler_RightTex);

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = unity_StereoEyeIndex == 0
                    ? SAMPLE_TEXTURE2D(_LeftTex, sampler_LeftTex, input.uv).rgb
                    : SAMPLE_TEXTURE2D(_RightTex, sampler_RightTex, input.uv).rgb;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
