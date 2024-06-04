#ifndef DDGI_INPUTS
#define DDGI_INPUTS

#define DDGI_2PI 6.2831853071795864f;  

#define PROBE_IRRADIANCE_TEXELS     6		// Texel Number in each direction (without border)
#define PROBE_DISTANCE_TEXELS       14		// Texel Number in each direction (without border)
#define BACKFACE_DEPTH_MULTIPLIER   -0.2f
#define MIN_WEIGHT                  0.0001f

#define DDGI_SKYLIGHT_MODE_SKYBOX_CUBEMAP	0
#define DDGI_SKYLIGHT_MODE_GRADIENT			1
#define DDGI_SKYLIGHT_MODE_COLOR			2
#define DDGI_SKYLIGHT_MODE_UNSUPPORTED		3

#define DDGI_PROBE_CLASSIFICATION_ON  1
#define DDGI_PROBE_CLASSIFICATION_OFF 0
#define DDGI_PROBE_STATE_ACTIVE		  0
#define DDGI_PROBE_STATE_INACTIVE	  1

#define DDGI_PROBE_RELOCATION_ON	1
#define DDGI_PROBE_RELOCATION_OFF	0

#define DDGI_PROBE_REDUCTION_ON		1
#define DDGI_PROBE_REDUCTION_OFF	0

// The number of fixed rays that are used by probe relocation and classification.
// These rays directions are always the same to produce temporally stable results.
#define RTXGI_DDGI_NUM_FIXED_RAYS 32

RWStructuredBuffer<float4> RayBuffer;

RWTexture2DArray<float4> _ProbeIrradiance;
RWTexture2DArray<float2> _ProbeDistance;
Texture2DArray<float4>   _ProbeIrradianceHistory;
Texture2DArray<float2>   _ProbeDistanceHistory;

#if defined(DDGI_VISUALIZATION) || defined(DDGI_RAYTRACING) || defined(FORWARD_USE_DDGI)
	Texture2DArray<float4>   _ProbeData;
#else
	RWTexture2DArray<float4> _ProbeData;
#endif

struct DirectionalLight
{
	float4 direction;
	float4 color;
};
StructuredBuffer<DirectionalLight> DirectionalLightBuffer;

struct PunctualLight
{
	float4 position;
	float4 color;
	float4 distanceAndSpotAttenuation;
	float4 spotDirection;
};
StructuredBuffer<PunctualLight> PunctualLightBuffer;

struct DDGIPayload
{
	float3 radiance;
	float  distance;

	// For recursive shadow ray tracing.
	bool isShadowPayload;
	bool isInShadow;

	// For ClosestHit Early Out.
	uint rayIndex;
};

// --------------------------
// Core Lighting Settings
// --------------------------
float3   _StartPosition;
float4   _ProbeRotation;
int      _RaysPerProbe;
float3   _ProbeSize;
int      _MaxRaysPerProbe;
uint3	 _ProbeCount;
float    _NormalBias;
float    _EnergyPreservation;
float3   _RandomVector;
float    _RandomAngle;
float	 _HistoryBlendWeight;

float _IndirectIntensity;
float _NormalBiasMultiplier;
float _ViewBiasMultiplier;
//float _BiasMultiplier;
//float _AxialDistanceMultiplier;

// --------------------------
// Probe Classification
// --------------------------
int DDGI_PROBE_CLASSIFICATION;

// --------------------------
// Probe Relocation
// --------------------------
int	  DDGI_PROBE_RELOCATION;
float _ProbeFixedRayBackfaceThreshold;
float _ProbeMinFrontfaceDistance;

// --------------------------
// Light Structures
// --------------------------
int _DirectionalLightCount;	 // 存储场景内所有Directional光源（不考虑剔除）
int _PunctualLightCount;	 // 存储场景内所有Spot和Point光源（不考虑剔除）

// --------------------------
// Sky Lights
// --------------------------
TEXTURECUBE(_SkyboxCubemap); SAMPLER(sampler_SkyboxCubemap);

int		DDGI_SKYLIGHT_MODE;
// Cubemap Skybox Material
float	_SkyboxIntensityMultiplier;
float4	_SkyboxTintColor;
float	_SkyboxExposure;
// Gradient
float4	_SkyColor;
float4	_EquatorColor;
float4	_GroundColor;
// Color
float4	_AmbientColor;

// --------------------------
// Probe Reduction
// --------------------------
uint3 _ReductionInputSize;
int	  DDGI_PROBE_REDUCTION;

#endif
