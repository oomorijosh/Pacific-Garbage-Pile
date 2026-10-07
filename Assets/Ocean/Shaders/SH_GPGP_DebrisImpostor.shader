Shader "GPGP/Debris Impostor"
{
    Properties
    {
        _BaseMap("Debris Atlas", 2D) = "white" {}
        _Tint("Underwater Tint", Color) = (0.82,0.95,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.2
        _Cell("Atlas Cell", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DebrisImpostor"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Cutoff;
                float4 _Cell;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half shade:TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 center=TransformObjectToWorld(float3(0,0,0));
                float sx=length(float3(unity_ObjectToWorld._m00,unity_ObjectToWorld._m10,unity_ObjectToWorld._m20));
                float sy=length(float3(unity_ObjectToWorld._m01,unity_ObjectToWorld._m11,unity_ObjectToWorld._m21));
                float3 right=normalize(float3(UNITY_MATRIX_I_V._m00,UNITY_MATRIX_I_V._m10,UNITY_MATRIX_I_V._m20));
                float3 up=normalize(float3(UNITY_MATRIX_I_V._m01,UNITY_MATRIX_I_V._m11,UNITY_MATRIX_I_V._m21));
                float3 positionWS=center+right*input.positionOS.x*sx+up*input.positionOS.y*sy;
                output.positionCS=TransformWorldToHClip(positionWS);
                output.uv=(input.uv+_Cell.xy)*.25;
                output.shade=lerp(.72h,1.08h,saturate(input.uv.y));
                return output;
            }

            half4 Frag(Varyings input):SV_Target
            {
                half4 sample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv);
                clip(sample.a-_Cutoff);
                half edge=smoothstep(_Cutoff,_Cutoff+.25h,sample.a);
                half3 color=sample.rgb*_Tint.rgb*input.shade;
                color+=half3(.03h,.18h,.24h)*(1-edge);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
