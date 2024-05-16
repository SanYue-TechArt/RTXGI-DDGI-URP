#ifndef DDGI_INPUTS
#define DDGI_INPUTS

#define PROBE_IRRADIANCE_TEXELS     6
#define PROBE_DISTANCE_TEXELS       14
#define BACKFACE_DEPTH_MULTIPLIER   -0.2f
#define MIN_WEIGHT                  0.0001f

#define DDGI_SKYLIGHT_MODE_SKYBOX_CUBEMAP	0
#define DDGI_SKYLIGHT_MODE_GRADIENT			1
#define DDGI_SKYLIGHT_MODE_COLOR			2
#define DDGI_SKYLIGHT_MODE_UNSUPPORTED		3

RWTexture2D<float4> _IrradianceTexture;
RWTexture2D<float2> _DistanceTexture;
Texture2D<float4> _IrradianceTextureHistory;
Texture2D<float2> _DistanceTextureHistory;

#if defined(DDGI_VISUALIZATION) || defined(DDGI_RAYTRACING) || defined(FORWARD_USE_DDGI)
	Texture2DArray<float4> _ProbeData;
#else
	RWTexture2DArray<float4> _ProbeData;
#endif

RWStructuredBuffer<float4> RayBuffer;

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
};

/*CBUFFER_START(DDGIVolumeGpu)
    float3   _StartPosition;
    int      _RaysPerProbe;
    float3   _ProbeSize;
    int      _MaxRaysPerProbe;
    float3   _ProbeCount;
    float    _NormalBias;
    float    _EnergyPreservation;
    float    _Pad0;
    float    _Pad1;
CBUFFER_END*/

float3   _StartPosition;
int      _RaysPerProbe;
float3   _ProbeSize;
int      _MaxRaysPerProbe;
uint3	 _ProbeCount;
float    _NormalBias;
float    _EnergyPreservation;
float3   _RandomVector;
float    _RandomAngle;
float	 _HistoryBlendWeight;

//float _BiasMultiplier;
float _IndirectIntensity;
float _NormalBiasMultiplier;
float _ViewBiasMultiplier;
//float _AxialDistanceMultiplier;

// For Probe Relocation.
float _ProbeFixedRayBackfaceThreshold;
float _ProbeMinFrontfaceDistance;

int _DirectionalLightCount;	 // 存储场景内所有Directional光源（不考虑剔除）
int _PunctualLightCount;	 // 存储场景内所有Spot和Point光源（不考虑剔除）

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

#endif
