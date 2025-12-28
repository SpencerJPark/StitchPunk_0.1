// =====================================
// CUSTOM EDITOR WINDOW
// =====================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class AnimationClipEditorWindow : EditorWindow
{
    // Current state
    private AnimationClipSO currentClip;
    private AnimationPreviewController previewController;
    private int selectedTrackIndex = -1;
    private int selectedKeyframeIndex = -1;
    
    // UI State
    private Vector2 trackScrollPos;
    private Vector2 keyframeScrollPos;
    private Vector2 inspectorScrollPos;
    private float timelineZoom = 1f;
    private float timelineOffset = 0f;
    
    // Dragging state
    private bool isDraggingKeyframe = false;
    private bool isDraggingPlayhead = false;
    private float dragStartTime;
    
    // Layout constants
    private const float TIMELINE_HEIGHT = 60f;
    private const float TRACK_HEIGHT = 30f;
    private const float KEYFRAME_SIZE = 12f;
    private const float INSPECTOR_WIDTH = 300f;
    
    // Colors
    private static readonly Color TRACK_BG_EVEN = new Color(0.22f, 0.22f, 0.22f);
    private static readonly Color TRACK_BG_ODD = new Color(0.25f, 0.25f, 0.25f);
    private static readonly Color TRACK_SELECTED = new Color(0.3f, 0.4f, 0.5f);
    private static readonly Color KEYFRAME_COLOR = new Color(1f, 0.8f, 0.2f);
    private static readonly Color KEYFRAME_SELECTED = new Color(0.2f, 0.8f, 1f);
    private static readonly Color PLAYHEAD_COLOR = Color.red;
    private static readonly Color TIMELINE_BG = new Color(0.18f, 0.18f, 0.18f);
    
    [MenuItem("Window/Animation/Clip Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<AnimationClipEditorWindow>("Animation Clip Editor");
        window.minSize = new Vector2(800, 500);
    }
    
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        FindPreviewController();
    }
    
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }
    
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            FindPreviewController();
        }
    }
    
    private void FindPreviewController()
    {
        previewController = FindObjectOfType<AnimationPreviewController>();
    }
    
    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        
        // Left panel - Track list and timeline
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        DrawToolbar();
        DrawTimeline();
        DrawTrackList();
        EditorGUILayout.EndVertical();
        
        // Right panel - Inspector
        EditorGUILayout.BeginVertical(GUILayout.Width(INSPECTOR_WIDTH));
        DrawInspector();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndHorizontal();
        
        HandleInput();
        
        // Repaint during playback
        if (Application.isPlaying && previewController != null && previewController.isPlaying)
        {
            Repaint();
        }
    }
    
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // Clip selector
        EditorGUI.BeginChangeCheck();
        currentClip = (AnimationClipSO)EditorGUILayout.ObjectField(
            currentClip, typeof(AnimationClipSO), false, GUILayout.Width(200));
        if (EditorGUI.EndChangeCheck() && previewController != null)
        {
            previewController.SetClip(currentClip);
        }
        
        GUILayout.Space(20);
        
        // Playback controls
        GUI.enabled = Application.isPlaying && previewController != null;
        
        if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(25)))
        {
            previewController?.Stop();
        }
        if (GUILayout.Button("⏪", EditorStyles.toolbarButton, GUILayout.Width(25)))
        {
            previewController?.PreviousFrame();
        }
        
        bool isPlaying = previewController != null && previewController.isPlaying;
        if (GUILayout.Button(isPlaying ? "⏸" : "▶", EditorStyles.toolbarButton, GUILayout.Width(25)))
        {
            if (isPlaying) previewController?.Pause();
            else previewController?.Play();
        }
        
        if (GUILayout.Button("⏩", EditorStyles.toolbarButton, GUILayout.Width(25)))
        {
            previewController?.NextFrame();
        }
        
        GUI.enabled = true;
        
        GUILayout.Space(20);
        
        // Time display
        float currentTime = previewController != null ? previewController.normalizedTime : 0f;
        float duration = currentClip != null ? currentClip.duration : 1f;
        EditorGUILayout.LabelField($"Time: {currentTime:F2} / {currentTime * duration:F2}s", GUILayout.Width(120));
        
        GUILayout.FlexibleSpace();
        
        // Zoom controls
        EditorGUILayout.LabelField("Zoom:", GUILayout.Width(40));
        timelineZoom = EditorGUILayout.Slider(timelineZoom, 0.5f, 4f, GUILayout.Width(100));
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawTimeline()
    {
        Rect timelineRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, 
            GUILayout.ExpandWidth(true), GUILayout.Height(TIMELINE_HEIGHT));
        
        // Background
        EditorGUI.DrawRect(timelineRect, TIMELINE_BG);
        
        if (currentClip == null) return;
        
        float contentWidth = timelineRect.width - 20f;
        float scaledWidth = contentWidth * timelineZoom;
        
        // Draw time markers
        Handles.color = Color.gray;
        int numMarkers = Mathf.CeilToInt(10 * timelineZoom);
        for (int i = 0; i <= numMarkers; i++)
        {
            float t = i / (float)numMarkers;
            float x = timelineRect.x + 10f + t * scaledWidth - timelineOffset;
            
            if (x < timelineRect.x || x > timelineRect.xMax) continue;
            
            Handles.DrawLine(
                new Vector3(x, timelineRect.y + 30f, 0),
                new Vector3(x, timelineRect.yMax, 0));
            
            GUI.Label(new Rect(x - 20, timelineRect.y + 10f, 40, 20), 
                $"{t:F1}", EditorStyles.miniLabel);
        }
        
        // Draw keyframe markers (aggregate from all tracks)
        HashSet<float> keyframeTimes = new HashSet<float>();
        foreach (var track in currentClip.partTracks)
        {
            foreach (var kf in track.keyframes)
            {
                keyframeTimes.Add(kf.normalizedTime);
            }
        }
        
        foreach (float t in keyframeTimes)
        {
            float x = timelineRect.x + 10f + t * scaledWidth - timelineOffset;
            if (x < timelineRect.x || x > timelineRect.xMax) continue;
            
            Rect markerRect = new Rect(x - 4, timelineRect.y + 35f, 8, 20);
            EditorGUI.DrawRect(markerRect, KEYFRAME_COLOR);
        }
        
        // Draw playhead
        float playheadTime = previewController != null ? previewController.normalizedTime : 0f;
        float playheadX = timelineRect.x + 10f + playheadTime * scaledWidth - timelineOffset;
        
        if (playheadX >= timelineRect.x && playheadX <= timelineRect.xMax)
        {
            Handles.color = PLAYHEAD_COLOR;
            Handles.DrawLine(
                new Vector3(playheadX, timelineRect.y, 0),
                new Vector3(playheadX, timelineRect.yMax, 0));
            
            // Playhead handle
            Rect handleRect = new Rect(playheadX - 6, timelineRect.y, 12, 15);
            EditorGUI.DrawRect(handleRect, PLAYHEAD_COLOR);
        }
        
        // Handle playhead dragging
        if (Event.current.type == EventType.MouseDown && timelineRect.Contains(Event.current.mousePosition))
        {
            isDraggingPlayhead = true;
            UpdatePlayheadFromMouse(timelineRect, scaledWidth);
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseDrag && isDraggingPlayhead)
        {
            UpdatePlayheadFromMouse(timelineRect, scaledWidth);
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp && isDraggingPlayhead)
        {
            isDraggingPlayhead = false;
            Event.current.Use();
        }
    }
    
    private void UpdatePlayheadFromMouse(Rect timelineRect, float scaledWidth)
    {
        float mouseX = Event.current.mousePosition.x;
        float t = (mouseX - timelineRect.x - 10f + timelineOffset) / scaledWidth;
        t = Mathf.Clamp01(t);
        
        if (previewController != null)
        {
            previewController.SetTime(t);
        }
        
        Repaint();
    }
    
    private void DrawTrackList()
    {
        if (currentClip == null)
        {
            EditorGUILayout.HelpBox("Select an Animation Clip to edit", MessageType.Info);
            return;
        }
        
        // Track list header
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Body Part", GUILayout.Width(120));
        EditorGUILayout.LabelField("Blend", GUILayout.Width(60));
        EditorGUILayout.LabelField("Keyframes", GUILayout.ExpandWidth(true));
        
        if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(25)))
        {
            AddNewTrack();
        }
        EditorGUILayout.EndHorizontal();
        
        // Track list
        trackScrollPos = EditorGUILayout.BeginScrollView(trackScrollPos);
        
        float contentWidth = position.width - INSPECTOR_WIDTH - 220f;
        float scaledWidth = contentWidth * timelineZoom;
        
        for (int i = 0; i < currentClip.partTracks.Count; i++)
        {
            var track = currentClip.partTracks[i];
            DrawTrack(i, track, scaledWidth);
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private void DrawTrack(int index, AnimationClipSO.PartTrack track, float scaledWidth)
    {
        Rect trackRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.Height(TRACK_HEIGHT));
        
        // Background
        Color bgColor = index == selectedTrackIndex ? TRACK_SELECTED :
            (index % 2 == 0 ? TRACK_BG_EVEN : TRACK_BG_ODD);
        EditorGUI.DrawRect(trackRect, bgColor);
        
        // Track selection
        if (Event.current.type == EventType.MouseDown && trackRect.Contains(Event.current.mousePosition))
        {
            selectedTrackIndex = index;
            selectedKeyframeIndex = -1;
            GUI.FocusControl(null);
            Repaint();
        }
        
        // Body part label
        Rect labelRect = new Rect(trackRect.x + 5, trackRect.y + 5, 115, 20);
        EditorGUI.LabelField(labelRect, track.bodyPart.ToString());
        
        // Blend mode indicator
        Rect blendRect = new Rect(trackRect.x + 125, trackRect.y + 5, 55, 20);
        EditorGUI.LabelField(blendRect, track.blendMode == BlendMode.Additive ? "ADD" : "OVR",
            EditorStyles.miniLabel);
        
        // Keyframe area
        Rect keyframeArea = new Rect(trackRect.x + 185, trackRect.y, trackRect.width - 220f, trackRect.height);
        
        // Draw keyframes
        for (int k = 0; k < track.keyframes.Count; k++)
        {
            var kf = track.keyframes[k];
            float x = keyframeArea.x + kf.normalizedTime * scaledWidth - timelineOffset;
            
            if (x < keyframeArea.x - KEYFRAME_SIZE || x > keyframeArea.xMax + KEYFRAME_SIZE) continue;
            
            Rect kfRect = new Rect(x - KEYFRAME_SIZE / 2, trackRect.y + (trackRect.height - KEYFRAME_SIZE) / 2,
                KEYFRAME_SIZE, KEYFRAME_SIZE);
            
            bool isSelected = index == selectedTrackIndex && k == selectedKeyframeIndex;
            Color kfColor = isSelected ? KEYFRAME_SELECTED : KEYFRAME_COLOR;
            
            // Draw diamond shape
            DrawDiamond(kfRect, kfColor);
            
            // Keyframe selection
            if (Event.current.type == EventType.MouseDown && kfRect.Contains(Event.current.mousePosition))
            {
                selectedTrackIndex = index;
                selectedKeyframeIndex = k;
                isDraggingKeyframe = true;
                dragStartTime = kf.normalizedTime;
                Event.current.Use();
                Repaint();
            }
        }
        
        // Delete track button
        Rect deleteRect = new Rect(trackRect.xMax - 25, trackRect.y + 5, 20, 20);
        if (GUI.Button(deleteRect, "×"))
        {
            if (EditorUtility.DisplayDialog("Delete Track", 
                $"Delete track for {track.bodyPart}?", "Delete", "Cancel"))
            {
                Undo.RecordObject(currentClip, "Delete Track");
                currentClip.partTracks.RemoveAt(index);
                EditorUtility.SetDirty(currentClip);
                if (selectedTrackIndex == index)
                {
                    selectedTrackIndex = -1;
                    selectedKeyframeIndex = -1;
                }
            }
        }
    }
    
    private void DrawDiamond(Rect rect, Color color)
    {
        Vector3 center = rect.center;
        float halfSize = rect.width / 2;
        
        Vector3[] points = new Vector3[]
        {
            center + Vector3.up * halfSize,
            center + Vector3.right * halfSize,
            center + Vector3.down * halfSize,
            center + Vector3.left * halfSize,
        };
        
        Handles.color = color;
        Handles.DrawAAConvexPolygon(points);
        
        Handles.color = Color.black;
        Handles.DrawPolyLine(points[0], points[1], points[2], points[3], points[0]);
    }
    
    private void DrawInspector()
    {
        EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        inspectorScrollPos = EditorGUILayout.BeginScrollView(inspectorScrollPos);
        
        if (currentClip == null)
        {
            EditorGUILayout.HelpBox("No clip selected", MessageType.Info);
        }
        else if (selectedTrackIndex < 0 || selectedTrackIndex >= currentClip.partTracks.Count)
        {
            // Clip properties
            DrawClipInspector();
        }
        else if (selectedKeyframeIndex < 0)
        {
            // Track properties
            DrawTrackInspector();
        }
        else
        {
            // Keyframe properties
            DrawKeyframeInspector();
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private void DrawClipInspector()
    {
        EditorGUILayout.LabelField("Clip Properties", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        currentClip.animationType = (AnimationType)EditorGUILayout.EnumPopup("Animation Type", currentClip.animationType);
        currentClip.duration = EditorGUILayout.FloatField("Duration (seconds)", currentClip.duration);
        currentClip.looping = EditorGUILayout.Toggle("Looping", currentClip.looping);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Blending", EditorStyles.boldLabel);
        currentClip.allowBlendIn = EditorGUILayout.Toggle("Allow Blend In", currentClip.allowBlendIn);
        currentClip.allowBlendOut = EditorGUILayout.Toggle("Allow Blend Out", currentClip.allowBlendOut);
        
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(currentClip);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Tracks: {currentClip.partTracks.Count}");
        
        int totalKeyframes = currentClip.partTracks.Sum(t => t.keyframes.Count);
        EditorGUILayout.LabelField($"Total Keyframes: {totalKeyframes}");
    }
    
    private void DrawTrackInspector()
    {
        var track = currentClip.partTracks[selectedTrackIndex];
        
        EditorGUILayout.LabelField("Track Properties", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        track.bodyPart = (BodyPart)EditorGUILayout.EnumPopup("Body Part", track.bodyPart);
        track.blendMode = (BlendMode)EditorGUILayout.EnumPopup("Blend Mode", track.blendMode);
        track.interpolation = (InterpolationMode)EditorGUILayout.EnumPopup("Default Interpolation", track.interpolation);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animated Properties", EditorStyles.boldLabel);
        
        track.animatedProperties = (AnimatedProperties)EditorGUILayout.EnumFlagsField("Properties", track.animatedProperties);
        
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(currentClip);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Keyframes: {track.keyframes.Count}");
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Add Keyframe at Current Time"))
        {
            AddKeyframeAtCurrentTime(track);
        }
        
        if (GUILayout.Button("Add Keyframe at Start"))
        {
            AddKeyframe(track, 0f);
        }
        
        if (GUILayout.Button("Add Keyframe at End"))
        {
            AddKeyframe(track, 1f);
        }
    }
    
    private void DrawKeyframeInspector()
    {
        var track = currentClip.partTracks[selectedTrackIndex];
        var keyframe = track.keyframes[selectedKeyframeIndex];
        
        EditorGUILayout.LabelField("Keyframe Properties", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Track: {track.bodyPart}", EditorStyles.miniLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        // Time
        keyframe.normalizedTime = EditorGUILayout.Slider("Time", keyframe.normalizedTime, 0f, 1f);
        
        if (currentClip.duration > 0)
        {
            EditorGUILayout.LabelField($"  = {keyframe.normalizedTime * currentClip.duration:F3}s", EditorStyles.miniLabel);
        }
        
        EditorGUILayout.Space();
        
        // Transform properties
        EditorGUILayout.LabelField("Transform", EditorStyles.boldLabel);
        
        bool showPos = (track.animatedProperties & AnimatedProperties.PositionAll) != 0;
        bool showRot = (track.animatedProperties & AnimatedProperties.Rotation) != 0;
        bool showScale = (track.animatedProperties & AnimatedProperties.Scale) != 0;
        bool showImage = (track.animatedProperties & AnimatedProperties.ImageIndex) != 0;
        
        if (showPos)
        {
            keyframe.position = EditorGUILayout.Vector3Field("Position", keyframe.position);
            EditorGUILayout.LabelField("  (X, Y = offset, Z = layer order)", EditorStyles.miniLabel);
        }
        
        if (showRot)
        {
            keyframe.rotation = EditorGUILayout.FloatField("Rotation (degrees)", keyframe.rotation);
        }
        
        if (showScale)
        {
            keyframe.scale = EditorGUILayout.Vector2Field("Scale", keyframe.scale);
            EditorGUILayout.LabelField("  (-1 to flip)", EditorStyles.miniLabel);
        }
        
        if (showImage)
        {
            keyframe.imageIndex = EditorGUILayout.IntField("Image Index", keyframe.imageIndex);
            EditorGUILayout.LabelField("  (-1 = no change)", EditorStyles.miniLabel);
        }
        
        EditorGUILayout.Space();
        
        // Interpolation
        EditorGUILayout.LabelField("Interpolation", EditorStyles.boldLabel);
        keyframe.overrideInterpolation = EditorGUILayout.Toggle("Override Track Default", keyframe.overrideInterpolation);
        
        if (keyframe.overrideInterpolation)
        {
            keyframe.interpolationOverride = (InterpolationMode)EditorGUILayout.EnumPopup("Mode", keyframe.interpolationOverride);
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.EnumPopup("Mode (from track)", track.interpolation);
            EditorGUI.EndDisabledGroup();
        }
        
        if (EditorGUI.EndChangeCheck())
        {
            // Re-sort keyframes by time
            track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
            selectedKeyframeIndex = track.keyframes.IndexOf(keyframe);
            EditorUtility.SetDirty(currentClip);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.Space();
        
        // Quick actions
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            DuplicateKeyframe(track, keyframe);
        }
        
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("Delete"))
        {
            DeleteSelectedKeyframe();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        
        // Copy/Paste
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy"))
        {
            CopyKeyframe(keyframe);
        }
        if (GUILayout.Button("Paste"))
        {
            PasteKeyframe(track);
        }
        EditorGUILayout.EndHorizontal();
    }
    
    private void HandleInput()
    {
        Event e = Event.current;
        
        // Keyframe dragging
        if (isDraggingKeyframe && e.type == EventType.MouseDrag)
        {
            if (selectedTrackIndex >= 0 && selectedKeyframeIndex >= 0)
            {
                var track = currentClip.partTracks[selectedTrackIndex];
                var kf = track.keyframes[selectedKeyframeIndex];
                
                float contentWidth = position.width - INSPECTOR_WIDTH - 220f;
                float scaledWidth = contentWidth * timelineZoom;
                
                float delta = e.delta.x / scaledWidth;
                kf.normalizedTime = Mathf.Clamp01(kf.normalizedTime + delta);
                
                EditorUtility.SetDirty(currentClip);
                Repaint();
            }
            e.Use();
        }
        else if (isDraggingKeyframe && e.type == EventType.MouseUp)
        {
            isDraggingKeyframe = false;
            
            // Re-sort keyframes
            if (selectedTrackIndex >= 0 && selectedKeyframeIndex >= 0)
            {
                var track = currentClip.partTracks[selectedTrackIndex];
                var kf = track.keyframes[selectedKeyframeIndex];
                track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
                selectedKeyframeIndex = track.keyframes.IndexOf(kf);
            }
            
            e.Use();
        }
        
        // Keyboard shortcuts
        if (e.type == EventType.KeyDown)
        {
            switch (e.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (selectedKeyframeIndex >= 0)
                    {
                        DeleteSelectedKeyframe();
                        e.Use();
                    }
                    break;
                    
                case KeyCode.D:
                    if (e.control && selectedKeyframeIndex >= 0)
                    {
                        var track = currentClip.partTracks[selectedTrackIndex];
                        var kf = track.keyframes[selectedKeyframeIndex];
                        DuplicateKeyframe(track, kf);
                        e.Use();
                    }
                    break;
                    
                case KeyCode.Space:
                    if (Application.isPlaying && previewController != null)
                    {
                        if (previewController.isPlaying)
                            previewController.Pause();
                        else
                            previewController.Play();
                        e.Use();
                    }
                    break;
                    
                case KeyCode.LeftArrow:
                    previewController?.PreviousFrame();
                    e.Use();
                    break;
                    
                case KeyCode.RightArrow:
                    previewController?.NextFrame();
                    e.Use();
                    break;
            }
        }
        
        // Mouse wheel zoom
        if (e.type == EventType.ScrollWheel && e.control)
        {
            timelineZoom = Mathf.Clamp(timelineZoom - e.delta.y * 0.1f, 0.5f, 4f);
            e.Use();
            Repaint();
        }
    }
    
    private void AddNewTrack()
    {
        Undo.RecordObject(currentClip, "Add Track");
        
        // Find first body part not already used
        BodyPart newPart = BodyPart.Body;
        var usedParts = currentClip.partTracks.Select(t => t.bodyPart).ToHashSet();
        foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
        {
            if (!usedParts.Contains(part))
            {
                newPart = part;
                break;
            }
        }
        
        var newTrack = new AnimationClipSO.PartTrack
        {
            bodyPart = newPart,
            blendMode = BlendMode.Additive,
            interpolation = InterpolationMode.Linear,
            animatedProperties = AnimatedProperties.All,
            keyframes = new List<AnimationClipSO.Keyframe>()
        };
        
        currentClip.partTracks.Add(newTrack);
        selectedTrackIndex = currentClip.partTracks.Count - 1;
        selectedKeyframeIndex = -1;
        
        EditorUtility.SetDirty(currentClip);
    }
    
    private void AddKeyframeAtCurrentTime(AnimationClipSO.PartTrack track)
    {
        float time = previewController != null ? previewController.normalizedTime : 0f;
        AddKeyframe(track, time);
    }
    
    private void AddKeyframe(AnimationClipSO.PartTrack track, float normalizedTime)
    {
        Undo.RecordObject(currentClip, "Add Keyframe");
        
        var newKf = new AnimationClipSO.Keyframe
        {
            normalizedTime = normalizedTime,
            position = Vector3.zero,
            rotation = 0f,
            scale = Vector2.one,
            imageIndex = -1,
            overrideInterpolation = false
        };
        
        track.keyframes.Add(newKf);
        track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
        selectedKeyframeIndex = track.keyframes.IndexOf(newKf);
        
        EditorUtility.SetDirty(currentClip);
    }
    
    private void DuplicateKeyframe(AnimationClipSO.PartTrack track, AnimationClipSO.Keyframe source)
    {
        Undo.RecordObject(currentClip, "Duplicate Keyframe");
        
        var newKf = new AnimationClipSO.Keyframe
        {
            normalizedTime = Mathf.Clamp01(source.normalizedTime + 0.1f),
            position = source.position,
            rotation = source.rotation,
            scale = source.scale,
            imageIndex = source.imageIndex,
            overrideInterpolation = source.overrideInterpolation,
            interpolationOverride = source.interpolationOverride
        };
        
        track.keyframes.Add(newKf);
        track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
        selectedKeyframeIndex = track.keyframes.IndexOf(newKf);
        
        EditorUtility.SetDirty(currentClip);
    }
    
    private void DeleteSelectedKeyframe()
    {
        if (selectedTrackIndex < 0 || selectedKeyframeIndex < 0) return;
        
        Undo.RecordObject(currentClip, "Delete Keyframe");
        
        var track = currentClip.partTracks[selectedTrackIndex];
        track.keyframes.RemoveAt(selectedKeyframeIndex);
        selectedKeyframeIndex = Mathf.Min(selectedKeyframeIndex, track.keyframes.Count - 1);
        
        EditorUtility.SetDirty(currentClip);
    }
    
    // Copy/Paste support
    private static AnimationClipSO.Keyframe copiedKeyframe;
    
    private void CopyKeyframe(AnimationClipSO.Keyframe kf)
    {
        copiedKeyframe = new AnimationClipSO.Keyframe
        {
            normalizedTime = kf.normalizedTime,
            position = kf.position,
            rotation = kf.rotation,
            scale = kf.scale,
            imageIndex = kf.imageIndex,
            overrideInterpolation = kf.overrideInterpolation,
            interpolationOverride = kf.interpolationOverride
        };
    }
    
    private void PasteKeyframe(AnimationClipSO.PartTrack track)
    {
        if (copiedKeyframe == null) return;
        
        Undo.RecordObject(currentClip, "Paste Keyframe");
        
        float pasteTime = previewController != null ? previewController.normalizedTime : copiedKeyframe.normalizedTime;
        
        var newKf = new AnimationClipSO.Keyframe
        {
            normalizedTime = pasteTime,
            position = copiedKeyframe.position,
            rotation = copiedKeyframe.rotation,
            scale = copiedKeyframe.scale,
            imageIndex = copiedKeyframe.imageIndex,
            overrideInterpolation = copiedKeyframe.overrideInterpolation,
            interpolationOverride = copiedKeyframe.interpolationOverride
        };
        
        track.keyframes.Add(newKf);
        track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
        selectedKeyframeIndex = track.keyframes.IndexOf(newKf);
        
        EditorUtility.SetDirty(currentClip);
    }
}
#endif