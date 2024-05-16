using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DDGIFeature))]
public class DDGIFeatureEditor : Editor
{
    private void OnEnable()
    {

    }

    public override void OnInspectorGUI()
    {
        if (!SystemInfo.supportsRayTracing)
        {
            EditorGUILayout.HelpBox("DDGI依赖硬件光线跟踪，只在DX12、Playstation 5以及Xbox Series X上受支持", MessageType.Warning);
            return;
        }
    }
}
