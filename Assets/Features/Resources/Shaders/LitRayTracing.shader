Shader "Custom/LitRayTracing"
{
    Properties
    {
    [Main(SurfaceGroup, _, off, off)] _SurfaceGroup ("Surface Options", float) = 0
        // Blending state
        [Preset(SurfaceGroup, LWGUI_BlendModePreset)] _Surface("Blend Mode Preset", Float) = 0.0
        [SubEnum(SurfaceGroup, UnityEngine.Rendering.CullMode)]_Cull("Cull Mode", Float) = 2.0
        [SubEnum(SurfaceGroup, UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1.0
        [SubEnum(SurfaceGroup, UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0.0
        [SubToggle(SurfaceGroup, _)] _ZWrite("ZWrite", Float) = 1.0
        
    [Main(PBRGroup, _, off, off)] _PBRGroup ("PBR Options", float) = 0
        [Tex(PBRGroup, _BaseColor)] _BaseMap("Albedo", 2D) = "white" {}
        [HideInInspector] _BaseColor("Color", Color) = (1,1,1,1)
        [Sub(PBRGroup)]_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Space(10)]
        [Tex(PBRGroup)]_MetallicGlossMap("Mask Map", 2D) = "white" {}
        [Sub(PBRGroup)]_Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [Sub(PBRGroup)]_Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [Space(10)]
        [Tex(PBRGroup)]_BumpMap("Normal Map", 2D) = "bump" {}
        [Sub(PBRGroup)]_BumpScale("Normal Scale", Range(0,3)) = 1.0
        [Space(10)]
        [Tex(PBRGroup)]_OcclusionMap("Occlusion", 2D) = "white" {}
        [Sub(PBRGroup)]_OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [Space(10)]
        [Tex(PBRGroup, _EmissionColor)]_EmissionMap("Emission", 2D) = "white" {}
        [HideInInspector][HDR] _EmissionColor("Color", Color) = (0,0,0)
        
        // Legacy Parameters (Ignored)
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        // For Forward Raster Lighting
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend[_SrcBlend][_DstBlend]/*, [_SrcBlendAlpha][_DstBlendAlpha]*/
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]
            
            HLSLPROGRAM

            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // Universal Render Pipeline Keywords.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            // DDGI Debug Keywords.
            #pragma shader_feature DDGI_SHOW_INDIRECT_ONLY
            #pragma shader_feature DDGI_SHOW_PURE_INDIRECT_RADIANCE

            #define FORWARD_USE_DDGI 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Lib/DDGIInputs.hlsl"
            #include "Lib/DDGIProbeIndexing.hlsl"
            #include "Lib/DDGIFuncs.hlsl"

            #include "Lib/LitRayTracingInput.hlsl"
            #include "Lib/LitRayTracingForwardPass.hlsl"
            
            ENDHLSL
        }
        
        // For Ray Tracing Demo
        Pass
        {
            Name "RayTracing"
            Tags { "LightMode" = "RayTracing" }
            
            HLSLPROGRAM

            #pragma raytracing test // test和任何内容无关，加此行只是为了通过编译

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            
            #include "Lib/Common/RayTracingCommon.hlsl"
            #include "Lib/LitRayTracingInput.hlsl"

            [shader("closesthit")]
            void ClosestHitShader(inout RayPayload rayPayload : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
            {
                IntersectionVertex vertex = (IntersectionVertex)0;
                GetCurrentIntersectionVertex(attributeData, vertex);

                float3 normalWS = TransformObjectToWorldNormal(vertex.normalOS);
                float3 worldPos = TransformObjectToWorld(vertex.positionOS);
                
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(worldPos));
                float3 radiance = mainLight.color * mainLight.shadowAttenuation * _BaseColor * LambertNoPI() * dot(mainLight.direction, normalWS);

                rayPayload.color.rgb = radiance;
                rayPayload.color.a = 1.0f;
            }
            
            ENDHLSL
        }
        
        // For DDGI Ray Tracing
        Pass
        {
            Name "DDGIRayTracing"
            Tags { "LightMode" = "DDGIRayTracing" }
            
            HLSLPROGRAM

            #pragma raytracing test // test和任何内容无关，加此行只是为了通过编译

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #define DDGI_RAYTRACING 1

            #include "Lib/Common/RayTracingCommon.hlsl"
            #include "Lib/DDGIInputs.hlsl"
            #include "Lib/DDGIProbeIndexing.hlsl"
            #include "Lib/DDGIFuncs.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float  _BumpScale;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            [shader("closesthit")]
            void ClosestHitShader(inout DDGIPayload payload : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
            {
                if(payload.isShadowPayload)
                {
                    payload.isInShadow = true;
                    return;
                }

                payload.hitKind             = HitKind();
                payload.distance            = RayTCurrent();
                payload.worldRayDirection   = WorldRayDirection();

                if(HitKind() == HIT_KIND_TRIANGLE_BACK_FACE) return;

                // ---------------------------------------
                // Intersection geometry and brdf data.
                // ---------------------------------------
                IntersectionVertex vertex = (IntersectionVertex)0.0f;
                GetCurrentIntersectionVertex(attributeData, vertex);

                float2 uv   = vertex.uv;
                float3 N    = TransformObjectToWorldNormal(vertex.normalOS);
                float3 T    = TransformObjectToWorldDir(vertex.tangentOS.xyz);
                float3 P    = TransformObjectToWorld(vertex.positionOS);

                // Evaluate Lighting via Per-Pixel Normal
                const float3x3 tangentToWorld = CreateTangentToWorld(N, T, vertex.tangentOS.w * GetOddNegativeScale());
                N = UnpackNormalScale(SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, uv, 0), _BumpScale);
                N = TransformTangentToWorldDir(N, tangentToWorld, true);

                const float3 albedo = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0).rgb * _BaseColor.rgb;

                payload.worldPos    = P;
                payload.worldNormal = N;
                payload.albedo      = albedo;
                payload.emission    = _EmissionColor.rgb;
            }
            
            ENDHLSL
        }
        
        // Shadow Caster
        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // This pass is used when drawing to a _CameraNormalsTexture texture
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            // -------------------------------------
            // Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    
    CustomEditor "LWGUI.LWGUI"
}
