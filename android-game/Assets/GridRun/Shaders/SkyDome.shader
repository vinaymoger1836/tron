// Camera-centred backdrop: deep zenith, hazy horizon, and a thin luminous horizon line.
Shader "GridRun/SkyDome"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.002, 0.004, 0.010, 1)
        _Horizon("Horizon", Color) = (0.015, 0.060, 0.085, 1)
        [HDR] _Band("Horizon band", Color) = (0.10, 0.65, 0.90, 1)
        _Ground("Below horizon", Color) = (0.004, 0.010, 0.016, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        Pass
        {
            Name "SkyDome"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith;
                half4 _Horizon;
                half4 _Band;
                half4 _Ground;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.direction = world - TransformObjectToWorld(float3(0, 0, 0));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float h = normalize(input.direction).y;
                half3 color = h >= 0 ? lerp(_Horizon.rgb, _Zenith.rgb, saturate(pow(h, 0.55) * 1.35))
                                     : lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 7.0));
                color += _Band.rgb * (exp(-abs(h - 0.012) * 140.0) * 0.55 + exp(-abs(h) * 22.0) * 0.08);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
