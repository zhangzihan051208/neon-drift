// 被风吹动的局部（雨棚、篷布、晾着的衣服、灯笼、垃圾袋）：盖在原图同一位置上的一小块，
// 按整像素左右或上下错动。没风的时候和原图完全一样。
Shader "NeonDrift/WindSway"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        _Rect ("Rect (uv x, y, w, h)", Vector) = (0, 0, 1, 1)
        _Wind ("Mode, Amp, Phase, Dir", Vector) = (0, 1.5, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _Rect;
                float4 _Wind;
            CBUFFER_END
            float _NDGust;      // 0 = 没风，1 = 一阵大风
            float _NDWindT;     // 累计的摆动相位（风大时走得快）

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 l = saturate((i.uv - _Rect.xy) / _Rect.zw);      // 这一小块里的位置，y 向上
                float g = _NDGust, t = _NDWindT + _Wind.z, amp = _Wind.y;
                float edgeX = smoothstep(0.0, 0.15, l.x) * smoothstep(0.0, 0.15, 1.0 - l.x);
                float edgeY = smoothstep(0.0, 0.15, l.y) * smoothstep(0.0, 0.15, 1.0 - l.y);
                float2 off = 0;
                if (_Wind.x < 0.5)                  // 挂着的：上边固定，越往下摆得越大
                {
                    float w = (1.0 - l.y) * (1.0 - l.y) * edgeX;
                    off.x = (sin(t + l.y * 5.0) * (0.2 + g) + g * 0.7 * _Wind.w) * amp * w;
                }
                else if (_Wind.x < 1.5)             // 立着的：下边固定，上面晃
                {
                    float w = l.y * l.y * edgeX;
                    off.x = (sin(t + l.y * 4.0) * (0.2 + g) + g * 0.6 * _Wind.w) * amp * w;
                }
                else                                // 铺着的篷布：一道道波纹
                {
                    float w = edgeX * edgeY;
                    off.y = sin(t * 1.3 + l.x * 9.0) * (0.15 + g) * amp * w;
                    off.x = cos(t * 0.9 + l.y * 7.0) * g * amp * 0.6 * w;
                }
                off = round(off) * _MainTex_TexelSize.xy;                // 只按整像素挪，保持像素画的硬边
                float2 lo = _Rect.xy + _MainTex_TexelSize.xy * 0.5, hi = _Rect.xy + _Rect.zw - _MainTex_TexelSize.xy * 0.5;
                float2 uv = clamp(i.uv - off, lo, hi);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
            }
            ENDHLSL
        }
    }
}
