#ifndef LIT_RAY_TRACING_FORWARD_PASS
#define LIT_RAY_TRACING_FORWARD_PASS

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;
    float3 positionWS               : TEXCOORD1;
    float3 normalWS                 : TEXCOORD2;
    float4 tangentWS                : TEXCOORD3;
    float4 shadowCoord              : TEXCOORD4;
    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    half3 viewDirWS         = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float sgn               = input.tangentWS.w;
    float3 bitangent        = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld  = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    inputData.positionWS        = input.positionWS;
    inputData.positionCS        = input.positionCS;
    inputData.normalWS          = TransformTangentToWorld(normalTS, tangentToWorld);
    inputData.normalWS          = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS   = viewDirWS;
    inputData.shadowCoord       = TransformWorldToShadowCoord(inputData.positionWS);
    inputData.fogCoord          = 0.0f; // Ignored
    inputData.vertexLighting    = 0.0f; // Ignored
    inputData.bakedGI           = 0.0f; // Ignored
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask        = 1.0f; // Ignored
    inputData.tangentToWorld    = tangentToWorld;
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput   = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    real sign        = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS  = half4(normalInput.tangentWS.xyz, sign);

    output.uv           = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionWS   = vertexInput.positionWS;
    output.normalWS     = normalInput.normalWS;
    output.tangentWS    = tangentWS;
    output.shadowCoord  = GetShadowCoord(vertexInput);
    output.positionCS   = vertexInput.positionCS;

    return output;
}

void LitPassFragment(Varyings input, out half4 outColor : SV_Target0)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData = (SurfaceData)0;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    InputData   inputData   = (InputData)0;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    BRDFData    brdfData    = (BRDFData)0;
    InitializeBRDFData(surfaceData, brdfData);
    
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);

    float4 color = float4(0.0f, 0.0f, 0.0f, surfaceData.alpha);

    // Emissive
    color.rgb += surfaceData.emission;

    // Direct Lighting Evaluate
    Light  mainLight = GetMainLight(inputData.shadowCoord);
    color.rgb        += LightingPhysicallyBased(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS) * aoFactor.directAmbientOcclusion;

    for(int i = 0; i < GetAdditionalLightsCount(); ++i)
    {
        Light  addLight     = GetAdditionalLight(i, inputData.positionWS);
        color.rgb           += LightingPhysicallyBased(brdfData, addLight, inputData.normalWS, inputData.viewDirectionWS) * aoFactor.directAmbientOcclusion;
    }

    // Indirect Lighting Evaluate.
    float3 indirectRadiance = SampleDDGIIrradiance(inputData.positionWS, inputData.normalWS, -inputData.viewDirectionWS);
    float3 indirectLighting = surfaceData.albedo * indirectRadiance * aoFactor.indirectAmbientOcclusion;
    #ifdef DDGI_SHOW_INDIRECT_ONLY
        color.rgb           = indirectLighting;
    #elif DDGI_SHOW_PURE_INDIRECT_RADIANCE
        color.rgb           = indirectRadiance;
    #else
        color.rgb           += indirectLighting;
    #endif

    outColor = color;
}

#endif