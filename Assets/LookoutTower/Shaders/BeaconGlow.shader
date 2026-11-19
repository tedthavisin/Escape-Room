// Soft round additive glow for distant lamps. Deliberately ignores scene fog: a bright lamp cuts through night haze
// far better than the fog model suggests. Still depth-tested, so trees and walls hide it.
Shader "LookoutTower/BeaconGlow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (8, 5, 2, 1)
        _Falloff ("Falloff", Range(1, 8)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Falloff;
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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0;            // 0 at the centre, 1 at the quad edge
                half core = saturate(1.0 - r);
                half glow = pow(core, _Falloff) + 2.0 * pow(core, _Falloff * 6.0);
                return half4(_Color.rgb * glow, 1.0);
            }
            ENDHLSL
        }
    }
}
