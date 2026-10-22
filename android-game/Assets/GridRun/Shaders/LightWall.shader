// Translucent light ribbon for trails and boundaries. Height comes from the object's
// Y scale, so stretched unit cubes keep bright top and base edges of constant width.
Shader "GridRun/LightWall"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.2, 2.2, 3.0, 1)
        _FillAlpha("Fill alpha", Range(0, 1)) = 0.42
        _EdgeWidth("Edge width", Float) = 0.07
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "LightWall"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
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
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 height : TEXCOORD0; // x: height above base, y: total height (world units)
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
                float y = input.height.x;
                float top = input.height.y - y;
                half edge = max(exp(-top / _EdgeWidth), exp(-y / (_EdgeWidth * 0.8)) * 0.8);
                half scan = 0.88 + 0.12 * sin(y * 46.0);
                half alpha = saturate(_FillAlpha * scan + edge);
                half3 color = _Color.rgb * (0.45 + edge * 2.4);
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
