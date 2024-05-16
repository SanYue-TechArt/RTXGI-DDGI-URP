#ifndef RAY_TRACING_COMMON
#define RAY_TRACING_COMMON

#define INTERPOLATE_RAYTRACING_ATTRIBUTE(A0, A1, A2, BARYCENTRIC_COORDINATES) (A0 * BARYCENTRIC_COORDINATES.x + A1 * BARYCENTRIC_COORDINATES.y + A2 * BARYCENTRIC_COORDINATES.z)

#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

RaytracingAccelerationStructure _AccelerationStructure;

// 相交几何属性
struct IntersectionVertex
{
    float3 positionOS;
    float3 normalOS;
    float4 tangentOS;
    float2 uv;
};

// 重心坐标插值
struct AttributeData
{
    float2 barycentrics;
};

inline void GenerateCameraRay(out float3 origin, out float3 direction)
{
    float2 xy = DispatchRaysIndex().xy + 0.5f; // center in the middle of the pixel. 
    float2 screenPos = xy / DispatchRaysDimensions().xy * 2.0f - 1.0f; //Range (-1,1)
    screenPos.y *= -1;
    
    // Un project the pixel coordinate into a ray.
    float4 world  = mul(_InvCameraViewProj, float4(screenPos, 0, 1));
    world.xyz     /= world.w;
    origin        = _WorldSpaceCameraPos.xyz;
    direction     = normalize(world.xyz - origin);
}

void FetchIntersectionVertex(uint vertexIndex, out IntersectionVertex outVertex)
{
    outVertex.positionOS    = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
    outVertex.normalOS      = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
    outVertex.tangentOS     = UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTangent);
    outVertex.uv            = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
}

void GetCurrentIntersectionVertex(AttributeData attributeData, out IntersectionVertex outVertex)
{
    // Fetch the indices of the currentr triangle
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

    // Fetch the 3 vertices
    IntersectionVertex v0, v1, v2;
    FetchIntersectionVertex(triangleIndices.x, v0);
    FetchIntersectionVertex(triangleIndices.y, v1);
    FetchIntersectionVertex(triangleIndices.z, v2);

    // Compute the full barycentric coordinates
    float3 barycentricCoordinates = float3(1.0 - attributeData.barycentrics.x - attributeData.barycentrics.y, attributeData.barycentrics.x, attributeData.barycentrics.y);
    float3 positionOS   = INTERPOLATE_RAYTRACING_ATTRIBUTE(v0.positionOS, v1.positionOS, v2.positionOS, barycentricCoordinates);
    float3 normalOS     = INTERPOLATE_RAYTRACING_ATTRIBUTE(v0.normalOS, v1.normalOS, v2.normalOS, barycentricCoordinates);
    float4 tangentOS    = INTERPOLATE_RAYTRACING_ATTRIBUTE(v0.tangentOS, v1.tangentOS, v2.tangentOS, barycentricCoordinates);
    float2 uv           = INTERPOLATE_RAYTRACING_ATTRIBUTE(v0.uv, v1.uv, v2.uv, barycentricCoordinates);

    outVertex.positionOS    = positionOS;
    outVertex.normalOS      = normalOS;
    outVertex.tangentOS     = tangentOS;
    outVertex.uv            = uv;
}

// 追踪阴影光线的方法（只是留档，目前不支持在Unity中使用，因为Closest Hit Shader中不允许使用RayTraceInline）
// 相关讨论：https://forum.unity.com/threads/raytracing-rayquery-and-traceinline.961075/
bool TraceShadowRay(RayDesc rayDesc)
{
    RayQuery<RAY_FLAG_CULL_NON_OPAQUE | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> q;

    q.TraceRayInline(_AccelerationStructure, RAY_FLAG_NONE, 0xFF, rayDesc);
    while (q.Proceed())
    {
		switch (q.CandidateType())
        {
            case CANDIDATE_NON_OPAQUE_TRIANGLE:
		    {
                q.CommitNonOpaqueTriangleHit();
                break;
            }
        }
    }
    return q.CommittedStatus() != COMMITTED_TRIANGLE_HIT;
}

bool TraceDirectionalShadowRay(Light light, float3 worldPos)
{
    RayDesc rayDesc;
    rayDesc.Origin      = worldPos;
    rayDesc.Direction   = light.direction;
    rayDesc.TMin        = 1e-1f;
    rayDesc.TMax        = FLT_MAX;

    return TraceShadowRay(rayDesc);
}

bool TracePunctualShadowRay(uint i, float3 worldPos)
{
    // Reference: RealtimeLights.hlsl
    #if USE_FORWARD_PLUS
        int lightIndex = i;
    #else
        int lightIndex = GetPerObjectLightIndex(i);
    #endif

    Light light = GetAdditionalPerObjectLight(i, worldPos);
    
    RayDesc rayDesc;
    rayDesc.Origin      = worldPos;
    rayDesc.Direction   = light.direction;
    rayDesc.TMin        = 1e-1f;

    float tMax = 0.0f;
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        tMax = length(_AdditionalLightsBuffer[lightIndex].position - worldPos);
    #else
        tMax = length(_AdditionalLightsPosition[lightIndex] - worldPos);
    #endif
    rayDesc.TMax = tMax;

    return TraceShadowRay(rayDesc);
}

#endif