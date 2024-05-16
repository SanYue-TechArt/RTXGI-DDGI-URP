using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[CustomEditor(typeof(DDGI))]
public sealed class DDGIEditor : VolumeComponentEditor
{
    private SerializedDataParameter mEnableDDGI;
    private SerializedDataParameter mIndirectIntensity;
    private SerializedDataParameter mNormalBiasMultiplier;
    private SerializedDataParameter mViewBiasMultiplier;
    private SerializedDataParameter mDebugProbe;
    private SerializedDataParameter mProbeDebugMode;
    private SerializedDataParameter mProbeRadius;
    private SerializedDataParameter mDebugIndirect;
    private SerializedDataParameter mIndirectDebugMode;
    private SerializedDataParameter mProbeMinFrontfaceDistance;
    private SerializedDataParameter mProbeFixedRayBackfaceThreshold;
    private SerializedDataParameter mUseCustomBounds;
    private SerializedDataParameter mProbeCountX;
    private SerializedDataParameter mProbeCountY;
    private SerializedDataParameter mProbeCountZ;
    private SerializedDataParameter mRaysPerProbe;

    public override void OnEnable()
    {
        var o = new PropertyFetcher<DDGI>(serializedObject);

        mEnableDDGI = Unpack(o.Find(x => x.enableDDGI));
        mIndirectIntensity = Unpack(o.Find(x => x.indirectIntensity));
        mNormalBiasMultiplier = Unpack(o.Find(x => x.normalBiasMultiplier));
        mViewBiasMultiplier = Unpack(o.Find(x => x.viewBiasMultiplier));
        mDebugProbe = Unpack(o.Find(x => x.debugProbe));
        mProbeDebugMode = Unpack(o.Find(x => x.probeDebugMode));
        mProbeRadius = Unpack(o.Find(x => x.probeRadius));
        mDebugIndirect = Unpack(o.Find(x => x.debugIndirect));
        mIndirectDebugMode = Unpack(o.Find(x => x.indirectDebugMode));
        mProbeMinFrontfaceDistance = Unpack(o.Find(x => x.probeMinFrontfaceDistance));
        mProbeFixedRayBackfaceThreshold = Unpack(o.Find(x => x.probeFixedRayBackfaceThreshold));
        mUseCustomBounds = Unpack(o.Find(x => x.useCustomBounds));
        mProbeCountX = Unpack(o.Find(x => x.probeCountX));
        mProbeCountY = Unpack(o.Find(x => x.probeCountY));
        mProbeCountZ = Unpack(o.Find(x => x.probeCountZ));
        mRaysPerProbe = Unpack(o.Find(x => x.raysPerProbe));
    }

    public override void OnInspectorGUI()
    {
        if (!SystemInfo.supportsRayTracing)
        {
            EditorGUILayout.HelpBox("DDGI依赖硬件光线跟踪，只在DX12、Playstation 5以及Xbox Series X上受支持", MessageType.Warning);
            return;
        }
        
        PropertyField(mEnableDDGI);

    #region Dynamic Lighting Settings

        PropertyField(mIndirectIntensity);
        PropertyField(mNormalBiasMultiplier);
        PropertyField(mViewBiasMultiplier);
        EditorGUILayout.Space(5.0f);

    #endregion

        
    #region Debug Options

        PropertyField(mDebugProbe);
        if (mDebugProbe.value.boolValue)
        {
            EditorGUI.indentLevel++;
            PropertyField(mProbeDebugMode);
            PropertyField(mProbeRadius);
            EditorGUI.indentLevel--;
        }
        PropertyField(mDebugIndirect);
        if (mDebugIndirect.value.boolValue)
        {
            EditorGUI.indentLevel++;
            PropertyField(mIndirectDebugMode);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space(5.0f);

    #endregion


    #region Relocation Settings

        PropertyField(mProbeMinFrontfaceDistance);
        PropertyField(mProbeFixedRayBackfaceThreshold);
        EditorGUILayout.Space(5.0f);

    #endregion


    #region Reinitialize Settings
    
        PropertyField(mUseCustomBounds);
        if (mUseCustomBounds.value.boolValue)
        {
            var customBounds = FindFirstObjectByType<DDGICustomBounds>();
            if (customBounds == null)
            {
                EditorGUILayout.HelpBox("在当前场景中未检测到有效的DDGI Custom Bounds，您可能从未创建过它，或者将其设置为了禁用状态；" +
                                        "要创建它，你可以在Hierarchy中右击->Light->DDGI Custom Bounds", 
                    MessageType.Warning);
            }
        }
            
        PropertyField(mProbeCountX);
        PropertyField(mProbeCountY);
        PropertyField(mProbeCountZ);
        PropertyField(mRaysPerProbe);
    
    #endregion
        
    
        if (GUILayout.Button("Refresh DDGI Settings"))
        {
            var urpAsset = GraphicsSettings.renderPipelineAsset;
            
            if (urpAsset != null && urpAsset is UniversalRenderPipelineAsset)
            {
                Type urpAssetType = urpAsset.GetType();
                FieldInfo scriptableRendererDataListField = urpAssetType.GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                    
                if (scriptableRendererDataListField != null)
                {
                    ScriptableRendererData[] rendererDataList = scriptableRendererDataListField.GetValue(urpAsset) as ScriptableRendererData[];

                    if (rendererDataList == null) return;
                        
                    foreach (var rendererData in rendererDataList)
                    {
                        var ddgiFeature = (DDGIFeature)rendererData.rendererFeatures.Find(x => x.GetType() == typeof(DDGIFeature));
                        if(ddgiFeature != null) ddgiFeature.Reinitialize();
                    }
                }
            }
        }
    }
}
