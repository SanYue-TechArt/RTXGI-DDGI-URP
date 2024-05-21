using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum ProbeDebugMode
{
    Irradiance,
    Distance,
    RelocationOffset
}

public enum IndirectDebugMode
{
    FullIndirectRadiance = 0,
    PureIndirectRadiance = 1
}

[Serializable, VolumeComponentMenuForRenderPipeline("Lighting/Dynamic Diffuse Global Illumination", typeof(UniversalRenderPipeline))]
public class DDGI : VolumeComponent, IPostProcessComponent
{
    // 该参数是为了解决场景volume没挂载ddgi时，VolumeManager.instance.stack仍能获取到其它场景ddgi组件的问题
    // 目前尚不明确这种问题为什么会发生
    public BoolParameter enableDDGI = new BoolParameter(false);

#region Dynamic Lighting Settings

    [Header("Dynamic Lighting Settings")]
    public ClampedFloatParameter indirectIntensity = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);

    public ClampedFloatParameter normalBiasMultiplier = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);
    
    public ClampedFloatParameter viewBiasMultiplier = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);
    
    //public ClampedFloatParameter biasMultiplier = new ClampedFloatParameter(0.25f, 0.0f, 1.0f);
    //public ClampedFloatParameter axialDistanceMultiplier = new ClampedFloatParameter(0.75f, 0.0f, 1.0f);

#endregion


#region Relocation Settings

    [Header("Relocation Settings")]
    public BoolParameter enableProbeRelocation = new BoolParameter(true);
        
    public ClampedFloatParameter probeMinFrontfaceDistance = new ClampedFloatParameter(0.3f, 0.0f, 2.0f);
            
    [Tooltip("当Probe中命中背面的光线比例超过该数值时，将对Probe进行偏移处理（由于Relocation过程不可逆，调高该数值时需要刷新DDGI设置）")]
    public ClampedFloatParameter probeFixedRayBackfaceThreshold = new ClampedFloatParameter(0.25f, 0.0f, 1.0f);

#endregion


#region Probe Variability Settings

    [Header("Probe Variability Settings (Experimental)")]
    [Tooltip("开启后，系统将分析GI结果的变异性 (Variability)，如果变异性较低，则表明积分结果收敛，我们将停止更新Probe Texture，避免间接光闪烁，同时节省开销【注：该特性不支持自发光物体！】")]
    public BoolParameter enableProbeVariability = new BoolParameter(false);

    [Tooltip("当变异性低于该值时GI将停止更新，该值越大则积分会更早收敛【注：过高的值可能会导致GI收敛性不足；过低的值则该特性不能防止间接光闪烁】")]
    public ClampedFloatParameter probeVariabilityThreshold = new ClampedFloatParameter(0.025f, 0.0f, 1.0f);

#endregion


#region Debug Options

    [Header("Debug Options")]
    public BoolParameter debugProbe = new BoolParameter(false);

    public ProbeDebugParameter probeDebugMode = new ProbeDebugParameter(ProbeDebugMode.Irradiance);

    public ClampedFloatParameter probeRadius = new ClampedFloatParameter(11.0f, 0.01f, 20.0f);

    [Tooltip("只展示间接光照结果（考虑几何表面反照率）")]
    public BoolParameter debugIndirect = new BoolParameter(false);

    public IndirectDebugParameter indirectDebugMode = new IndirectDebugParameter(IndirectDebugMode.FullIndirectRadiance);

#endregion


#region Reinitialize Settings

    [Header("Reinitialize Settings (Need Refresh)")]
    [Tooltip("使用自定义的DDGI边界框，此功能需要在场景内创建DDGI Custom Bounds")]
    public BoolParameter useCustomBounds = new BoolParameter(false);

    public ClampedIntParameter probeCountX = new ClampedIntParameter(22, 1, 25);
    public ClampedIntParameter probeCountY = new ClampedIntParameter(22, 1, 25);
    public ClampedIntParameter probeCountZ = new ClampedIntParameter(22, 1, 25);

    [Tooltip("每一个Probe发射的光线数量")]
    public ClampedIntParameter raysPerProbe = new ClampedIntParameter(144, 32, 256);

#endregion


    public bool IsActive() => indirectIntensity.value > 0.0f && enableDDGI.value;
    
    public bool IsTileCompatible() => false;
}

[Serializable]
public sealed class ProbeDebugParameter : VolumeParameter<ProbeDebugMode>
{
    public ProbeDebugParameter(ProbeDebugMode value, bool overrideState = false) : base(value, overrideState) { }
}

[Serializable]
public sealed class IndirectDebugParameter : VolumeParameter<IndirectDebugMode>
{
    public IndirectDebugParameter(IndirectDebugMode value, bool overrideState = false) : base(value, overrideState) { }
}