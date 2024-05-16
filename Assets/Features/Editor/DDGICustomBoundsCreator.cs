using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class DDGICustomBoundsCreator : Editor
{
    [MenuItem("GameObject/Light/DDGI Custom Bounds")]
    public static void CreateDDGICustomBounds()
    {
        if (FindObjectsOfType<DDGICustomBounds>().Length > 0)
        {
            EditorUtility.DisplayDialog("不允许创建重复的DDGI Custom Bounds", "场景中已存在DDGI Custom Bounds", "确认");
            return;
        }
        
        var ddgiBounds = new GameObject("DDGI Custom Bounds");
        ddgiBounds.AddComponent<BoxCollider>();
        ddgiBounds.AddComponent<DDGICustomBounds>();
    }
}
