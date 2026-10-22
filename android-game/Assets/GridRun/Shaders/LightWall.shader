// Translucent light ribbon for trails and boundaries. Height comes from the object's
// Y scale, so stretched unit cubes keep bright top and base edges of constant width.
Shader "GridRun/LightWall"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.2, 2.2, 3.0, 1)
        _FillAlpha("Fill alpha", Range(0, 1)) = 0.42
        _EdgeWidth("Edge width", Float) = 0.07
        _StripeA("Stripe A height fraction (0 = none)", Range(0, 1)) = 0
        _StripeB("Stripe B height fraction (0 = none)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "LightWall"
            Tags { "LightMode" = "UniversalForward" }
            // Alpha blending (not additive) so overlapping segments and box faces never stack into white.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FillAlpha;
                float _EdgeWidth;
                half _StripeA;
                half _StripeB;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // x: height above base, y: total height (world units). Centroid sampling keeps MSAA
                // edge samples on thin, grazing faces from extrapolating far outside the wall.
                centroid float2 height : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                float scaleY = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
                output.positionCS = position.positionCS;
                output.height = float2((input.positionOS.y + 0.5) * scaleY, scaleY);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Clamp so an extrapolated sample can never overflow exp() into Inf/NaN (which bloom spreads).
                float y = clamp(input.height.x, 0.0, input.height.y);
                float top = input.height.y - y;
                half edge = max(exp(-top / _EdgeWidth), exp(-y / (_EdgeWidth * 0.8)) * 0.8);
                // Rider-style bands (Hunter, Survivor, Trapper) so trails differ by more than colour.
                half stripe = (_StripeA > 0 ? exp(-abs(y - _StripeA * input.height.y) / 0.022) : 0) +
                              (_StripeB > 0 ? exp(-abs(y - _StripeB * input.height.y) / 0.022) : 0);
                edge = max(edge, stripe * 0.85);
                half scan = 0.88 + 0.12 * sin(y * 46.0);
                half alpha = saturate(_FillAlpha * scan + edge);
                half3 color = _Color.rgb * (0.32 + edge * 1.15);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
