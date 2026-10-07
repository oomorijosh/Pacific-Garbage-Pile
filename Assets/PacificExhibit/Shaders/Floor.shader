Shader "PacificExhibit/Floor"
{
    Properties
    {
        _BaseColor("Floor Color / Texture Tint", Color) = (0.006, 0.013, 0.018, 1)
        [ToggleUI] _UseBaseMap("Use Tile Texture", Float) = 0
        [NoScaleOffset] _BaseMap("Tile Texture (2 x 2 Tiles)", 2D) = "white" {}
        [NoScaleOffset] _ReflectionTex("Planar Reflection (Per Camera)", 2D) = "black" {}
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.55
        [HideInInspector] _ReflectionEnabled("Reflection Enabled", Float) = 0
        _ReflectionTint("Reflection Tint", Color) = (0.7, 0.88, 1, 1)
        _TileScale("Tiles Per Meter", Float) = 1
        _RippleStrength("Reflection Ripple", Range(0, 0.02)) = 0.002
        [Toggle] _ReflectionFlipY("Flip Reflection Vertically", Float) = 0
        [HideInInspector] _ExhibitOrigin("Exhibit Origin", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "ReflectiveFloor"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Reflection cameras render to a mono texture, including in XR.
            // TEXTURE2D_X would incorrectly reinterpret that texture as an array.
            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ReflectionTint;
                float _UseBaseMap;
                float _ReflectionStrength;
                float _ReflectionEnabled;
                float _TileScale;
                float _RippleStrength;
                float _ReflectionFlipY;
                float4 _ExhibitOrigin;
                float4x4 _ReflectionVP;
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
                float4 reflectionPosition : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // Each content camera receives its matching reflection texture
                // and world projection, including asymmetric Cyber Canoe eyes.
                output.reflectionPosition = mul(_ReflectionVP, float4(output.positionWS, 1.0));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 worldXZ = input.positionWS.xz;
                float2 exhibitXZ = worldXZ - _ExhibitOrigin.xz;
                float2 tile = worldXZ * max(_TileScale, 0.01);
                float2 tileDistance = abs(frac(tile + 0.5) - 0.5);
                float lineDistance = min(tileDistance.x, tileDistance.y);
                float lineWidth = max(fwidth(lineDistance), 0.003);
                float grout = 1.0 - smoothstep(0.003, 0.003 + lineWidth, lineDistance);
                float grain = Hash21(floor(worldXZ * 160.0));
                float roughness = 0.88 + grain * 0.12;
                float tileVariation = 0.88 + Hash21(floor(tile)) * 0.15;
                half3 color = _BaseColor.rgb * tileVariation * (1.0 - grout * 0.65);
                // The museum image contains two tiles per axis. World-space UVs
                // keep each tile the same size across the entire gallery floor.
                half3 tileColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, tile * 0.5).rgb;
                color = lerp(color, tileColor * _BaseColor.rgb, saturate(_UseBaseMap));
                // A soft blue pool anchors the luminous box in its dark room.
                float groundGlow = exp2(-dot(exhibitXZ, exhibitXZ) * 0.15);
                color += half3(0.002, 0.012, 0.02) * groundGlow;

                float2 reflectionUV = input.reflectionPosition.xy / max(input.reflectionPosition.w, 0.0001) * 0.5 + 0.5;
                reflectionUV.y = lerp(reflectionUV.y, 1.0 - reflectionUV.y, saturate(_ReflectionFlipY));
                float time = _Time.y * 0.22;
                float2 ripple = float2(
                    sin(worldXZ.y * 23.0 + sin(worldXZ.x * 4.7) + time),
                    sin(worldXZ.x * 17.0 - worldXZ.y * 8.0 - time * 0.7));
                reflectionUV += ripple * _RippleStrength;
                float2 reflectionBorder = min(reflectionUV, 1.0 - reflectionUV);
                float reflectionMask = smoothstep(0.0, 0.025, min(reflectionBorder.x, reflectionBorder.y));
                reflectionMask *= step(0.0001, input.reflectionPosition.w) * saturate(_ReflectionEnabled);
                half3 reflection = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, saturate(reflectionUV)).rgb;
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1.0 - saturate(dot(normalize(input.normalWS), viewDirection)), 2.0);
                float distanceFade = exp2(-length(exhibitXZ) * 0.12);
                float strength = _ReflectionStrength * (0.45 + fresnel * 0.55) * distanceFade * reflectionMask;
                color += reflection * _ReflectionTint.rgb * strength * roughness * (1.0 - grout * 0.45);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
