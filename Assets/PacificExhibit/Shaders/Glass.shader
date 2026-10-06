Shader "PacificExhibit/Glass"
{
    Properties
    {
        _BaseColor("Glass Tint", Color) = (0.015, 0.19, 0.25, 1)
        [HDR] _GlowColor("Edge Tint", Color) = (0.04, 0.7, 1, 1)
        _Opacity("Glass Opacity", Range(0, 0.4)) = 0.035
        _FresnelPower("Edge Falloff", Range(1, 8)) = 4
        _EdgeGlow("Edge Brightness", Range(0, 8)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" }
        Pass
        {
            Name "Glass"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GlowColor;
                float _Opacity;
                float _FresnelPower;
                float _EdgeGlow;
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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1.0 - saturate(abs(dot(normalize(input.normalWS), viewDirection))), _FresnelPower);
                half3 color = _BaseColor.rgb + _GlowColor.rgb * fresnel * _EdgeGlow;
                half alpha = saturate(_Opacity + fresnel * 0.085);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
