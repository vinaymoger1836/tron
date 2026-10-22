// Glossy Grid floor: procedural anti-aliased lines, optional planar reflection, and
// coloured light pools under nearby cycles. One draw call replaces per-line geometry.
Shader "GridRun/GridFloor"
{
    Properties
    {
        _BaseColor("Base color", Color) = (0.006, 0.011, 0.018, 1)
        [HDR] _LineColor("Line color", Color) = (0.12, 1.1, 1.5, 1)
        _GridSize("Grid size", Float) = 4
        _LineWidth("Line half width", Float) = 0.025
        _MajorEvery("Major line interval", Float) = 4
        _Reflectivity("Reflectivity", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "GridFloor"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                float _GridSize;
                float _LineWidth;
                float _MajorEvery;
                half _Reflectivity;
            CBUFFER_END

            TEXTURE2D(_GridReflectionTex);
            SAMPLER(sampler_GridReflectionTex);
            half _GridReflectionOn;
            float4 _GridGlowPos[4];
            half4 _GridGlowColor[4];

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Coverage of grid lines with a fixed world width; fades sub-pixel lines instead of shimmering.
            half Lines(float2 world, float spacing, float halfWidth, out float nearest)
            {
                float2 distance = abs(frac(world / spacing + 0.5) - 0.5) * spacing;
                float2 footprint = max(fwidth(world), 1e-4);
                float2 coverage = saturate((halfWidth - distance) / footprint + 0.5) * saturate(2.0 * halfWidth / footprint);
                nearest = min(distance.x, distance.y);
                return max(coverage.x, coverage.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 world = input.positionWS.xz;
                float minorDistance, majorDistance;
                half minor = Lines(world, _GridSize, _LineWidth, minorDistance);
                half major = Lines(world, _GridSize * _MajorEvery, _LineWidth * 1.8, majorDistance);
                float footprint = max(fwidth(world.x), fwidth(world.y));
                half haloFade = saturate(0.6 / max(footprint * 8.0, 1e-3));
                half halo = (exp(-minorDistance * 9.0) * 0.10 + exp(-majorDistance * 5.0) * 0.16) * haloFade;

                half3 color = _BaseColor.rgb;
                half3 pools = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 offset = world - _GridGlowPos[i].xz;
                    pools += _GridGlowColor[i].rgb * (_GridGlowPos[i].w * exp(-dot(offset, offset) * 0.28));
                }
                color += pools * 0.20;
                color += _LineColor.rgb * (minor * 0.45 + major + halo) * (1.0 + pools * 0.8);

                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = 0.30 + 0.70 * pow(1.0 - saturate(view.y), 4.0);
                float2 screen = GetNormalizedScreenSpaceUV(input.positionCS);
                // The mirror camera is a proper rotation of the main camera, so its image is horizontally flipped.
                float2 uv = float2(1.0 - screen.x, screen.y);
                half3 reflection = SAMPLE_TEXTURE2D(_GridReflectionTex, sampler_GridReflectionTex, uv).rgb * 0.40;
                reflection += SAMPLE_TEXTURE2D(_GridReflectionTex, sampler_GridReflectionTex, uv + float2(0, 0.004)).rgb * 0.22;
                reflection += SAMPLE_TEXTURE2D(_GridReflectionTex, sampler_GridReflectionTex, uv - float2(0, 0.004)).rgb * 0.22;
                reflection += SAMPLE_TEXTURE2D(_GridReflectionTex, sampler_GridReflectionTex, uv + float2(0, 0.010)).rgb * 0.08;
                reflection += SAMPLE_TEXTURE2D(_GridReflectionTex, sampler_GridReflectionTex, uv - float2(0, 0.010)).rgb * 0.08;
                color += reflection * (_Reflectivity * fresnel * _GridReflectionOn);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
