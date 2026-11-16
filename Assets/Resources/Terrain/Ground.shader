// Ground of the map (MapView). The cell picture gives every cell its colour, already shaded by
// the fog of war, and in alpha the kind of ground: 0 — never seen (flat colour, nothing of the
// ground shows), 1 — ground, 2 — rock floor, 3 — ramp, 4 — ore. A detail texture of that kind is
// laid over it in the cells of the map; a wall of a cliff takes the cliff texture. Detail textures have a
// mean of 0.25 (Tools/art/tile.py), so with a gain of 4 the cell's own colour is kept on average.
Shader "CoopRTS/Ground"
{
    Properties
    {
        [MainTexture] [NoScaleOffset] _CellTex ("Cells: colour with fog, kind in alpha", 2D) = "white" {}
        [NoScaleOffset] _Layers ("Detail: ground, rock floor, ramp, ore, cliff", 2DArray) = "" {}
        _Tiling ("Cells per tile: ground, rock floor, ramp, ore", Vector) = (6, 4, 5, 4)
        _Cliff ("Cliff: cells per tile, height per tile, brightness", Vector) = (4, 3.2, 0.7, 0)
        _OreBed ("Ore bed brightness", Range(0, 1)) = 0.6
        _Gain ("Detail gain", Float) = 4
        _Macro ("Large patches, strength", Range(0, 0.5)) = 0.14
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Tiling;
            float4 _Cliff;
            float _OreBed;
            float _Gain;
            float _Macro;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_CellTex);
            TEXTURE2D_ARRAY(_Layers);
            SAMPLER(sampler_Layers);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                // Cells of the map: the mesh is built in them.
                float3 cells : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.cells = input.positionOS.xyz;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 cell = SAMPLE_TEXTURE2D(_CellTex, sampler_PointClamp, input.uv);
                int kind = (int)(cell.a * 255.0 + 0.5);
                float3 normal = normalize(input.normalWS);
                float3 cells = input.cells;

                bool wall = normal.y < 0.5;
                float layer = wall ? 4.0 : max(kind - 1, 0);
                float2 uv = wall
                    ? float2((abs(normal.x) > abs(normal.z) ? cells.z : cells.x) / _Cliff.x, cells.y / _Cliff.y)
                    : cells.xz / _Tiling[(int)layer];
                half3 detail = SAMPLE_TEXTURE2D_ARRAY(_Layers, sampler_Layers, uv, layer).rgb * _Gain;
                // Patches a few cells wide: a repeat of the texture is harder to see under them.
                float patches = Noise(cells.xz * 0.13) * 0.6 + Noise(cells.xz * 0.41 + 17.0) * 0.4;
                // The bed of a deposit is darker than its colour: the ore lying on it is what shines.
                detail *= (1.0 + _Macro * (patches * 2.0 - 1.0)) * (wall ? _Cliff.z : kind == 4 ? _OreBed : 1.0);
                // Never seen: one flat colour, no light and no shadow, so not even a cliff shows through.
                if (kind == 0)
                    return half4(cell.rgb, 1);
                half3 albedo = cell.rgb * detail;

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 ambient = SampleSH(normal);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor occlusion = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                    light.color *= occlusion.directAmbientOcclusion;
                    ambient *= occlusion.indirectAmbientOcclusion;
                #endif
                half lit = saturate(dot(normal, light.direction)) * light.shadowAttenuation * light.distanceAttenuation;
                half3 color = albedo * (light.color * lit + ambient);
                return half4(MixFog(color, input.fogFactor), 1);
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

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 Frag() : SV_Target
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
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Ambient occlusion is computed from this pass: without it nothing darkens where things meet the ground.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
