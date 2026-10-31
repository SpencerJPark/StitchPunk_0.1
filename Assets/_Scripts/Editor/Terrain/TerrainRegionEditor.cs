#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(TerrainRegion))]
class TerrainRegionEditor : Editor
{
    readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        TerrainRegion region = (TerrainRegion)target;
        EditorGUILayout.LabelField("Tiles covered: " + region.TilesInside().Count());
    }

    void OnSceneGUI()
    {
        TerrainRegion region = (TerrainRegion)target;
        boundsHandle.center = (region.boundsMin + region.boundsMax) * 0.5f;
        boundsHandle.size = region.boundsMax - region.boundsMin;

        EditorGUI.BeginChangeCheck();
        boundsHandle.DrawHandle();
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(region, "Resize Terrain Region");
            region.boundsMin = boundsHandle.center - boundsHandle.size * 0.5f;
            region.boundsMax = boundsHandle.center + boundsHandle.size * 0.5f;
            EditorUtility.SetDirty(region);
        }
    }
}
#endif
