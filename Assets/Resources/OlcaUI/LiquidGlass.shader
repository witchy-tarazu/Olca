Shader "Olca/UI/LiquidGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("9スライスマスク", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            sampler2D _CameraSortingLayerTexture;
            float4 _ClipRect;

            struct Vertex
            {
                float4 position : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 surface : TEXCOORD1;
            };
            struct Interpolators
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float2 local : TEXCOORD2;
                float4 surface : TEXCOORD3;
            };

            Interpolators Vert(Vertex input)
            {
                Interpolators result;
                result.position = UnityObjectToClipPos(input.position);
                result.screen = ComputeScreenPos(result.position);
                result.local = input.position.xy;
                result.uv = input.uv;
                result.color = input.color;
                result.surface = input.surface;
                return result;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                half4 mask = tex2D(_MainTex, input.uv);
                float2 uv = input.screen.xy / input.screen.w;
                half control = mask.b;

                // 背景の位置を保ち、面全体を同じ強さで対称にぼかす。
                float2 tap = 2.1 * lerp(1.0, .8, control) / _ScreenParams.xy;
                half3 scene = tex2D(_CameraSortingLayerTexture, uv).rgb * .25;
                scene += tex2D(_CameraSortingLayerTexture, uv + float2(tap.x, 0)).rgb * .125;
                scene += tex2D(_CameraSortingLayerTexture, uv - float2(tap.x, 0)).rgb * .125;
                scene += tex2D(_CameraSortingLayerTexture, uv + float2(0, tap.y)).rgb * .125;
                scene += tex2D(_CameraSortingLayerTexture, uv - float2(0, tap.y)).rgb * .125;
                scene += tex2D(_CameraSortingLayerTexture, uv + tap).rgb * .0625;
                scene += tex2D(_CameraSortingLayerTexture, uv - tap).rgb * .0625;
                scene += tex2D(_CameraSortingLayerTexture, uv + float2(tap.x, -tap.y)).rgb * .0625;
                scene += tex2D(_CameraSortingLayerTexture, uv + float2(-tap.x, tap.y)).rgb * .0625;

                // ゲーム画面の色を通しつつ、明るい背景でも白文字を読める輝度に抑える。
                half light = saturate(dot(scene, half3(.2126, .7152, .0722)));
                half2 inset = saturate(1.0 - abs(input.surface.xy * 2.0 - 1.0));
                half center = smoothstep(0.0, .92, inset.x * inset.y);
                // 暗い背景では薄い着色に留め、明るい背景では読取位置だけを追加で減光する。
                half bright = smoothstep(.14, .68, light);
                half readingArea = lerp(center, lerp(.32, 1.0, center), control);
                half dim = .04 + center * .10 + bright * lerp(.06, .65, readingArea);
                half3 tint = lerp(half3(.12, .23, .28), half3(.046, .10, .14), bright);
                half3 color = lerp(scene, tint, dim);

                // 明所のパネルは景色の色を残したまま減光し、中央で深くする。
                half brightPanel = bright * (1.0 - control);
                half3 richScene = max(0.0, light + (scene - light) * 1.75);
                richScene *= lerp(.48, .24, center);
                color = lerp(color, richScene, brightPanel);
                color = lerp(color, input.color.rgb, control * lerp(.08, .28, center));
                // フォーカス面は色相を固定せず、ガラス越しの景色の彩度を上げる。
                half focus = input.surface.z;
                half3 vividGlass = max(0.0, light + (scene - light) * 1.8);
                color = lerp(color, vividGlass * lerp(.95, .55, bright), focus * .72);

                // 上と左に細い反射を入れ、反対側に薄い影を落とす。
                float2 slope = float2(ddx(mask.r), ddy(mask.r));
                float2 normal = slope * rsqrt(dot(slope, slope) + .0003);
                half incidence = dot(normal, half2(-.55, -.83));

                // 暗い背景では反射の色を保ち、光る範囲を外周の細い帯に絞る。
                half rimLight = smoothstep(.08, .62, light);
                half rimStart = lerp(.88, .66, rimLight);
                half rim = smoothstep(rimStart, .99, mask.r) * smoothstep(.35, .85, mask.a);
                half rimStrength = lerp(.44, 1.0, rimLight);
                half brightRim = rim * rimStrength * (.12 + .70 * smoothstep(-.10, .85, incidence));
                half darkRim = rim * rimStrength * .38 * smoothstep(.10, .85, -incidence);
                color = lerp(color, color * .37, darkRim);
                half glint = rim * smoothstep(-.35, .72, incidence) * focus * .88;
                half3 rimColor = lerp(half3(.82, .95, 1.0), half3(.98, .99, 1.0), focus);
                color = lerp(color, rimColor, saturate(brightRim + glint));
                half alpha = mask.a * input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.local, _ClipRect);
                #endif
                return half4(color, alpha);
            }
            ENDCG
        }
    }
}
