Shader "CarWreck/CompactHatchback/Two Color Paint"
{
    Properties
    {
        _PrimaryColor("Primary Paint", Color) = (0.71,0.0054,0.0027,1)
        _StripeColor("Stripe Color", Color) = (0.869,0.848,0.748,1)
        [NoScaleOffset] _StripeMask("Stripe Mask (UV1)", 2D) = "black" {}
        _Metallic("Metallic", Range(0,1)) = 0.18
        _Smoothness("Smoothness", Range(0,1)) = 0.72
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _PrimaryColor, _StripeColor;
        half _Metallic, _Smoothness;
        CBUFFER_END
        TEXTURE2D(_StripeMask); SAMPLER(sampler_StripeMask);
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 stripeUV:TEXCOORD1; };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 stripeUV:TEXCOORD2; };
        Varyings Vert(Attributes a)
        {
            Varyings v;
            VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);
            v.positionCS=p.positionCS; v.positionWS=p.positionWS;
            v.normalWS=TransformObjectToWorldNormal(a.normalOS); v.stripeUV=a.stripeUV;
            return v;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardPaint"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            half4 Frag(Varyings v):SV_Target
            {
                InputData data=(InputData)0;
                data.positionWS=v.positionWS; data.normalWS=NormalizeNormalPerPixel(v.normalWS);
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);
                data.shadowCoord=TransformWorldToShadowCoord(v.positionWS);
                data.bakedGI=SampleSH(data.normalWS); data.shadowMask=half4(1,1,1,1);
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);
                data.vertexLighting=VertexLighting(v.positionWS,data.normalWS);
                SurfaceData surface=(SurfaceData)0;
                half mask=SAMPLE_TEXTURE2D(_StripeMask,sampler_StripeMask,v.stripeUV).r;
                surface.albedo=lerp(_PrimaryColor.rgb,_StripeColor.rgb,mask);
                surface.alpha=1; surface.metallic=_Metallic; surface.smoothness=_Smoothness;
                surface.normalTS=half3(0,0,1); surface.occlusion=1;
                half4 color=UniversalFragmentPBR(data,surface);
                color.rgb=MixFog(color.rgb,ComputeFogFactor(TransformWorldToHClip(v.positionWS).z));
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(Attributes a):SV_POSITION
            {
                float3 p=TransformObjectToWorld(a.positionOS.xyz);
                float3 n=TransformObjectToWorldNormal(a.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 direction=normalize(_LightPosition-p);
                #else
                float3 direction=_LightDirection;
                #endif
                float4 position=TransformWorldToHClip(ApplyShadowBias(p,n,direction));
                #if UNITY_REVERSED_Z
                position.z=min(position.z,UNITY_NEAR_CLIP_VALUE*position.w);
                #else
                position.z=max(position.z,UNITY_NEAR_CLIP_VALUE*position.w);
                #endif
                return position;
            }
            half4 DepthFrag():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings v):SV_Target { return v.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings v):SV_Target
            {
                float3 normalWS=normalize(v.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct=PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
                #else
                return half4(normalWS,0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
