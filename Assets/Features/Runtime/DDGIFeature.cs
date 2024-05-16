using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Random = System.Random;

public sealed class DDGIFeature : ScriptableRendererFeature
{
    public sealed class DDGIPass : ScriptableRenderPass
    {
        private bool mIsInitialized = false;

        private bool mNeedToReset = true;
        private bool mNeedToResetProbeRelocation = true;

        private static readonly int PROBE_IRRADIANCE_TEXELS = 6;
        private static readonly int PROBE_DISTANCE_TEXELS = 14;

        private RayTracingShader mDDGIRayTraceShader;
        private RayTracingAccelerationStructure mAccelerationStructure;

        private readonly ComputeShader mUpdateIrradianceCS;
        private readonly int mUpdateIrradianceKernel;
        private readonly ComputeShader mUpdateDistanceCS;
        private readonly int mUpdateDistanceKernel;
        private readonly ComputeShader mRelocateProbeCS;
        private readonly int mResetRelocationKernel;
        private readonly int mRelocateProbeKernel;

        private readonly Shader mCubemapSkyPS;

        private DDGI mddgiOverride;

        struct DDGIVolumeCpu
        {
            public Vector3 Origin;
            public Vector3 Extents;
            public Vector3Int NumProbes;
            public int MaxNumRays;
            public int NumRays;
        }
        private DDGIVolumeCpu mDDGIVolumeCpu;

        private RenderTexture mIrradiance;
        private RenderTargetIdentifier mIrradianceId;
        private RenderTexture mDistance;
        private RenderTargetIdentifier mDistanceId;
        private RenderTexture mIrradianceHistory;
        private RenderTargetIdentifier mIrradianceHistoryId;
        private RenderTexture mDistanceHistory;
        private RenderTargetIdentifier mDistanceHistoryId;
        private RenderTexture mProbeData; // For Relocate Probe
        private RenderTargetIdentifier mProbeDataId;
        
        private ComputeBuffer mRayBuffer;
        private ComputeBuffer mDirectionalLightBuffer;
        private ComputeBuffer mPunctualLightBuffer;

        // 用于支持多个定向光的情况
        private struct DirectionalLight
        {
            public Vector4 direction;
            public Vector4 color;
        }

        // Reference: RealtimeLights.hlsl 153
        // 注：我们认定点光源、聚光灯和面光源是精确光，定向光不在此列
        private struct PunctualLight
        {
            public Vector4 position;
            public Vector4 color;
            public Vector4 distanceAndSpotAttenuation;
            public Vector4 spotDirection;
        }

        /*[GenerateHLSL(needAccessors = false, generateCBuffer = true)]
        unsafe struct DDGIVolumeGpu
        {
            public Vector3 _StartPosition;
            public int _RaysPerProbe;
            public Vector3 _ProbeSize;
            public int _MaxRaysPerProbe;
            public Vector3Int _ProbeCount;
            public float _NormalBias;
            public float _EnergyPreservation;
            public float _Pad0;
            public float _Pad1;
        }
        private ConstantBuffer<DDGIVolumeGpu> mDDGIVolumeGpuCB;*/

        private static class GpuParams
        {
            public static readonly int RayBuffer = Shader.PropertyToID("RayBuffer");
            public static readonly int DirectionalLightBuffer = Shader.PropertyToID("DirectionalLightBuffer");
            public static readonly int PunctualLightBuffer = Shader.PropertyToID("PunctualLightBuffer");
            public static readonly string RayGenShaderName = "DDGI_RayGen"; 
            
            /// <summary>
            /// 为了避免ConstantBuffer<>可能出现的问题，我们先直接传递常量（即视为场景内只有一个DDGI Volume）
            /// </summary>
            public static readonly int DDGIVolumeGpu = Shader.PropertyToID("DDGIVolumeGpu"); 
            public static readonly int _StartPosition = Shader.PropertyToID("_StartPosition"); 
            public static readonly int _RaysPerProbe = Shader.PropertyToID("_RaysPerProbe"); 
            public static readonly int _ProbeSize = Shader.PropertyToID("_ProbeSize"); 
            public static readonly int _MaxRaysPerProbe = Shader.PropertyToID("_MaxRaysPerProbe"); 
            public static readonly int _ProbeCount = Shader.PropertyToID("_ProbeCount"); 
            public static readonly int _NormalBias = Shader.PropertyToID("_NormalBias"); 
            public static readonly int _IndirectIntensity = Shader.PropertyToID("_IndirectIntensity"); 
            public static readonly int _BiasMultiplier = Shader.PropertyToID("_BiasMultiplier"); 
            public static readonly int _NormalBiasMultiplier = Shader.PropertyToID("_NormalBiasMultiplier"); 
            public static readonly int _ViewBiasMultiplier = Shader.PropertyToID("_ViewBiasMultiplier"); 
            public static readonly int _AxialDistanceMultiplier = Shader.PropertyToID("_AxialDistanceMultiplier"); 
            public static readonly int _EnergyPreservation = Shader.PropertyToID("_EnergyPreservation");
            public static readonly int _HistoryBlendWeight = Shader.PropertyToID("_HistoryBlendWeight");
            public static readonly int _DirectionalLightCount = Shader.PropertyToID("_DirectionalLightCount");
            public static readonly int _PunctualLightCount = Shader.PropertyToID("_PunctualLightCount");

            public static readonly int _IrradianceTexture = Shader.PropertyToID("_IrradianceTexture");
            public static readonly int _DistanceTexture = Shader.PropertyToID("_DistanceTexture");
            public static readonly int _IrradianceTextureHistory = Shader.PropertyToID("_IrradianceTextureHistory");
            public static readonly int _DistanceTextureHistory = Shader.PropertyToID("_DistanceTextureHistory");
            public static readonly int _ProbeData = Shader.PropertyToID("_ProbeData");
            
            public static readonly int _AccelerationStructure = Shader.PropertyToID("_AccelerationStructure");
            public static readonly int _RandomVector = Shader.PropertyToID("_RandomVector");
            public static readonly int _RandomAngle = Shader.PropertyToID("_RandomAngle");
            
            public static readonly int _ProbeFixedRayBackfaceThreshold = Shader.PropertyToID("_ProbeFixedRayBackfaceThreshold");
            public static readonly int _ProbeMinFrontfaceDistance = Shader.PropertyToID("_ProbeMinFrontfaceDistance");
            
            // Keywords
            public static readonly string DDGI_SHOW_INDIRECT_ONLY = "DDGI_SHOW_INDIRECT_ONLY";
            public static readonly string DDGI_SHOW_PURE_INDIRECT_RADIANCE = "DDGI_SHOW_PURE_INDIRECT_RADIANCE";
            
            // For Sky Lighting
            public static readonly string DDGI_SKYLIGHT_MODE = "DDGI_SKYLIGHT_MODE";
            // For Sky Lighting - Cubemap Skybox Material [Only]
            public static readonly int _SkyboxCubemap = Shader.PropertyToID("_SkyboxCubemap");
            public static readonly int _SkyboxIntensityMultiplier = Shader.PropertyToID("_SkyboxIntensityMultiplier");
            public static readonly int _SkyboxTintColor = Shader.PropertyToID("_SkyboxTintColor");
            public static readonly int _SkyboxExposure = Shader.PropertyToID("_SkyboxExposure");
            // For Sky Lighting - Gradient
            public static readonly int _SkyColor = Shader.PropertyToID("_SkyColor");
            public static readonly int _EquatorColor = Shader.PropertyToID("_EquatorColor");
            public static readonly int _GroundColor = Shader.PropertyToID("_GroundColor");
            // For Sky Lighting - Color
            public static readonly int _AmbientColor = Shader.PropertyToID("_AmbientColor");
        }

        public DDGIPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            
            //mDDGIVolumeGpuCB = new ConstantBuffer<DDGIVolumeGpu>();

            mDDGIRayTraceShader = Resources.Load<RayTracingShader>("Shaders/DDGIRayTracing");
            mUpdateIrradianceCS = Resources.Load<ComputeShader>("Shaders/DDGIUpdateIrradiance");
            mUpdateIrradianceKernel = mUpdateIrradianceCS.FindKernel("DDGIUpdateIrradiance");
            mUpdateDistanceCS = Resources.Load<ComputeShader>("Shaders/DDGIUpdateDistance");
            mUpdateDistanceKernel = mUpdateDistanceCS.FindKernel("DDGIUpdateDistance");
            mRelocateProbeCS = Resources.Load<ComputeShader>("Shaders/DDGIRelocateProbe");
            mResetRelocationKernel = mRelocateProbeCS.FindKernel("DDGIResetRelocation");
            mRelocateProbeKernel = mRelocateProbeCS.FindKernel("DDGIRelocateProbe");

            RayTracingAccelerationStructure.RASSettings setting = new RayTracingAccelerationStructure.RASSettings
                (RayTracingAccelerationStructure.ManagementMode.Automatic, RayTracingAccelerationStructure.RayTracingModeMask.Everything,  255);
            mAccelerationStructure = new RayTracingAccelerationStructure(setting);
            
            // Shader.Find不稳健，Shader在打包后可能出现丢失的情况，此时用Find是无效的
            // 出于演示目的在此摆烂
            mCubemapSkyPS = Shader.Find("Skybox/Cubemap");
        }

        public void Reinitialize()
        {
            mIsInitialized = false;
            mNeedToReset = true;
            mNeedToResetProbeRelocation = true;
        }

        public void Release()
        {
            /*if (mDDGIVolumeGpuCB != null)
            {
                mDDGIVolumeGpuCB.Release();
                mDDGIVolumeGpuCB = null;
            }*/

            if (mIrradianceHistory != null) { mIrradianceHistory.Release(); mIrradianceHistory = null; }

            if (mDistanceHistory != null) { mDistanceHistory.Release(); mDistanceHistory = null; }

            if (mIrradiance != null) { mIrradiance.Release(); mIrradiance = null; }

            if (mDistance != null) { mDistance.Release(); mDistance = null; }

            if (mAccelerationStructure != null) { mAccelerationStructure.Release(); mAccelerationStructure = null; }

            if (mRayBuffer != null) { mRayBuffer.Release(); mRayBuffer = null; }

            if (mDirectionalLightBuffer != null) { mDirectionalLightBuffer.Release(); mDirectionalLightBuffer = null; }

            if (mPunctualLightBuffer != null) { mPunctualLightBuffer.Release(); mPunctualLightBuffer = null; }
        }

        /// <summary>
        /// 一些对外接口，主要用于Debug，没有Runtime意义
        /// </summary>
        public bool IsInitialized() => mIsInitialized;
        public Vector3 GetBoundsCenter() => mDDGIVolumeCpu.Origin;
        public Vector3 GetBoundsExtents() => mDDGIVolumeCpu.Extents;
        public ComputeBuffer GetRayBuffer() => mRayBuffer;
        public Vector3Int GetNumProbes() => mDDGIVolumeCpu.NumProbes;
        public RenderTargetIdentifier GetProbeData() => mProbeDataId;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            
            mddgiOverride = VolumeManager.instance.stack.GetComponent<DDGI>();

            Initialize();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (mddgiOverride == null || !mIsInitialized) return;
            if (!mddgiOverride.IsActive()) return;

            var cmd = CommandBufferPool.Get("DDGI Pass");
            var camera = renderingData.cameraData.camera;
            var renderer = renderingData.cameraData.renderer;

            int numProbesFlat = mDDGIVolumeCpu.NumProbes.x * mDDGIVolumeCpu.NumProbes.y * mDDGIVolumeCpu.NumProbes.z;
            var random = (float)NextDouble(new Random(), 0.0f, 1.0f, 5); // 生成0-1中的随机数，小数保留5位
            var randomVec = Vector3.Normalize(new Vector3(2.0f * random - 1.0f, 2.0f * random - 1.0f, 2.0f * random - 1.0f));
            var randomAngle = random * Mathf.PI * 2.0f;
            
            if (mNeedToReset)
            {
                CoreUtils.SetRenderTarget(cmd, mIrradianceHistory, ClearFlag.Color, new Color(0,0,0,0));
                CoreUtils.SetRenderTarget(cmd, mDistanceHistory, ClearFlag.Color, new Color(0,0,0,0));
                mNeedToReset = false;
            }

            using (new ProfilingScope(cmd, new ProfilingSampler("DDGI Ray Trace Pass")))
            {
                UpdateDDGIGpuParameters(cmd);
                
                // TODO: RayBuffer持久化，无需逐帧申请
                if(mRayBuffer != null) { mRayBuffer.Release(); mRayBuffer = null; }
                mRayBuffer = new ComputeBuffer(numProbesFlat * mDDGIVolumeCpu.MaxNumRays, 16 /* float4 */, ComputeBufferType.Default);

                BuildLightStructuredBuffer(cmd);
                
                cmd.BuildRayTracingAccelerationStructure(mAccelerationStructure);
                cmd.SetRayTracingAccelerationStructure(mDDGIRayTraceShader, GpuParams._AccelerationStructure, mAccelerationStructure);
                
                cmd.SetRayTracingShaderPass(mDDGIRayTraceShader, "DDGIRayTracing");
                cmd.SetGlobalTexture(GpuParams._IrradianceTextureHistory, mIrradianceHistoryId);
                cmd.SetGlobalTexture(GpuParams._DistanceTextureHistory, mDistanceHistoryId);
                cmd.SetGlobalTexture(GpuParams._ProbeData, mProbeDataId);

                cmd.SetRayTracingBufferParam(mDDGIRayTraceShader, GpuParams.RayBuffer, mRayBuffer);
                cmd.SetGlobalBuffer(GpuParams.DirectionalLightBuffer, mDirectionalLightBuffer);     // We will use it in closest hit shader, not in actual .raytrace shader
                cmd.SetGlobalBuffer(GpuParams.PunctualLightBuffer, mPunctualLightBuffer);           // We will use it in closest hit shader, not in actual .raytrace shader
                cmd.SetRayTracingVectorParam(mDDGIRayTraceShader, GpuParams._RandomVector, randomVec);
                cmd.SetRayTracingFloatParam(mDDGIRayTraceShader, GpuParams._RandomAngle, randomAngle);
                
                UpdateSkyLight(cmd);

                // 实际调度的光线数量按本帧预算为准，若按照22*22*22*144的设置，总计1,533,312（153万）条光线，相当于1080p-1spp光线跟踪的73%，该设置能得到非常平滑的效果，实际可以更低
                cmd.DispatchRays(mDDGIRayTraceShader, GpuParams.RayGenShaderName, (uint)mDDGIVolumeCpu.NumRays, (uint)numProbesFlat, 1, camera);
            }

            using (new ProfilingScope(cmd, new ProfilingSampler("DDGI Update Irradiance Pass")))
            {
                UpdateDDGIGpuParameters(cmd);
                
                cmd.SetComputeBufferParam(mUpdateIrradianceCS, mUpdateIrradianceKernel, GpuParams.RayBuffer, mRayBuffer);
                cmd.SetComputeTextureParam(mUpdateIrradianceCS, mUpdateIrradianceKernel, GpuParams._IrradianceTexture, mIrradianceId);
                cmd.SetComputeTextureParam(mUpdateIrradianceCS, mUpdateIrradianceKernel, GpuParams._IrradianceTextureHistory, mIrradianceHistoryId);
                cmd.SetComputeVectorParam(mUpdateIrradianceCS, GpuParams._RandomVector, randomVec);
                cmd.SetComputeFloatParam(mUpdateIrradianceCS, GpuParams._RandomAngle, randomAngle);

                cmd.DispatchCompute(mUpdateIrradianceCS, mUpdateIrradianceKernel, numProbesFlat, 1, 1);
            }

            using (new ProfilingScope(cmd, new ProfilingSampler("DDGI Update Distance Pass")))
            {
                UpdateDDGIGpuParameters(cmd);
                
                cmd.SetComputeBufferParam(mUpdateDistanceCS, mUpdateDistanceKernel, GpuParams.RayBuffer, mRayBuffer);
                cmd.SetComputeTextureParam(mUpdateDistanceCS, mUpdateDistanceKernel, GpuParams._DistanceTexture, mDistanceId);
                cmd.SetComputeTextureParam(mUpdateDistanceCS, mUpdateDistanceKernel, GpuParams._DistanceTextureHistory, mDistanceHistoryId);
                cmd.SetComputeVectorParam(mUpdateDistanceCS, GpuParams._RandomVector, randomVec);
                cmd.SetComputeFloatParam(mUpdateDistanceCS, GpuParams._RandomAngle, randomAngle);
                
                cmd.DispatchCompute(mUpdateDistanceCS, mUpdateIrradianceKernel, numProbesFlat, 1, 1);
            }

            if (true)
            {
                using (new ProfilingScope(cmd, new ProfilingSampler("DDGI Relocate Probe Pass")))
                {
                    const float groupSizeX = 32.0f;
                    int numGroupsX = (int)Mathf.Ceil(numProbesFlat / groupSizeX);

                    if (mNeedToResetProbeRelocation)
                    {
                        UpdateDDGIGpuParameters(cmd);
                        cmd.SetComputeVectorParam(mRelocateProbeCS, GpuParams._RandomVector, randomVec);
                        cmd.SetComputeFloatParam(mRelocateProbeCS, GpuParams._RandomAngle, randomAngle);
                        cmd.SetComputeTextureParam(mRelocateProbeCS, mResetRelocationKernel, GpuParams._ProbeData, mProbeDataId);
                        cmd.DispatchCompute(mRelocateProbeCS, mResetRelocationKernel, numGroupsX, 1, 1);
                        mNeedToResetProbeRelocation = false;
                    }
                    
                    UpdateDDGIGpuParameters(cmd);
                    
                    cmd.SetComputeTextureParam(mRelocateProbeCS, mRelocateProbeKernel, GpuParams._ProbeData, mProbeDataId);
                    cmd.SetComputeVectorParam(mRelocateProbeCS, GpuParams._RandomVector, randomVec);
                    cmd.SetComputeFloatParam(mRelocateProbeCS, GpuParams._RandomAngle, randomAngle);
                    cmd.SetComputeBufferParam(mRelocateProbeCS, mRelocateProbeKernel, GpuParams.RayBuffer, mRayBuffer);
                    cmd.DispatchCompute(mRelocateProbeCS, mRelocateProbeKernel, numGroupsX, 1, 1);
                }
            }

            // 两种Swap的方式
            /*(mIrradianceId, mIrradianceHistoryId) = (mIrradianceHistoryId, mIrradianceId);
            (mDistanceId, mDistanceHistoryId) = (mDistanceHistoryId, mDistanceId);*/
            cmd.CopyTexture(mIrradianceId, mIrradianceHistoryId);
            cmd.CopyTexture(mDistanceId, mDistanceHistoryId);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void Initialize()
        {
            if (mIsInitialized || mddgiOverride == null) return;
            
            // Initialize cpu-side volume parameters
            var sceneBoundingBox = GenerateSceneMeshBounds();
            if (sceneBoundingBox.extents == Vector3.zero) return;   // 包围盒零值表示场景没有任何几何体，没有GI意义
            mDDGIVolumeCpu.Origin = sceneBoundingBox.center;
            mDDGIVolumeCpu.Extents = 1.1f * sceneBoundingBox.extents;
            mDDGIVolumeCpu.NumProbes = new Vector3Int(mddgiOverride.probeCountX.value, mddgiOverride.probeCountY.value, mddgiOverride.probeCountZ.value);
            mDDGIVolumeCpu.NumRays = mddgiOverride.raysPerProbe.value;
            mDDGIVolumeCpu.MaxNumRays = 512;
            
            // 注：尽量使用GraphicsFormat来提供明确的浮点数 / 定点数指认
            // 比如Distance Texture，先前使用RenderTextureFormat.RG32，该格式使用的是16-bit无符号定点数，但距离需要是浮点数
            // 申请RG32作为Distance Texture会忽略距离信息的小数位，会导致切比雪夫可见性测试发生Edge Clamp Artifacts.
            
            // Create irradiance history
            var irradianceDimensions = GetProbeTextureDimensions(mDDGIVolumeCpu.NumProbes, PROBE_IRRADIANCE_TEXELS);
            if(mIrradianceHistory != null) { mIrradianceHistory.Release(); mIrradianceHistory = null; }
            mIrradianceHistory = new RenderTexture(irradianceDimensions.x, irradianceDimensions.y, 0, 
                GraphicsFormat.R16G16B16A16_SFloat);
            mIrradianceHistory.filterMode = FilterMode.Bilinear;
            mIrradianceHistory.useMipMap = false;
            mIrradianceHistory.autoGenerateMips = false;
            mIrradianceHistory.enableRandomWrite = true;
            mIrradianceHistory.name = "DDGI Irradiance History";
            mIrradianceHistory.Create();
            mIrradianceHistoryId = new RenderTargetIdentifier(mIrradianceHistory);

            // Create distance history
            var distanceDimensions = GetProbeTextureDimensions(mDDGIVolumeCpu.NumProbes, PROBE_DISTANCE_TEXELS);
            if(mDistanceHistory != null) { mDistanceHistory.Release(); mDistanceHistory = null; }
            mDistanceHistory = new RenderTexture(distanceDimensions.x, distanceDimensions.y, 0,
                GraphicsFormat.R16G16_SFloat);
            mDistanceHistory.filterMode = FilterMode.Bilinear;
            mDistanceHistory.useMipMap = false;
            mDistanceHistory.autoGenerateMips = false;
            mDistanceHistory.enableRandomWrite = true;
            mDistanceHistory.name = "DDGI Distance History";
            mDistanceHistory.Create();
            mDistanceHistoryId = new RenderTargetIdentifier(mDistanceHistory);
            
            // Create irradiance
            if(mIrradiance != null) { mIrradiance.Release(); mIrradiance = null; }
            mIrradiance = new RenderTexture(irradianceDimensions.x, irradianceDimensions.y, 0,
                GraphicsFormat.R16G16B16A16_SFloat);
            mIrradiance.filterMode = FilterMode.Bilinear;
            mIrradiance.useMipMap = false;
            mIrradiance.autoGenerateMips = false;
            mIrradiance.enableRandomWrite = true;
            mIrradiance.name = "DDGI Radiance";
            mIrradiance.Create();
            mIrradianceId = new RenderTargetIdentifier(mIrradiance);

            // Create distance
            if(mDistance != null) { mDistance.Release(); mDistance = null; }
            mDistance = new RenderTexture(distanceDimensions.x, distanceDimensions.y, 0,
                GraphicsFormat.R16G16_SFloat);
            mDistance.filterMode = FilterMode.Bilinear;
            mDistance.useMipMap = false;
            mDistance.autoGenerateMips = false;
            mDistance.enableRandomWrite = true;
            mDistance.name = "DDGI Distance";
            mDistance.Create();
            mDistanceId = new RenderTargetIdentifier(mDistance);
            
            // Create Probe Data
            if(mProbeData != null) { mProbeData.Release(); mProbeData = null; }
            var probeDataDimensions = GetProbeDataDimensions(mDDGIVolumeCpu.NumProbes);
            mProbeData = new RenderTexture(probeDataDimensions.x, probeDataDimensions.y, 0,
                GraphicsFormat.R16G16B16A16_SFloat);
            mProbeData.filterMode = FilterMode.Bilinear;
            mProbeData.useMipMap = false;
            mProbeData.autoGenerateMips = false;
            mProbeData.enableRandomWrite = true;
            mProbeData.name = "DDGI Probe Data";
            mProbeData.dimension = TextureDimension.Tex2DArray;
            mProbeData.volumeDepth = probeDataDimensions.z;
            mProbeData.Create();
            mProbeDataId = new RenderTargetIdentifier(mProbeData);

            mIsInitialized = true;
        }

        private void UpdateDDGIGpuParameters(CommandBuffer cmd)
        {
            /*// TODO: Support more ddgi volumes.
            DDGIVolumeGpu ddgiVolumeGpu;

            ddgiVolumeGpu._StartPosition = mDDGIVolumeCpu.Origin - mDDGIVolumeCpu.Extents;
            var a = 2.0f * mDDGIVolumeCpu.Extents;
            var b = new Vector3(mDDGIVolumeCpu.NumProbes.x, mDDGIVolumeCpu.NumProbes.y, mDDGIVolumeCpu.NumProbes.z) - Vector3.one;
            ddgiVolumeGpu._ProbeSize = new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
            ddgiVolumeGpu._RaysPerProbe = mDDGIVolumeCpu.NumRays;
            ddgiVolumeGpu._MaxRaysPerProbe = mDDGIVolumeCpu.MaxNumRays;
            ddgiVolumeGpu._ProbeCount = new Vector3Int(mDDGIVolumeCpu.NumProbes.x, mDDGIVolumeCpu.NumProbes.y, mDDGIVolumeCpu.NumProbes.z);
            ddgiVolumeGpu._NormalBias = 0.25f;
            ddgiVolumeGpu._EnergyPreservation = 0.85f;
            ddgiVolumeGpu._Pad0 = 0.0f;
            ddgiVolumeGpu._Pad1 = 0.0f;
            
            mDDGIVolumeGpuCB.PushGlobal(cmd, ddgiVolumeGpu, GpuParams.DDGIVolumeGpu);*/

            cmd.SetGlobalVector(GpuParams._StartPosition, mDDGIVolumeCpu.Origin - mDDGIVolumeCpu.Extents);
            var a = 2.0f * mDDGIVolumeCpu.Extents;
            var b = new Vector3(mDDGIVolumeCpu.NumProbes.x, mDDGIVolumeCpu.NumProbes.y, mDDGIVolumeCpu.NumProbes.z) - Vector3.one;
            cmd.SetGlobalVector(GpuParams._ProbeSize, new Vector4(a.x / b.x, a.y / b.y, a.z / b.z, 0.0f));
            cmd.SetGlobalInt(GpuParams._RaysPerProbe, mDDGIVolumeCpu.NumRays);
            cmd.SetGlobalInt(GpuParams._MaxRaysPerProbe, mDDGIVolumeCpu.MaxNumRays);
            cmd.SetGlobalVector(GpuParams._ProbeCount, new Vector4(mDDGIVolumeCpu.NumProbes.x, mDDGIVolumeCpu.NumProbes.y, mDDGIVolumeCpu.NumProbes.z, 0.0f));
            cmd.SetGlobalFloat(GpuParams._NormalBias, 0.25f);
            cmd.SetGlobalFloat(GpuParams._EnergyPreservation, 0.85f);
            cmd.SetGlobalFloat(GpuParams._HistoryBlendWeight, 0.98f);
            
            //cmd.SetGlobalFloat(GpuParams._BiasMultiplier, mddgiOverride.biasMultiplier.value);
            cmd.SetGlobalFloat(GpuParams._IndirectIntensity, mddgiOverride.indirectIntensity.value);
            cmd.SetGlobalFloat(GpuParams._NormalBiasMultiplier, mddgiOverride.normalBiasMultiplier.value);
            cmd.SetGlobalFloat(GpuParams._ViewBiasMultiplier, mddgiOverride.viewBiasMultiplier.value);
            //cmd.SetGlobalFloat(GpuParams._AxialDistanceMultiplier, mddgiOverride.axialDistanceMultiplier.value);
            
            cmd.SetGlobalFloat(GpuParams._ProbeFixedRayBackfaceThreshold, mddgiOverride.probeFixedRayBackfaceThreshold.value);
            cmd.SetGlobalFloat(GpuParams._ProbeMinFrontfaceDistance, mddgiOverride.probeMinFrontfaceDistance.value);

            cmd.DisableShaderKeyword(GpuParams.DDGI_SHOW_INDIRECT_ONLY);
            cmd.DisableShaderKeyword(GpuParams.DDGI_SHOW_PURE_INDIRECT_RADIANCE);
            if (mddgiOverride.debugIndirect.value)
            {
                switch (mddgiOverride.indirectDebugMode.value)
                {
                    case IndirectDebugMode.FullIndirectRadiance:
                        cmd.EnableShaderKeyword(GpuParams.DDGI_SHOW_INDIRECT_ONLY);
                        break;
                    case IndirectDebugMode.PureIndirectRadiance:
                        cmd.EnableShaderKeyword(GpuParams.DDGI_SHOW_PURE_INDIRECT_RADIANCE);
                        break;
                }
            }
        }

        // Raytrace shader不支持multi_compile，我们使用int define的方式确定天光模式
        private enum SkyLightMode
        {
            DDGI_SKYLIGHT_MODE_SKYBOX_CUBEMAP = 0,
            DDGI_SKYLIGHT_MODE_GRADIENT = 1,
            DDGI_SKYLIGHT_MODE_COLOR = 2,
            DDGI_SKYLIGHT_MODE_UNSUPPORTED = 3
        }

        /// <summary>
        /// 本方法用于更新天空光照，以便给Miss Shader采样使用
        /// 依赖于Window->Rendering->Lighting面板参数
        /// </summary>
        private void UpdateSkyLight(CommandBuffer cmd)
        {
            switch (RenderSettings.ambientMode)
            {
                case AmbientMode.Skybox:
                    UpdateSkyLightAsSkybox(cmd);
                    break;
                case AmbientMode.Trilight:
                    UpdateSkyLightAsGradient(cmd);
                    break;
                case AmbientMode.Flat:
                    UpdateSkyLightAsColor(cmd);
                    break;
            }
        }

        private void UpdateSkyLightAsSkybox(CommandBuffer cmd)
        {
            var skybox = RenderSettings.skybox;
            if (skybox == null)
            {
                // 如果没有正确设置天空盒材质，则Fallback到纯色 (Ambient Color)，与Unity内行为一致
                UpdateSkyLightAsColor(cmd);
                return;
            }

            if (mCubemapSkyPS == null)
            {
                Debug.LogWarning("DDGIFeature没有成功找到URP内置的天空盒Shader，请排查");
                UpdateSkyLightAsBlack(cmd);
                return;
            }

            if (skybox.shader == mCubemapSkyPS)
            {
                cmd.SetRayTracingIntParam(mDDGIRayTraceShader, GpuParams.DDGI_SKYLIGHT_MODE, (int)SkyLightMode.DDGI_SKYLIGHT_MODE_SKYBOX_CUBEMAP);
                cmd.SetRayTracingFloatParam(mDDGIRayTraceShader, GpuParams._SkyboxIntensityMultiplier, RenderSettings.ambientIntensity);
                cmd.SetRayTracingVectorParam(mDDGIRayTraceShader, GpuParams._SkyboxTintColor, skybox.GetColor("_Tint"));
                cmd.SetRayTracingFloatParam(mDDGIRayTraceShader, GpuParams._SkyboxExposure, skybox.GetFloat("_Exposure"));
                cmd.SetRayTracingTextureParam(mDDGIRayTraceShader, GpuParams._SkyboxCubemap, skybox.GetTexture("_Tex"));
            }
            else
            {
                // 我们目前只支持应用最多的Cubemap式天空盒，其它类型的天空盒不受支持，将Fallback到纯黑
                UpdateSkyLightAsBlack(cmd);
            }
        }

        private void UpdateSkyLightAsGradient(CommandBuffer cmd)
        {
            cmd.SetRayTracingIntParam(mDDGIRayTraceShader, GpuParams.DDGI_SKYLIGHT_MODE, (int)SkyLightMode.DDGI_SKYLIGHT_MODE_GRADIENT);
            cmd.SetRayTracingVectorParam(mDDGIRayTraceShader, GpuParams._SkyColor, RenderSettings.ambientSkyColor);
            cmd.SetRayTracingVectorParam(mDDGIRayTraceShader, GpuParams._EquatorColor, RenderSettings.ambientEquatorColor);
            cmd.SetRayTracingVectorParam(mDDGIRayTraceShader, GpuParams._GroundColor, RenderSettings.ambientGroundColor);
        }

        private void UpdateSkyLightAsColor(CommandBuffer cmd)
        {
            cmd.SetRayTracingIntParam(mDDGIRayTraceShader, GpuParams.DDGI_SKYLIGHT_MODE, (int)SkyLightMode.DDGI_SKYLIGHT_MODE_COLOR);
            cmd.SetRayTracingVectorParam(mDDGIRayTraceShader, GpuParams._AmbientColor, RenderSettings.ambientSkyColor);
        }

        private void UpdateSkyLightAsBlack(CommandBuffer cmd)
        {
            cmd.SetRayTracingIntParam(mDDGIRayTraceShader, GpuParams.DDGI_SKYLIGHT_MODE, (int)SkyLightMode.DDGI_SKYLIGHT_MODE_UNSUPPORTED);
        }

        /// <summary>
        /// Unity默认会对场景中的额外光做剔除，这会影响我们获取场景全局的光照信息
        /// 只能自己在CPU端手动收集一次
        /// </summary>
        private void BuildLightStructuredBuffer(CommandBuffer cmd)
        {
            var cpuLights = FindObjectsOfType<Light>();

            var gpuDirectionalLights = new List<DirectionalLight>();
            var gpuPunctualLights = new List<PunctualLight>();
            foreach (var cpuLight in cpuLights)
            {
                // 暂不支持面光源的动态全局光照...
                if (cpuLight.type == LightType.Point || cpuLight.type == LightType.Spot)
                {
                    var position = cpuLight.transform.position;
                    var color = cpuLight.color * cpuLight.intensity;
                    var lightAttenuation = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
                    var lightSpotDir = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
                    
                    GetPunctualLightDistanceAttenuation(cpuLight.range, ref lightAttenuation);

                    if (cpuLight.type == LightType.Spot)
                    {
                        GetSpotDirection(cpuLight.transform.forward, out lightSpotDir);
                        GetSpotAngleAttenuation(cpuLight.spotAngle, cpuLight.innerSpotAngle, ref lightAttenuation);
                    }
                    
                    PunctualLight punctualLight;
                    punctualLight.position = new Vector4(position.x, position.y, position.z, 1.0f);
                    punctualLight.color = color;
                    punctualLight.distanceAndSpotAttenuation = lightAttenuation;
                    punctualLight.spotDirection = lightSpotDir;
                    
                    gpuPunctualLights.Add(punctualLight);
                }
                else if (cpuLight.type == LightType.Directional)
                {
                    var lightForward = cpuLight.transform.forward;
                    
                    DirectionalLight directionalLight;
                    directionalLight.direction = new Vector4(-lightForward.x, -lightForward.y, -lightForward.z, 0.0f);
                    directionalLight.color = cpuLight.color;
                    
                    gpuDirectionalLights.Add(directionalLight);
                }
            }
            
            // 如果灯光数组大小为0，就只申请带1个元素的空buffer，创建大小为0的ComputeBuffer会引发错误
            if(mDirectionalLightBuffer != null) { mDirectionalLightBuffer.Release(); mDirectionalLightBuffer = null; }
            mDirectionalLightBuffer = new ComputeBuffer(Mathf.Max(gpuDirectionalLights.Count, 1), 2 * 16, ComputeBufferType.Default);
            
            if(mPunctualLightBuffer != null) { mPunctualLightBuffer.Release(); mPunctualLightBuffer = null; }
            mPunctualLightBuffer = new ComputeBuffer(Mathf.Max(gpuPunctualLights.Count, 1), 4 * 16, ComputeBufferType.Default);

            mDirectionalLightBuffer.SetData(gpuDirectionalLights.ToArray());
            mPunctualLightBuffer.SetData(gpuPunctualLights.ToArray());
            
            cmd.SetGlobalInt(GpuParams._DirectionalLightCount, gpuDirectionalLights.Count);
            cmd.SetGlobalInt(GpuParams._PunctualLightCount, gpuPunctualLights.Count);
        }
        
        // Reference: UniversalRenderPipelineCore.cs 1634
        private static void GetPunctualLightDistanceAttenuation(float lightRange, ref Vector4 lightAttenuation)
        {
            // Light attenuation in universal matches the unity vanilla one (HINT_NICE_QUALITY).
            // attenuation = 1.0 / distanceToLightSqr
            // The smoothing factor makes sure that the light intensity is zero at the light range limit.
            // (We used to offer two different smoothing factors.)

            // The current smoothing factor matches the one used in the Unity lightmapper.
            // smoothFactor = (1.0 - saturate((distanceSqr * 1.0 / lightRangeSqr)^2))^2
            float lightRangeSqr = lightRange * lightRange;
            float fadeStartDistanceSqr = 0.8f * 0.8f * lightRangeSqr;
            float fadeRangeSqr = (fadeStartDistanceSqr - lightRangeSqr);
            float lightRangeSqrOverFadeRangeSqr = -lightRangeSqr / fadeRangeSqr;
            float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, lightRangeSqr);

            // On all devices: Use the smoothing factor that matches the GI.
            lightAttenuation.x = oneOverLightRangeSqr;
            lightAttenuation.y = lightRangeSqrOverFadeRangeSqr;
        }

        // Reference: UniversalRenderPipelineCore.cs 1654
        private static void GetSpotAngleAttenuation(float spotAngle, float? innerSpotAngle, ref Vector4 lightAttenuation)
        {
            // Spot Attenuation with a linear falloff can be defined as
            // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
            // This can be rewritten as
            // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
            // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
            // If we precompute the terms in a MAD instruction
            float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * spotAngle * 0.5f);
            // We need to do a null check for particle lights
            // This should be changed in the future
            // Particle lights will use an inline function
            float cosInnerAngle;
            if (innerSpotAngle.HasValue)
                cosInnerAngle = Mathf.Cos(innerSpotAngle.Value * Mathf.Deg2Rad * 0.5f);
            else
                cosInnerAngle = Mathf.Cos((2.0f * Mathf.Atan(Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad) * (64.0f - 18.0f) / 64.0f)) * 0.5f);
            float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
            float invAngleRange = 1.0f / smoothAngleRange;
            float add = -cosOuterAngle * invAngleRange;

            lightAttenuation.z = invAngleRange;
            lightAttenuation.w = add;
        }
        
        // Reference: UniversalRenderPipelineCore.cs 1681
        private static void GetSpotDirection(Vector3 forward, out Vector4 lightSpotDir)
        {
            lightSpotDir = new Vector4(-forward.x, -forward.y, -forward.z, 0.0f);
        }

        private Bounds GenerateSceneMeshBounds()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

            if (mddgiOverride != null && mddgiOverride.useCustomBounds.value)
            {
                // 目前只支持单个自定义包围盒
                var ddgiCustomBounds = FindFirstObjectByType<DDGICustomBounds>();
                var boxCollider = ddgiCustomBounds.GetComponent<BoxCollider>();
                if (boxCollider != null) bounds = boxCollider.bounds;
            }
            else
            {
                // 根据场景Mesh自动生成包围盒
                foreach (var meshRenderer in FindObjectsOfType<MeshRenderer>())
                {
                    bounds.Encapsulate(meshRenderer.bounds);
                }
                
                // 理论上来说我们不会逐帧更新包围盒，因此不必强行包含骨骼网格体，下面这段去掉也是可以的
                foreach (var skinnedMeshRenderer in FindObjectsOfType<SkinnedMeshRenderer>())
                {
                    bounds.Encapsulate(skinnedMeshRenderer.bounds);
                }
            }

            return bounds;
        }

        private static Vector2Int GetProbeTextureDimensions(Vector3Int numProbes, int texelsPerProbe)
        {
            // 这里加1是为每一个Probe的Texture区块留边
            int width = (1 + texelsPerProbe + 1) * numProbes.y * numProbes.x;
            int height = (1 + texelsPerProbe + 1) * numProbes.z;
            return new Vector2Int(width, height);
        }

        private static Vector3Int GetProbeDataDimensions(Vector3Int numProbes)
        {
            return numProbes;
        }
        
        private static double NextDouble(Random ran, double minValue, double maxValue, int decimalPlace)
        {
            double randNum = ran.NextDouble() * (maxValue - minValue) + minValue;
            return Convert.ToDouble(randNum.ToString("f" + decimalPlace));
        }
    }

    public sealed class DDGIVisualizePass : ScriptableRenderPass
    {
        private DDGI mddgiOverride;
        
        private Shader mVisualizeShader;
        private Material mVisualizeMaterial;
        private Mesh mVisualizeMesh;

        private DDGIPass mDDGIPass;

        private ComputeBuffer mArgsBuffer;

        private static class GpuParams
        {
            public static readonly string DDGI_DEBUG_IRRADIANCE = "DDGI_DEBUG_IRRADIANCE";
            public static readonly string DDGI_DEBUG_DISTANCE = "DDGI_DEBUG_DISTANCE";
            public static readonly string DDGI_DEBUG_OFFSET = "DDGI_DEBUG_OFFSET";
            
            public static readonly int _ProbeData = Shader.PropertyToID("_ProbeData");
            public static readonly int _ddgiSphere_ObjectToWorld = Shader.PropertyToID("_ddgiSphere_ObjectToWorld");
        }
        
        public DDGIVisualizePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            mVisualizeShader = Resources.Load<Shader>("Shaders/DDGIVisualize");
            mVisualizeMaterial = CoreUtils.CreateEngineMaterial(mVisualizeShader);
            mVisualizeMaterial.enableInstancing = true;
        }

        public void Setup(Mesh debugMesh, DDGIPass ddgiPass)
        {
            mVisualizeMesh = debugMesh;
            mDDGIPass = ddgiPass;
        }

        public void Release()
        {
            CoreUtils.Destroy(mVisualizeMaterial);

            if (mArgsBuffer != null)
            {
                mArgsBuffer.Release();
                mArgsBuffer = null;
            }
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            base.Configure(cmd, cameraTextureDescriptor);

            mddgiOverride = VolumeManager.instance.stack.GetComponent<DDGI>();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (mVisualizeMesh == null || mDDGIPass == null) return;
            if (mddgiOverride == null || !mddgiOverride.IsActive()) return;

            var ddgiOverride = VolumeManager.instance.stack.GetComponent<DDGI>();
            if (ddgiOverride == null) return;

            if (!ddgiOverride.debugProbe.value) return;
            
            var cmd = CommandBufferPool.Get("DDGI Visualize");
            var camera = renderingData.cameraData.camera;
            var renderer = renderingData.cameraData.renderer;

            // Configure Debug Mode.
            {
                cmd.DisableShaderKeyword(GpuParams.DDGI_DEBUG_IRRADIANCE);
                cmd.DisableShaderKeyword(GpuParams.DDGI_DEBUG_DISTANCE);
                cmd.DisableShaderKeyword(GpuParams.DDGI_DEBUG_OFFSET);
                
                if (ddgiOverride.debugProbe.value)
                {
                    switch (ddgiOverride.probeDebugMode.value)
                    {
                        case ProbeDebugMode.Irradiance:
                            cmd.EnableShaderKeyword(GpuParams.DDGI_DEBUG_IRRADIANCE);
                            break;
                        case ProbeDebugMode.Distance:
                            cmd.EnableShaderKeyword(GpuParams.DDGI_DEBUG_DISTANCE);
                            break;
                        case ProbeDebugMode.RelocationOffset:
                            cmd.EnableShaderKeyword(GpuParams.DDGI_DEBUG_OFFSET);
                            break;
                    }
                }
            }

            // Prepare Rendering Parameters
            {
                var matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * mddgiOverride.probeRadius.value);
                cmd.SetGlobalMatrix(GpuParams._ddgiSphere_ObjectToWorld, matrix);
                
                cmd.SetGlobalTexture(GpuParams._ProbeData, mDDGIPass.GetProbeData());
            }

            // Construct Indirect Draw Arguments.
            {
                var numProbes = mDDGIPass.GetNumProbes();
                var numProbesFlat = numProbes.x * numProbes.y * numProbes.z; 
                
                uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
                args[0] = (uint)mVisualizeMesh.GetIndexCount(0);
                args[1] = (uint)numProbesFlat;
                args[2] = (uint)mVisualizeMesh.GetIndexStart(0);
                args[3] = (uint)mVisualizeMesh.GetBaseVertex(0);
                
                if(mArgsBuffer != null) { mArgsBuffer.Release(); mArgsBuffer = null; }
                mArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
                mArgsBuffer.SetData(args);
            }
            
            // Draw Spheres.
            {
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                // cmd.DrawMeshInstanced限制每Pass最多1024个，所以只能用间接绘制
                cmd.DrawMeshInstancedIndirect(mVisualizeMesh, 0, mVisualizeMaterial, 0, mArgsBuffer);
            }
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    public DDGIPass GetDDGIPass() => mDDGIPass;

    private DDGIPass mDDGIPass;
    private DDGIVisualizePass mDDGIVisualizePass;

    private Mesh mDDGIVisualizeSphere;

    private bool mIsRayTracingSupported;

    public override void Create()
    {
        mIsRayTracingSupported = SystemInfo.supportsRayTracing;
        if (!mIsRayTracingSupported) return;
        
        mDDGIPass = new DDGIPass();
        mDDGIVisualizePass = new DDGIVisualizePass();

        mDDGIVisualizeSphere = Resources.Load<Mesh>("Meshes/DDGIVisualizationSphere");

    #if UNITY_EDITOR
        EditorSceneManager.sceneOpened += OnSceneOpened;
    #endif
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera) return;
        if (!mIsRayTracingSupported) return;
        
        renderer.EnqueuePass(mDDGIPass);

        mDDGIVisualizePass.Setup(mDDGIVisualizeSphere, mDDGIPass);
        renderer.EnqueuePass(mDDGIVisualizePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        
        if (!mIsRayTracingSupported) return;

        if (mDDGIPass != null)
        {
            mDDGIPass.Release();
            mDDGIPass = null;
        }

        if (mDDGIVisualizePass != null)
        {
            mDDGIVisualizePass.Release();
            mDDGIVisualizePass = null;
        }

    #if UNITY_EDITOR
        EditorSceneManager.sceneOpened -= OnSceneOpened;
    #endif
    }
    
    public void Reinitialize()
    {
        mDDGIPass.Reinitialize();
    }
    
    private void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        Reinitialize();
    }
}
