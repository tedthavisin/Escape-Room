// Night sky: the same latitude-longitude panorama as Skybox/Panoramic (identical sampling, tint and exposure, so the
// sky below the horizon still renders exactly the fog colour), with a slowly drifting cloud layer on top.
// Clouds are two samples of a tileable noise texture projected onto a flat layer overhead; they fade out towards the
// horizon so the horizon/fog match is untouched. Cheap enough for Quest (two texture reads, no loops).
Shader "LookoutTower/NightSkyClouds"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        [NoScaleOffset] _MainTex ("Spherical (HDR)", 2D) = "grey" {}

        [Header(Clouds)]
        [NoScaleOffset] _CloudNoise ("Cloud Noise (tileable, R)", 2D) = "grey" {}
        _CloudColor ("Cloud Color", Color) = (0.2, 0.22, 0.28, 1)
        _CloudCoverage ("Coverage", Range(0, 1)) = 0.6
        _CloudSoftness ("Softness", Range(0.01, 0.5)) = 0.1
        _CloudOpacity ("Opacity", Range(0, 1)) = 0.93
        _CloudScale ("Scale", Float) = 0.16
        _CloudSpeed ("Drift (uv/s), xy = big, zw = detail", Vector) = (0.0035, 0.0018, 0.006, 0.003)
        _HorizonFade ("Horizon Fade", Range(0.01, 0.6)) = 0.22
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _MainTex_HDR;
            half4 _Tint;
            half _Exposure;

            sampler2D _CloudNoise;
            half4 _CloudColor;
            half _CloudCoverage, _CloudSoftness, _CloudOpacity, _HorizonFade;
            float _CloudScale;
            float4 _CloudSpeed;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // as Skybox/Panoramic
            inline float2 ToRadialCoords(float3 coords)
            {
                float3 n = normalize(coords);
                float latitude = acos(n.y);
                float longitude = atan2(n.z, n.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 tc = ToRadialCoords(i.texcoord);
                half4 tex = tex2D(_MainTex, tc);
                half3 c = DecodeHDR(tex, _MainTex_HDR);
                c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
                c *= _Exposure;

                float3 dir = normalize(i.texcoord);
                if (dir.y > 0.0)
                {
                    // flat cloud layer overhead: perspective squashes it towards the horizon
                    float2 uv = dir.xz / (dir.y + 0.12) * _CloudScale;
                    half big = tex2D(_CloudNoise, uv + _Time.y * _CloudSpeed.xy).r;
                    half detail = tex2D(_CloudNoise, uv * 2.7 + float2(0.37, 0.71) + _Time.y * _CloudSpeed.zw).r;
                    half n = big * 0.68 + detail * 0.32;
                    half threshold = 1.0 - _CloudCoverage;
                    half density = smoothstep(threshold - _CloudSoftness, threshold + _CloudSoftness, n);
                    density *= smoothstep(0.0, _HorizonFade, dir.y) * _CloudOpacity;
                    // thinner edges catch a little moonlight
                    half3 cloud = _CloudColor.rgb * lerp(1.35, 1.0, density);
                    c = lerp(c, cloud, density);
                }
                return half4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
