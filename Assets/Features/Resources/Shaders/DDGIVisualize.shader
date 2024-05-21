Shader "Custom/DDGIVisualize"
{
    Properties
    {

    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline" 
        }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma multi_compile _ DDGI_DEBUG_IRRADIANCE DDGI_DEBUG_DISTANCE DDGI_DEBUG_OFFSET

            #define DDGI_VISUALIZATION 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Lib/DDGIInputs.hlsl"
            #include "Lib/DDGIProbeIndexing.hlsl"
            #include "Lib/DDGIFuncs.hlsl"

            float4x4 _ddgiSphere_ObjectToWorld;

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                uint instanceId     : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : NORMAL;
                uint probeIndex     : SV_InstanceID;
            };

            Varyings vert(Attributes vin)
            {
                Varyings vout = (Varyings)0;

                //float3 worldPos = TransformObjectToWorld(vin.positionOS);
                float3 worldPos = mul((float3x3)_ddgiSphere_ObjectToWorld, vin.positionOS);

                uint   probeIndex    = vin.instanceId;
                float3 probePosition = DDGIGetRelocatedProbeWorldPosition(probeIndex);
                worldPos += probePosition;

                vout.positionCS = TransformWorldToHClip(worldPos);
                // 不要使用TransformObjectToWorldNormal，不知为什么在使用它的时候会造成奇怪的法线变换（有时会让法线反向）
                vout.normalWS = mul(vin.normalOS, (float3x3)_ddgiSphere_ObjectToWorld);
                vout.probeIndex = probeIndex;

                return vout;
            }

            float4 frag(Varyings pin) : SV_Target
            {
                #ifdef DDGI_DEBUG_IRRADIANCE
                    float3 uv       = DDGIGetProbeUV(pin.probeIndex, SafeNormalize(pin.normalWS), PROBE_IRRADIANCE_TEXELS);
		            float3 radiance = SAMPLE_TEXTURE2D_ARRAY_LOD(_ProbeIrradianceHistory, sampler_LinearClamp, uv.xy, uv.z, 0).rgb;
		            radiance        = pow(radiance, 2.5f);
		            float4 result   = float4(radiance, 1.0f);
                #elif DDGI_DEBUG_DISTANCE
                    float3 uv       = DDGIGetProbeUV(pin.probeIndex, SafeNormalize(pin.normalWS), PROBE_DISTANCE_TEXELS);
		            float distance  = SAMPLE_TEXTURE2D_ARRAY_LOD(_ProbeDistanceHistory, sampler_LinearClamp, uv.xy, uv.z, 0).r;
		            float3 color    = distance.xxx / (Max(_ProbeSize) * 3);
		            float4 result   = float4(color, 1.0f);
                #elif DDGI_DEBUG_OFFSET
                    uint3 probeDataCoords   = DDGIGetProbeTexelCoordsOneByOne(pin.probeIndex);
                    float3 offset           = LOAD_TEXTURE2D_ARRAY_LOD(_ProbeData, probeDataCoords.xy, probeDataCoords.z, 0).xyz;
                    float4 result           = float4(abs(offset), 1);
                #else
                    // May add more debug modes ?
                    float4 result = float4(0,0,1,0);
                #endif

                return result;
            }
            
            ENDHLSL
        }
    }
}
