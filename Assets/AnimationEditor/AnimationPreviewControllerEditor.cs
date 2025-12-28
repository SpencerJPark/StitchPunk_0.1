// =====================================
// SCENE VIEW PREVIEW (Optional - shows in Scene view)
// =====================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AnimationPreviewController))]
public class AnimationPreviewControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        var controller = (AnimationPreviewController)target;
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick Controls", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        
        GUI.enabled = Application.isPlaying;
        
        if (GUILayout.Button("⏮"))
            controller.Stop();
        if (GUILayout.Button("⏪"))
            controller.PreviousFrame();
        if (GUILayout.Button(controller.isPlaying ? "⏸" : "▶"))
        {
            if (controller.isPlaying) controller.Pause();
            else controller.Play();
        }
        if (GUILayout.Button("⏩"))
            controller.NextFrame();
        
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Open Animation Editor"))
        {
            AnimationClipEditorWindow.ShowWindow();
        }
    }
}
#endif