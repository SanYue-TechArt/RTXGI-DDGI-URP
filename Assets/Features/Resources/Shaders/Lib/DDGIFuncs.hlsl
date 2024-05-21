#ifndef DDGI_FUNCS
#define DDGI_FUNCS

#include "Common/Packing.hlsl"

//------------------------------------------------------------------------
// Math Utility
//------------------------------------------------------------------------

float Min(float2 v) { return min(v.x, v.y); }
float Min(float3 v) { return min(Min(v.xy), v.z); }
float Min(float4 v) { return min(Min(v.xyz), v.w);}
float Max(float2 v) { return max(v.x, v.y); }
float Max(float3 v) { return max(Max(v.xy), v.z); }
float Max(float4 v) { return max(Max(v.xyz), v.w);}

float Pow2(float x) { return x * x; }
float Pow3(float x) { return x * x * x; }

// 输出旋转后的方向（弧度制）
// Reference: Unity Shader Graph
float3 RotateAboutAxisInRadians(float3 In, float3 Axis, float Rotation)
{
	float s = sin(Rotation);
	float c = cos(Rotation);
	float one_minus_c = 1.0 - c;

	Axis = normalize(Axis);
	float3x3 rot_mat =
	{   one_minus_c * Axis.x * Axis.x + c, one_minus_c * Axis.x * Axis.y - Axis.z * s, one_minus_c * Axis.z * Axis.x + Axis.y * s,
		one_minus_c * Axis.x * Axis.y + Axis.z * s, one_minus_c * Axis.y * Axis.y + c, one_minus_c * Axis.y * Axis.z - Axis.x * s,
		one_minus_c * Axis.z * Axis.x - Axis.y * s, one_minus_c * Axis.y * Axis.z + Axis.x * s, one_minus_c * Axis.z * Axis.z + c
	};
	return mul(rot_mat,  In);
}

// 输出旋转的矩阵（弧度制）
float3x3 AngleAxis3x3(float angle, float3 axis)
{
	// Rotation with angle (in radians) and axis
	float c, s;
	sincos(angle, s, c);

	float t = 1 - c;
	float x = axis.x;
	float y = axis.y;
	float z = axis.z;

	return float3x3(
		t * x * x + c, t * x * y - s * z, t * x * z + s * y,
		t * x * y + s * z, t * y * y + c, t * y * z - s * x,
		t * x * z - s * y, t * y * z + s * x, t * z * z + c
		);
}


//------------------------------------------------------------------------
// Randomize Functions
//------------------------------------------------------------------------

// Ray Tracing Gems 2: Essential Ray Generation Shaders
float3 SphericalFibonacci(float i, float n)
{
	const float PHI = sqrt(5) * 0.5f + 0.5f;
	float fraction	= (i * (PHI - 1)) - floor(i * (PHI - 1));
	float phi		= 2.0f * PI * fraction;
	float cosTheta	= 1.0f - (2.0f * i + 1.0f) * (1.0f / n);
	float sinTheta	= sqrt(saturate(1.0 - cosTheta * cosTheta));
	
	return float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
}

float3 DDGIGetProbeRayDirection(int rayIndex)
{
	bool isFixedRay = false;
	int sampleIndex = rayIndex;
	int numRays		= _RaysPerProbe;
	
	//if (volume.probeRelocationEnabled || volume.probeClassificationEnabled)
	if ((DDGI_PROBE_RELOCATION == DDGI_PROBE_RELOCATION_ON) || false)
	{
		isFixedRay  = (rayIndex < RTXGI_DDGI_NUM_FIXED_RAYS);
		sampleIndex = isFixedRay ? rayIndex : (rayIndex - RTXGI_DDGI_NUM_FIXED_RAYS);
		numRays		= isFixedRay ? RTXGI_DDGI_NUM_FIXED_RAYS : (numRays - RTXGI_DDGI_NUM_FIXED_RAYS);
	}

	// Get a ray direction on the sphere
	float3 direction = SphericalFibonacci(sampleIndex, numRays);

	// Don't rotate fixed rays so relocation/classification are temporally stable
	if (isFixedRay) return normalize(direction);

	// Apply Rotation
	float3 randomDirection = RotateAboutAxisInRadians(direction, _RandomVector, _RandomAngle);
	return normalize(randomDirection);
}


//------------------------------------------------------------------------
// Light Fetcher
//------------------------------------------------------------------------

Light GetDDGIDirectionalLight(int index)
{
	DirectionalLight directionalLight = DirectionalLightBuffer[index];

	Light light;
	light.direction				= directionalLight.direction.xyz;
	light.color					= directionalLight.color.rgb;
	light.distanceAttenuation	= 1.0f;
	light.shadowAttenuation		= 1.0f;
	light.layerMask				= 0;

	return light;
}

Light GetDDGIPunctualLight(int index, float3 positionWS)
{
	PunctualLight punctualLight = PunctualLightBuffer[index];
	float4 lightPositionWS = punctualLight.position;
	float3 color = punctualLight.color.rgb;
	float4 distanceAndSpotAttenuation = punctualLight.distanceAndSpotAttenuation;
	float4 spotDirection = punctualLight.spotDirection;
	
	float3 lightVector	= lightPositionWS.xyz - positionWS * lightPositionWS.w;
	float distanceSqr	= max(dot(lightVector, lightVector), HALF_MIN);

	half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
	float attenuation	 = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy) * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

	// 我们使用光线跟踪确定阴影，这里shadowAttenuation赋1
	Light light;
	light.direction				= lightDirection;
	light.distanceAttenuation	= attenuation;
	light.shadowAttenuation		= 1.0;
	light.color					= color.rgb;
	light.layerMask				= 0;

	return light;
}


//------------------------------------------------------------------------
// Probe Data Fetcher
//------------------------------------------------------------------------

#if defined(DDGI_VISUALIZATION) || defined(DDGI_RAYTRACING) || defined(FORWARD_USE_DDGI)
	float3 DDGILoadProbeDataOffset(uint3 coords)
	{
		return LOAD_TEXTURE2D_ARRAY_LOD(_ProbeData, coords.xy, coords.z, 0).xyz * _ProbeSize;	
	}
#else
	// We use Texture2DArray in visualization, Texture2DArray dont support these function.
	float3 DDGILoadProbeDataOffset(uint3 coords)
	{
		return _ProbeData[coords].xyz * _ProbeSize;
	}

	void DDGIStoreProbeDataOffset(uint3 coords, float3 wsOffset)
	{
		// A-Component is useless now.
		_ProbeData[coords] = float4(wsOffset / _ProbeSize, 1.0f);
	}
#endif


//------------------------------------------------------------------------
// Probe World Position
//------------------------------------------------------------------------

// 根据probe的三维网格坐标获取其世界空间位置（不考虑Relocation）
float3 DDGIGetProbeWorldPosition(uint3 gridCoord)
{
	return _StartPosition + _ProbeSize * gridCoord;
}

// 根据probe的一维网格索引获取其世界空间位置（不考虑Relocation）
float3 DDGIGetProbeWorldPosition(uint probeIndex)
{
	uint3 gridCoord = DDGIGetProbeCoords(probeIndex);
	return DDGIGetProbeWorldPosition(gridCoord);
}

// 根据probe的三维网格坐标获取其世界空间位置（考虑Relocation）
float3 DDGIGetRelocatedProbeWorldPosition(int3 probeCoords)
{
	float3 probeWorldPosition = DDGIGetProbeWorldPosition(probeCoords);

	int probeIndex		= DDGIGetProbeIndex(probeCoords);
	uint3 coords		= DDGIGetProbeTexelCoordsOneByOne(probeIndex);
	probeWorldPosition	+= DDGILoadProbeDataOffset(coords);

	return probeWorldPosition;
}

// 根据probe的一维网格索引获取其世界空间位置（考虑Relocation）
float3 DDGIGetRelocatedProbeWorldPosition(int probeIndex)
{
	int3 probeCoords = DDGIGetProbeCoords(probeIndex);
	return DDGIGetRelocatedProbeWorldPosition(probeCoords);
}

// 接受一个世界空间位置P，返回与该位置相关的基准probe网格坐标（用于确定P所在的Probe网格块）
uint3 DDGIGetBaseGridCoords(float3 worldPos)
{
	return clamp(uint3((worldPos - _StartPosition) / _ProbeSize), uint3(0, 0, 0), uint3(_ProbeCount) - uint3(1, 1, 1));
}

//------------------------------------------------------------------------
// Runtime Probe Sampling
//------------------------------------------------------------------------

float3 ComputeBias(float3 normal, float3 viewDirection, float b = 0.2f)
{
	#if 0
		// Arida Implementation.
	    const float normalBiasMultiplier = 0.2f;
	    const float viewBiasMultiplier = 0.8f;
	    const float axialDistanceMultiplier = 0.75f;
	    return (normal * normalBiasMultiplier + viewDirection * viewBiasMultiplier) * axialDistanceMultiplier * Min(_ProbeSize) * b;
	#else
		// NVIDIA Implementation.
		return (normal * _NormalBiasMultiplier + viewDirection * _ViewBiasMultiplier);
	#endif
}

//https://github.com/simco50/D3D12_Research/blob/master/D3D12/Resources/Shaders/RayTracing/DDGICommon.hlsli
float3 SampleDDGIIrradiance(float3 P, float3 N, float3 Wo)
{
	float3 direction		= N;
	float3 biasedPosition	= P;
	float3 unbiasedPosition = P;
	float  volumeWeight		= 1.0f;

	biasedPosition += ComputeBias(direction, -Wo);

	// 当着色点位于Volume区域外，我们将提前返回
	// 当着色点逼近Volume边界（但没有超出volume区域），我们对其辐照度进行平滑过渡
	const float3 relativeCoordinates = (biasedPosition - _StartPosition) / _ProbeSize;
	for(uint i = 0; i < 3; ++i)
	{
		volumeWeight *= lerp(0, 1, saturate(relativeCoordinates[i]));
		if(relativeCoordinates[i] > _ProbeCount[i] - 2)
		{
			const float x = saturate(relativeCoordinates[i] - _ProbeCount[i] + 2);
			volumeWeight  *= lerp(1, 0, x);
		}
	}
	if(volumeWeight <= 0.0f) return 0.0f;

	// 计算relativeCoordinates时就需要偏移position（参考NVIDIA）
	// 如果在这里才偏移position（Arida的实现）会导致trilinear插值出现网格瑕疵
	//position += ComputeBias(direction, -Wo);

	const uint3  baseProbeCoords	= floor(relativeCoordinates);
	const float3 baseProbePosition	= DDGIGetProbeWorldPosition(baseProbeCoords);
	const float3 alpha				= saturate((biasedPosition - baseProbePosition) / _ProbeSize);

	float3 sumIrradiance = 0;
	float  sumWeight	 = 0;

	for (uint j = 0; j < 8; ++j)
	{
		uint3 indexOffset = uint3(j, j >> 1u, j >> 2u) & 1u;

		const uint3 probeCoords = clamp(baseProbeCoords + indexOffset, 0, _ProbeCount - 1);

		float3 probePosition = 0.0f;
		if (DDGI_PROBE_RELOCATION == DDGI_PROBE_RELOCATION_OFF)
			probePosition = DDGIGetProbeWorldPosition(probeCoords);
		else
			probePosition = DDGIGetRelocatedProbeWorldPosition(probeCoords);

		uint probeIndex = DDGIGetProbeIndex(probeCoords);

		float3 relativeProbePosition = biasedPosition - probePosition;
		float3 probeDirection		 = -normalize(relativeProbePosition);

		float3 trilinear		= max(0.001f, lerp(1.0f - alpha, alpha, indexOffset));
        float trilinearWeight	= (trilinear.x * trilinear.y * trilinear.z);

		float weight = 1.0f;

		// --------------------------------
		// Backface Weighting
		// --------------------------------
		#if 0
			// Arida Implementation.
			weight *= saturate(dot(probeDirection, direction));
		#else
			// NVIDIA Implementation.
			const float wrapShading = dot(normalize(probePosition - unbiasedPosition), direction) * 0.5f + 0.5f;
			weight *= (wrapShading * wrapShading) + 0.2f;
		#endif

		// --------------------------------
		// Chebyshev Visibility Test
		// --------------------------------
		float3 probeDistanceUV	= DDGIGetProbeUV(probeIndex, -probeDirection, PROBE_DISTANCE_TEXELS);
		float  probeDistance	= length(relativeProbePosition);
		// https://developer.download.nvidia.com/SDK/10/direct3d/Source/VarianceShadowMapping/Doc/VarianceShadowMapping.pdf
		float2 moments = SAMPLE_TEXTURE2D_ARRAY_LOD(_ProbeDistanceHistory, sampler_LinearClamp, probeDistanceUV.xy, probeDistanceUV.z, 0).xy;
		float variance = abs(Pow2(moments.x) - moments.y);
		float chebyshev = 1.0f;
		if(probeDistance > moments.x)
		{
			float mD = moments.x - probeDistance;
			chebyshev = variance / (variance + Pow2(mD));
			chebyshev = max(Pow3(chebyshev), 0.0);
		}
		weight *= max(chebyshev, 0.05f);
		
		weight = max(0.000001f, weight);

		// --------------------------------
		// Threshold and Trilinear Weight.
		// --------------------------------
		const float crushThreshold = 0.2f;
		if (weight < crushThreshold)
		{
			weight *= weight * weight * (1.0f / Pow2(crushThreshold));
		}
		weight *= trilinearWeight;

		float3 probeIrradianceUV = DDGIGetProbeUV(probeIndex, direction, PROBE_IRRADIANCE_TEXELS);
		float3 irradiance		 = SAMPLE_TEXTURE2D_ARRAY_LOD(_ProbeIrradianceHistory, sampler_LinearClamp, probeIrradianceUV.xy, probeIrradianceUV.z, 0).rgb;
		irradiance				 = pow(irradiance, 2.5f); // Gamma Correct.

		sumIrradiance += irradiance * weight;
		sumWeight	  += weight;
	}
	
	if(sumWeight == 0) return 0.0f;

	sumIrradiance *= (1.0f / sumWeight);
	sumIrradiance *= sumIrradiance;
	// 我们在外面使用的DiffuseBRDF没有除PI（LambertNoPI），所以这里不需要再乘
	//sumIrradiance *= PI;
	sumIrradiance *= _IndirectIntensity;
	
	return sumIrradiance * volumeWeight;
}

#endif