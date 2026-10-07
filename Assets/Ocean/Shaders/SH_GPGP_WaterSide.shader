Shader "GPGP/Water Side"
{
    Properties
    {
        _BaseMap("Water Side", 2D) = "white" {}
        _Tint("Tint", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 0.82
        _DriftSpeed("Caustic Drift", Range(0,0.1)) = 0.012
        _CausticStrength("Moving Caustics", Range(0,0.5)) = 0.12
        _EdgeGlow("Edge Glow", Range(0,2)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "WaterSide"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            // The front wall must not depth-occlude the debris suspended behind it.
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Opacity;
                half _DriftSpeed;
                half _CausticStrength;
                half _EdgeGlow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half3 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                // A second, softly moving lookup animates only the bright upper caustics.
                float2 driftUV = float2(frac(uv.x + _Time.y * _DriftSpeed), saturate(uv.y));
                half3 moving = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, driftUV).rgb;
                half upperMask = smoothstep(0.35h, 0.92h, uv.y);
                half caustics = max(max(moving.r, moving.g), moving.b);
                half3 color = baseColor * _Tint.rgb;
                color += caustics * upperMask * _CausticStrength * half3(0.25h, 0.85h, 1.0h);
                half edge = 1.0h - smoothstep(0.0h, 0.025h, min(input.uv.x, 1.0h - input.uv.x));
                color += edge * _EdgeGlow * half3(0.05h, 0.55h, 0.8h);
                return half4(color, saturate(_Opacity * _Tint.a));
            }
            ENDHLSL
        }
    }
}
