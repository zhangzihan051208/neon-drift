// 零的风衣下摆：腰下到靴子上面那一段按整像素横向错开（往身后飘 + 一层波浪），靴子和上半身不动。
// 每帧的位置（_Frame）和飘的参数（_Coat）由 ZeroVisual 用 MaterialPropertyBlock 传进来。
// 动作图每帧 180 像素高，脚底在第 4 行，靴子大约到第 30 行，腰在 80 行上下。
Shader "NeonDrift/ZeroCoat"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        _Frame ("Frame UV rect (x, y, w, h)", Vector) = (0, 0, 1, 1)
        _Coat ("Flow px, Ripple px, Phase, Flip", Vector) = (0, 0, 0, 1)
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
                float4 _Frame;
                float4 _Coat;
            CBUFFER_END
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
                float py = (i.uv.y - _Frame.y) / max(_Frame.w, 1e-5) * 180.0;          // 这一帧里从下往上第几行
                float w = smoothstep(84.0, 40.0, py) * smoothstep(26.0, 38.0, py);        // 腰 → 下摆越来越大，到靴子收回 0
                float shift = (_Coat.x * w + sin(_Coat.z + py * 0.33) * _Coat.y * w) * _Coat.w;
                shift = round(shift);                                                    // 只按整像素挪，保持像素画的硬边
                float2 uv = i.uv;
                uv.x -= shift * _MainTex_TexelSize.x;
                uv.x = clamp(uv.x, _Frame.x + _MainTex_TexelSize.x * 0.5, _Frame.x + _Frame.z - _MainTex_TexelSize.x * 0.5);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * i.color;
            }
            ENDHLSL
        }
    }
}
