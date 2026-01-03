// =====================================
// ANIMATION PREVIEW CONTROLLER EDITOR
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
        
        // Clip selection from library
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clip Selection", EditorStyles.boldLabel);
        
        var newClip = (AnimationClipSO)EditorGUILayout.ObjectField(
            "Animation Clip",
            controller.currentClip,
            typeof(AnimationClipSO),
            false
        );
        
        if (newClip != controller.currentClip)
        {
            controller.SetClip(newClip);
            EditorUtility.SetDirty(controller);
        }
        
        // Show current animation info
        if (controller.currentClip != null)
        {
            EditorGUILayout.HelpBox(
                $"Type: {controller.currentAnimation}\n" +
                $"Duration: {controller.currentClip.duration:F2}s\n" +
                $"Looping: {controller.currentClip.looping}\n" +
                $"Tracks: {controller.currentClip.partTracks?.Count ?? 0}",
                MessageType.Info
            );
        }
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Open Animation Editor"))
        {
            AnimationClipEditorWindow.ShowWindow();
        }
    }
}
#endif