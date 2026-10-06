Shader "PacificExhibit/RippleWater"
{
    Properties
    {
        _BaseColor("Deep Water Tint", Color) = (0.004, 0.095, 0.16, 1)
        [HDR] _GlowColor("Caustic Glow", Color) = (0.04, 0.8, 1, 1)
        _EmissionStrength("Caustic Brightness", Range(0, 20)) = 6
        _Opacity("Water Opacity", Range(0, 1)) = 0.2
        _WaveHeight("Wave Height", Range(0, 0.2)) = 0.025
        _WaveScale("Wave Scale", Float) = 2
        _WaveSpeed("Wave Speed", Float) = 0.4
        _Phase("Layer Phase", Float) = 0
        _CellScale("Caustic Cell Scale", Float) = 5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "RippleWater"
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
                float _EmissionStrength;
                float _Opacity;
                float _WaveHeight;
                float _WaveScale;
                float _WaveSpeed;
                float _Phase;
                float _CellScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 surfaceXZ : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The edges stay attached to the box while the inner grid gently moves.
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 position = input.positionOS.xyz;
                float2 p = position.xz * _WaveScale;
                float time = _Time.y * _WaveSpeed + _Phase;
                float a = dot(p, float2(1.14, 0.73)) + time;
                float b = dot(p, float2(-0.68, 1.41)) - time * 0.81;
                float c = dot(p, float2(1.83, -0.94)) + time * 0.58;
                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                float edgeFade = smoothstep(0.0, 0.06, min(edgeDistance.x, edgeDistance.y));
                float amplitude = _WaveHeight * edgeFade;
                position.y += amplitude * (sin(a) * 0.55 + sin(b) * 0.3 + sin(c) * 0.15);
                float2 gradient = amplitude * _WaveScale * (
                    cos(a) * 0.55 * float2(1.14, 0.73) +
                    cos(b) * 0.3 * float2(-0.68, 1.41) +
                    cos(c) * 0.15 * float2(1.83, -0.94));
                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(float3(-gradient.x, 1.0, -gradient.y));
                output.surfaceXZ = input.positionOS.xz;
                output.uv = input.uv;
                return output;
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Two nearest moving sites produce irregular branching wave crests.
            float CausticEdge(float2 p, float time)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float closest = 8.0;
                float secondClosest = 8.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 seed = Hash22(cell + offset);
                        float2 site = offset + 0.5 + 0.36 * sin(6.2831853 * seed + time * float2(0.54, 0.43));
                        float2 delta = site - local;
                        float distanceSquared = dot(delta, delta);
                        secondClosest = min(secondClosest, max(closest, distanceSquared));
                        closest = min(closest, distanceSquared);
                    }
                }
                return sqrt(secondClosest) - sqrt(closest);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = _Time.y * _WaveSpeed + _Phase;
                float2 p = input.surfaceXZ * max(_CellScale, 0.1);
                p += float2(sin(p.y * 1.2 + time * 0.48), cos(p.x * 0.91 - time * 0.37)) * 0.26;
                float edge = CausticEdge(p, time);
                float antialias = max(fwidth(edge), 0.006);
                float causticLine = 1.0 - smoothstep(0.028, 0.028 + antialias, edge);
                float halo = exp2(-edge * 11.0) * 0.22;
                float fineEdge = CausticEdge(p * 1.76 + float2(11.7, 8.3), -time * 0.7);
                float fineLine = 1.0 - smoothstep(0.018, 0.018 + max(fwidth(fineEdge), 0.006), fineEdge);
                float shimmer = 0.78 + 0.22 * sin(p.x * 1.49 + p.y * 0.87 + time * 1.1);
                float network = (causticLine + fineLine * 0.2) * shimmer;
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewDirection))), 3.0);
                half3 color = _BaseColor.rgb * (0.65 + fresnel * 0.65);
                color += _GlowColor.rgb * _EmissionStrength * (network + halo);
                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                float rim = 1.0 - smoothstep(0.004, 0.018, min(edgeDistance.x, edgeDistance.y));
                color += _GlowColor.rgb * _EmissionStrength * rim * 0.22;
                half alpha = saturate(_Opacity + network * 0.46 + fresnel * 0.06 + rim * 0.15);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
