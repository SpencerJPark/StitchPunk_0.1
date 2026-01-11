// =====================================
// ANIMATION PREVIEW CONTROLLER EDITOR
// =====================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(AnimationPreviewController))]
public class AnimationPreviewControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var controller = (AnimationPreviewController)target;
        
        // Mode toggle
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview Mode", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        controller.mode = (PreviewMode)EditorGUILayout.EnumPopup("Mode", controller.mode);
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(controller);
        }
        
        EditorGUILayout.Space();
        
        // Mode-specific UI
        if (controller.mode == PreviewMode.ClipEdit)
        {
            DrawClipEditMode(controller);
        }
        else
        {
            DrawClipPreviewMode(controller);
        }
        
        // Playback controls
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        controller.normalizedTime = EditorGUILayout.Slider("Time", controller.normalizedTime, 0f, 1f);
        controller.playbackSpeed = EditorGUILayout.FloatField("Speed", controller.playbackSpeed);
        controller.loop = EditorGUILayout.Toggle("Loop", controller.loop);
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(controller);
        }
        
        // Transport controls
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        
        GUI.enabled = Application.isPlaying;
        
        if (GUILayout.Button("⏮", GUILayout.Width(40)))
            controller.Stop();
        if (GUILayout.Button("⏪", GUILayout.Width(40)))
            controller.PreviousFrame();
        if (GUILayout.Button(controller.isPlaying ? "⏸" : "▶", GUILayout.Width(40)))
        {
            if (controller.isPlaying) controller.Pause();
            else controller.Play();
        }
        if (GUILayout.Button("⏩", GUILayout.Width(40)))
            controller.NextFrame();
        
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();
        
        // Active layers display
        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active Layers (Runtime)", EditorStyles.boldLabel);
            
            var layers = controller.GetCurrentLayers();
            
            if (layers.Count == 0)
            {
                EditorGUILayout.HelpBox("No active layers", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    
                    Color statusColor = layer.active ? Color.green : Color.gray;
                    var style = new GUIStyle(EditorStyles.label);
                    style.normal.textColor = statusColor;
                    
                    EditorGUILayout.LabelField($"[{layer.layer}]", style, GUILayout.Width(70));
                    EditorGUILayout.LabelField($"{layer.animation}", GUILayout.Width(100));
                    EditorGUILayout.LabelField($"t:{layer.time:F2}", GUILayout.Width(50));
                    EditorGUILayout.LabelField(layer.active ? "●" : "○", GUILayout.Width(20));
                    
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
        
        // Debug
        EditorGUILayout.Space();
        controller.showKeyframeMarkers = EditorGUILayout.Toggle("Show Gizmos", controller.showKeyframeMarkers);
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Open Animation Editor Window"))
        {
            AnimationClipEditorWindow.ShowWindow();
        }
    }
    
    private void DrawClipEditMode(AnimationPreviewController controller)
    {
        EditorGUILayout.LabelField("Clip Edit Mode", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        var newClip = (AnimationClipSO)EditorGUILayout.ObjectField(
            "Edit Clip",
            controller.editClip,
            typeof(AnimationClipSO),
            false
        );
        
        if (EditorGUI.EndChangeCheck() && newClip != controller.editClip)
        {
            controller.SetEditClip(newClip);
            EditorUtility.SetDirty(controller);
        }
        
        if (controller.editClip != null)
        {
            EditorGUILayout.HelpBox(
                $"Type: {controller.editClip.animationType}\n" +
                $"Duration: {controller.editClip.duration:F2}s\n" +
                $"Looping: {controller.editClip.looping}\n" +
                $"Tracks: {controller.editClip.partTracks?.Count ?? 0}",
                MessageType.Info
            );
        }
    }
    
    private void DrawClipPreviewMode(AnimationPreviewController controller)
    {
        EditorGUILayout.LabelField("Clip Preview Mode", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Layer 0 = Base (lowest priority)\nHigher layers override lower layers", MessageType.Info);
        
        // Preview layers list
        EditorGUILayout.Space();
        
        for (int i = 0; i < controller.previewLayers.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField($"Layer {i}:", GUILayout.Width(60));
            
            EditorGUI.BeginChangeCheck();
            controller.previewLayers[i] = (AnimationClipSO)EditorGUILayout.ObjectField(
                controller.previewLayers[i],
                typeof(AnimationClipSO),
                false
            );
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(controller);
            }
            
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                controller.RemovePreviewLayer(i);
                EditorUtility.SetDirty(controller);
                GUIUtility.ExitGUI();
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Show clip info
            if (controller.previewLayers[i] != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"→ {controller.previewLayers[i].animationType} ({controller.previewLayers[i].duration:F2}s)", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }
        
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("+ Add Layer"))
        {
            controller.previewLayers.Add(null);
            EditorUtility.SetDirty(controller);
        }
        
        if (controller.previewLayers.Count > 0 && GUILayout.Button("Clear All"))
        {
            controller.ClearPreviewLayers();
            EditorUtility.SetDirty(controller);
        }
        
        EditorGUILayout.EndHorizontal();
    }
}
#endif