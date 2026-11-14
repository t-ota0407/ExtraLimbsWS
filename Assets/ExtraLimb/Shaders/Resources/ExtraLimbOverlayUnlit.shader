// 当たる範囲（リーチング課題の効果器の球）を、手や指に隠れないように手前に描く半透明の色（2026-11-13）。
// 縁を濃く、中を薄くして、泡のように輪郭が見えるようにする（_CenterAlpha＝中の濃さの比、_RimPower＝縁の鋭さ）。
// URP の Unlit は奥行きの比較を変えられないので用意した。VR のシングルパス（インスタンス）でも両目に描く。
// Resources に置くのは、ビルドに必ず入れて Shader.Find で見つけるため。
Shader "ExtraLimb/OverlayUnlit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 0.5)
        _CenterAlpha ("Center Alpha (ratio)", Range(0, 1)) = 0.4
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
        [HideInInspector] _Color ("Color (Material.color)", Color) = (1, 1, 1, 0.5)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Overlay"
            ZWrite Off
            ZTest Always
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _CenterAlpha;
                half _RimPower;
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                half rim = pow(1.0h - facing, _RimPower);
                half alpha = _BaseColor.a * lerp(_CenterAlpha, 1.0h, rim);
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
