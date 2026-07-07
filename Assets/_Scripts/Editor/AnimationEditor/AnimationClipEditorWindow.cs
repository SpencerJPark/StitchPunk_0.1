// =====================================
// CUSTOM EDITOR WINDOW (Fixed for new layer system)
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
    private float cachedPlayheadTime = 0f;
    
    // UI State
    private Vector2 trackScrollPos;
    private Vector2 inspectorScrollPos;
    private float timelineZoom = 1f;
    private float timelineOffset = 0f;
    
    // Dragging state
    private bool isDraggingKeyframe = false;
    private bool isDraggingPlayhead = false;
    
    // Layout constants
    private const float TIMELINE_HEIGHT = 60f;
    private const float TRACK_HEIGHT = 30f;
    private const float TRACK_HEADER_WIDTH = 180f;
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
    
    private GUIStyle cachedStatusStyle;

    // Cached timeline rect for input handling
    private float cachedTimelineStartX;
    private float cachedScaledWidth;
    private Rect lastTimelineRect;
    private Rect lastTrackAreaRect;
    
    [MenuItem("Window/Animation/Clip Editor")]
    [MenuItem("Window/Stitch Punk/Animation Clip Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<AnimationClipEditorWindow>("Animation Clip Editor");
        window.minSize = new Vector2(800, 500);
    }
    
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += OnEditorUpdate;
        FindPreviewController();
    }
    
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.update -= OnEditorUpdate;
    }
    
    // Repaint cadence for the playhead while previewing. Play mode drives the player loop by
    // itself — never call QueuePlayerLoopUpdate here, it stacks extra simulation ticks on top of
    // the normal loop and multiplies the cost of every system in the preview world.
    private const double RepaintIntervalSeconds = 1.0 / 20.0;
    private double lastRepaintTime;

    private void OnEditorUpdate()
    {
        if (previewController == null) return;

        double now = EditorApplication.timeSinceStartup;
        if (now - lastRepaintTime < RepaintIntervalSeconds) return;

        lastRepaintTime = now;
        Repaint();
    }
    
    private void OnInspectorUpdate()
    {
        if (Application.isPlaying)
        {
            Repaint();
        }
    }
    
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += FindPreviewController;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            previewController = null;
        }
    }
    
    private void FindPreviewController()
    {
        previewController = FindAnyObjectByType<AnimationPreviewController>();
        
        if (previewController != null)
        {
            Debug.Log($"[AnimationClipEditor] Found preview controller: {previewController.name}");
            
            previewController.OnTimeChanged -= OnPreviewTimeChanged;
            previewController.OnTimeChanged += OnPreviewTimeChanged;
            
            previewController.OnClipChanged -= OnPreviewClipChanged;
            previewController.OnClipChanged += OnPreviewClipChanged;
            
            if (currentClip != null && previewController.editClip != currentClip)
            {
                previewController.SetEditClip(currentClip);
            }
            
            if (Application.isPlaying) {
                previewController.SetTime(cachedPlayheadTime);
            }
        }
        else if (Application.isPlaying)
        {
            Debug.LogWarning("[AnimationClipEditor] Could not find AnimationPreviewController in scene");
        }
    }
    
    private void OnPreviewTimeChanged(float time)
    {
        Repaint();
    }
    
    private void OnPreviewClipChanged(AnimationClipSO clip)
    {
        if (clip != currentClip)
        {
            currentClip = clip;
            selectedTrackIndex = -1;
            selectedKeyframeIndex = -1;
            Repaint();
        }
    }
    
    private void OnGUI()
    {
        if (Application.isPlaying && previewController == null)
        {
            FindPreviewController();
        }
        
        EditorGUILayout.BeginHorizontal();
        
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        DrawToolbar();
        DrawTimeline();
        DrawTrackList();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.BeginVertical(GUILayout.Width(INSPECTOR_WIDTH));
        DrawInspector();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndHorizontal();
        
        HandleGlobalInput();
    }
    
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // Clip selector
        EditorGUI.BeginChangeCheck();
        var newClip = (AnimationClipSO)EditorGUILayout.ObjectField(
            currentClip, typeof(AnimationClipSO), false, GUILayout.Width(200));
        if (EditorGUI.EndChangeCheck())
        {
            currentClip = newClip;
            selectedTrackIndex = -1;
            selectedKeyframeIndex = -1;
            
            if (previewController != null)
            {
                previewController.SetEditClip(currentClip);
            }
        }
        
        GUILayout.Space(20);
        
        bool hasController = Application.isPlaying && previewController != null;
        
        if (!Application.isPlaying)
        {
            EditorGUILayout.LabelField("Enter Play Mode to preview", EditorStyles.miniLabel, GUILayout.Width(150));
        }
        else if (previewController == null)
        {
            if (GUILayout.Button("Find Controller", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                FindPreviewController();
            }
        }
        else
        {
            if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(25)))
            {
                previewController.Stop();
            }
            
            if (GUILayout.Button("⏪", EditorStyles.toolbarButton, GUILayout.Width(25)))
            {
                previewController.PreviousFrame();
            }
            
            bool isPlaying = previewController.isPlaying;
            if (GUILayout.Button(isPlaying ? "⏸" : "▶", EditorStyles.toolbarButton, GUILayout.Width(25)))
            {
                if (isPlaying)
                    previewController.Pause();
                else
                    previewController.Play();
            }
            
            if (GUILayout.Button("⏩", EditorStyles.toolbarButton, GUILayout.Width(25)))
            {
                previewController.NextFrame();
            }
        }
        
        GUILayout.Space(20);
        
        // Time display - always get fresh from controller
        float currentTime = 0f;
        if (previewController != null)
        {
            currentTime = previewController.normalizedTime;
        }
        float duration = (currentClip != null) ? currentClip.duration : 1f;
        
        string timeText = $"Time: {currentTime:F3} ({currentTime * duration:F2}s)";
        EditorGUILayout.LabelField(timeText, GUILayout.Width(160));
        
        if (hasController)
        {
            if (cachedStatusStyle == null)
            {
                cachedStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            }
            cachedStatusStyle.normal.textColor = previewController.isPlaying ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(previewController.isPlaying ? "▶ PLAYING" : "⏸ PAUSED", cachedStatusStyle, GUILayout.Width(80));
        }
        
        GUILayout.FlexibleSpace();
        
        EditorGUILayout.LabelField("Zoom:", GUILayout.Width(40));
        timelineZoom = EditorGUILayout.Slider(timelineZoom, 0.5f, 4f, GUILayout.Width(100));
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawTimeline()
    {
        Rect timelineRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, 
            GUILayout.ExpandWidth(true), GUILayout.Height(TIMELINE_HEIGHT));
        
        lastTimelineRect = timelineRect;
        
        EditorGUI.DrawRect(timelineRect, TIMELINE_BG);
        
        if (currentClip == null)
        {
            GUI.Label(timelineRect, "Select a clip to edit", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        
        float timelineStartX = timelineRect.x + TRACK_HEADER_WIDTH;
        float timelineWidth = timelineRect.width - TRACK_HEADER_WIDTH - 20f;
        float scaledWidth = timelineWidth * timelineZoom;
        
        cachedTimelineStartX = timelineStartX;
        cachedScaledWidth = scaledWidth;
        
        Rect headerRect = new Rect(timelineRect.x, timelineRect.y, TRACK_HEADER_WIDTH, timelineRect.height);
        EditorGUI.DrawRect(headerRect, new Color(0.15f, 0.15f, 0.15f));
        GUI.Label(new Rect(headerRect.x + 10, headerRect.y + 20, 100, 20), "Timeline", EditorStyles.boldLabel);
        
        Handles.color = Color.gray;
        int numMarkers = Mathf.CeilToInt(10 * timelineZoom);
        for (int i = 0; i <= numMarkers; i++)
        {
            float t = i / (float)numMarkers;
            float x = timelineStartX + t * scaledWidth - timelineOffset;
            
            if (x < timelineStartX || x > timelineRect.xMax - 20f) continue;
            
            Handles.DrawLine(
                new Vector3(x, timelineRect.y + 35f, 0),
                new Vector3(x, timelineRect.yMax, 0));
            
            GUI.Label(new Rect(x - 15, timelineRect.y + 15f, 30, 20), 
                $"{t:F1}", EditorStyles.centeredGreyMiniLabel);
        }
        
        if (currentClip.partTracks != null)
        {
            HashSet<float> keyframeTimes = new HashSet<float>();
            foreach (var track in currentClip.partTracks)
            {
                if (track.keyframes == null) continue;
                foreach (var kf in track.keyframes)
                {
                    keyframeTimes.Add(kf.normalizedTime);
                }
            }
            
            foreach (float t in keyframeTimes)
            {
                float x = timelineStartX + t * scaledWidth - timelineOffset;
                if (x < timelineStartX || x > timelineRect.xMax - 20f) continue;
                
                Rect markerRect = new Rect(x - 3, timelineRect.y + 40f, 6, 15);
                EditorGUI.DrawRect(markerRect, KEYFRAME_COLOR);
            }
        }
        
        // PLAYHEAD - Get time directly from preview controller
        float playheadTime;

        if (isDraggingPlayhead)
        {
            playheadTime = cachedPlayheadTime;
        }
        else if (previewController != null)
        {
            // If we are playing, trust the controller. 
            // If paused, trust our cached value until the controller confirms change.
            if (previewController.isPlaying)
            {
                playheadTime = previewController.normalizedTime;
                cachedPlayheadTime = playheadTime;
            }
            else
            {
                playheadTime = cachedPlayheadTime;
            }
        }
        else
        {
            playheadTime = cachedPlayheadTime;
        }
        
        float playheadX = timelineStartX + playheadTime * scaledWidth - timelineOffset;
        
        // Always draw playhead if in valid range
        if (playheadX >= timelineStartX - 10 && playheadX <= timelineRect.xMax)
        {
            // Clamp for drawing
            float drawX = Mathf.Clamp(playheadX, timelineStartX, timelineRect.xMax - 20f);
            
            // Playhead line
            Handles.color = PLAYHEAD_COLOR;
            Handles.DrawLine(
                new Vector3(drawX, timelineRect.y, 0),
                new Vector3(drawX, timelineRect.yMax + 500f, 0));
            
            // Playhead handle
            Vector3[] trianglePoints = new Vector3[]
            {
                new Vector3(drawX - 8, timelineRect.y + 5, 0),
                new Vector3(drawX + 8, timelineRect.y + 5, 0),
                new Vector3(drawX + 8, timelineRect.y + 15, 0),
                new Vector3(drawX, timelineRect.y + 25, 0),
                new Vector3(drawX - 8, timelineRect.y + 15, 0),
            };
            Handles.color = PLAYHEAD_COLOR;
            Handles.DrawAAConvexPolygon(trianglePoints);
            
            // Debug: Show current time value
            GUI.Label(new Rect(drawX - 20, timelineRect.y + 28, 40, 15), 
                $"{playheadTime:F2}", EditorStyles.centeredGreyMiniLabel);
        }
        
        // Handle timeline clicking
        Rect clickableTimelineRect = new Rect(timelineStartX, timelineRect.y, timelineWidth, timelineRect.height);
        
        Event e = Event.current;
        
        if (e.type == EventType.MouseDown && e.button == 0 && clickableTimelineRect.Contains(e.mousePosition))
        {
            isDraggingPlayhead = true;
            UpdatePlayheadFromMouse(e.mousePosition.x);
            e.Use();
            Repaint();
        }
    }
    
    private void UpdatePlayheadFromMouse(float mouseX)
    {
        if (cachedScaledWidth <= 0) return;

        float t = (mouseX - cachedTimelineStartX + timelineOffset) / cachedScaledWidth;
        t = Mathf.Clamp01(t);
    
        // Update local cache immediately
        cachedPlayheadTime = t;

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
        
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Body Part", GUILayout.Width(100));
        EditorGUILayout.LabelField("Blend", GUILayout.Width(50));
        EditorGUILayout.LabelField("Keyframes", GUILayout.ExpandWidth(true));
        
        if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(25)))
        {
            AddNewTrack();
        }
        EditorGUILayout.EndHorizontal();
        
        trackScrollPos = EditorGUILayout.BeginScrollView(trackScrollPos);
        
        float trackAreaWidth = position.width - INSPECTOR_WIDTH - 20f;
        float timelineStartX = TRACK_HEADER_WIDTH;
        float timelineWidth = trackAreaWidth - TRACK_HEADER_WIDTH;
        float scaledWidth = timelineWidth * timelineZoom;
        
        if (currentClip.partTracks == null)
        {
            currentClip.partTracks = new List<AnimationClipSO.PartTrack>();
        }
        
        lastTrackAreaRect = GUILayoutUtility.GetRect(trackAreaWidth, 
            Mathf.Max(200f, currentClip.partTracks.Count * TRACK_HEIGHT));
        
        for (int i = 0; i < currentClip.partTracks.Count; i++)
        {
            var track = currentClip.partTracks[i];
            Rect trackRect = new Rect(lastTrackAreaRect.x, lastTrackAreaRect.y + i * TRACK_HEIGHT, 
                trackAreaWidth, TRACK_HEIGHT);
            DrawTrack(i, track, trackRect, timelineStartX, scaledWidth);
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private void DrawTrack(int index, AnimationClipSO.PartTrack track, Rect trackRect, float timelineStartX, float scaledWidth)
    {
        Color bgColor = index == selectedTrackIndex ? TRACK_SELECTED :
            (index % 2 == 0 ? TRACK_BG_EVEN : TRACK_BG_ODD);
        EditorGUI.DrawRect(trackRect, bgColor);
        
        Rect headerRect = new Rect(trackRect.x, trackRect.y, TRACK_HEADER_WIDTH, trackRect.height);
        
        Rect labelRect = new Rect(trackRect.x + 5, trackRect.y + 5, 90, 20);
        EditorGUI.LabelField(labelRect, track.animationTarget.ToString());
        
        Rect blendRect = new Rect(trackRect.x + 100, trackRect.y + 5, 40, 20);
        EditorGUI.LabelField(blendRect, track.blendMode == BlendMode.Additive ? "ADD" : "OVR",
            EditorStyles.miniLabel);
        
        Rect deleteRect = new Rect(trackRect.x + 145, trackRect.y + 5, 20, 20);
        if (GUI.Button(deleteRect, "×", EditorStyles.miniButton))
        {
            if (EditorUtility.DisplayDialog("Delete Track", 
                $"Delete track for {track.animationTarget}?", "Delete", "Cancel"))
            {
                Undo.RecordObject(currentClip, "Delete Track");
                currentClip.partTracks.RemoveAt(index);
                EditorUtility.SetDirty(currentClip);
                if (selectedTrackIndex == index)
                {
                    selectedTrackIndex = -1;
                    selectedKeyframeIndex = -1;
                }
                return;
            }
        }
        
        Event e = Event.current;
        if (e.type == EventType.MouseDown && headerRect.Contains(e.mousePosition))
        {
            selectedTrackIndex = index;
            selectedKeyframeIndex = -1;
            GUI.FocusControl(null);
            e.Use();
            Repaint();
        }
        
        Rect keyframeAreaRect = new Rect(trackRect.x + timelineStartX, trackRect.y, 
            trackRect.width - timelineStartX, trackRect.height);
        
        Handles.color = new Color(0.1f, 0.1f, 0.1f);
        Handles.DrawLine(
            new Vector3(keyframeAreaRect.x, trackRect.y, 0),
            new Vector3(keyframeAreaRect.x, trackRect.yMax, 0));
        
        if (track.keyframes != null)
        {
            for (int k = 0; k < track.keyframes.Count; k++)
            {
                var kf = track.keyframes[k];
                float x = keyframeAreaRect.x + kf.normalizedTime * scaledWidth - timelineOffset;
                
                if (x < keyframeAreaRect.x - KEYFRAME_SIZE || x > keyframeAreaRect.xMax + KEYFRAME_SIZE) 
                    continue;
                
                Rect kfRect = new Rect(
                    x - KEYFRAME_SIZE / 2, 
                    trackRect.y + (trackRect.height - KEYFRAME_SIZE) / 2,
                    KEYFRAME_SIZE, 
                    KEYFRAME_SIZE);
                
                bool isSelected = index == selectedTrackIndex && k == selectedKeyframeIndex;
                Color kfColor = isSelected ? KEYFRAME_SELECTED : KEYFRAME_COLOR;
                
                DrawDiamond(kfRect, kfColor);
                
                if (e.type == EventType.MouseDown && kfRect.Contains(e.mousePosition))
                {
                    selectedTrackIndex = index;
                    selectedKeyframeIndex = k;
                    isDraggingKeyframe = true;
                    e.Use();
                    Repaint();
                }
            }
        }
        
        if (e.type == EventType.MouseDown && e.clickCount == 2 && keyframeAreaRect.Contains(e.mousePosition))
        {
            float clickTime = (e.mousePosition.x - keyframeAreaRect.x + timelineOffset) / scaledWidth;
            clickTime = Mathf.Clamp01(clickTime);
            
            selectedTrackIndex = index;
            AddKeyframe(track, clickTime);
            e.Use();
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
            DrawClipInspector();
        }
        else if (selectedKeyframeIndex < 0 || selectedKeyframeIndex >= currentClip.partTracks[selectedTrackIndex].keyframes.Count)
        {
            DrawTrackInspector();
        }
        else
        {
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
        EditorGUILayout.LabelField($"Tracks: {currentClip.partTracks?.Count ?? 0}");
        
        int totalKeyframes = currentClip.partTracks?.Sum(t => t.keyframes?.Count ?? 0) ?? 0;
        EditorGUILayout.LabelField($"Total Keyframes: {totalKeyframes}");
        
        EditorGUILayout.Space();
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Add New Track"))
        {
            AddNewTrack();
        }
    }
    
    private void DrawTrackInspector()
    {
        var track = currentClip.partTracks[selectedTrackIndex];
        
        EditorGUILayout.LabelField("Track Properties", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        track.animationTarget = (AnimationTarget)EditorGUILayout.EnumPopup("Body Part", track.animationTarget);
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
        EditorGUILayout.LabelField($"Keyframes: {track.keyframes?.Count ?? 0}");
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Add Keyframe", EditorStyles.boldLabel);
        
        if (GUILayout.Button("At Current Time"))
        {
            float time = previewController != null ? previewController.normalizedTime : 0f;
            AddKeyframe(track, time);
        }
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("At Start (0)"))
        {
            AddKeyframe(track, 0f);
        }
        if (GUILayout.Button("At End (1)"))
        {
            AddKeyframe(track, 1f);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tip: Double-click in track to add keyframe", EditorStyles.miniLabel);
    }
    
    private void DrawKeyframeInspector()
    {
        var track = currentClip.partTracks[selectedTrackIndex];
        var keyframe = track.keyframes[selectedKeyframeIndex];
        
        EditorGUILayout.LabelField("Keyframe Properties", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Track: {track.animationTarget}", EditorStyles.miniLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        float newTime = EditorGUILayout.Slider("Time", keyframe.normalizedTime, 0f, 1f);
        
        if (currentClip.duration > 0)
        {
            EditorGUILayout.LabelField($"  = {keyframe.normalizedTime * currentClip.duration:F3}s", EditorStyles.miniLabel);
        }
        
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("Transform", EditorStyles.boldLabel);
        
        AnimatedProperties props = track.animatedProperties;
        
        if ((props & AnimatedProperties.PositionAll) != 0)
        {
            keyframe.position = EditorGUILayout.Vector3Field("Position", keyframe.position);
            EditorGUILayout.LabelField("  (X, Y = offset, Z = layer order)", EditorStyles.miniLabel);
        }
        
        if ((props & AnimatedProperties.Rotation) != 0)
        {
            keyframe.rotation = EditorGUILayout.FloatField("Rotation (degrees)", keyframe.rotation);
        }
        
        if ((props & AnimatedProperties.Scale) != 0)
        {
            keyframe.scale = EditorGUILayout.Vector2Field("Scale", keyframe.scale);
            EditorGUILayout.LabelField("  (-1 to flip)", EditorStyles.miniLabel);
        }
        
        if ((props & AnimatedProperties.ImageIndex) != 0)
        {
            keyframe.imageIndex = EditorGUILayout.IntField("Image Index", keyframe.imageIndex);
            EditorGUILayout.LabelField("  (-1 = no change)", EditorStyles.miniLabel);
        }
        
        EditorGUILayout.Space();
        
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
        
        bool changed = EditorGUI.EndChangeCheck();
        
        if (Mathf.Abs(newTime - keyframe.normalizedTime) > 0.0001f)
        {
            keyframe.normalizedTime = newTime;
            track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
            selectedKeyframeIndex = track.keyframes.IndexOf(keyframe);
            changed = true;
        }
        
        if (changed)
        {
            EditorUtility.SetDirty(currentClip);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
        {
            DuplicateKeyframe(track, keyframe);
        }
        if (GUILayout.Button("Go To"))
        {
            if (previewController != null)
            {
                previewController.SetTime(keyframe.normalizedTime);
            }
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy"))
        {
            CopyKeyframe(keyframe);
        }
        if (GUILayout.Button("Paste Values"))
        {
            PasteKeyframeValues(keyframe);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("Delete Keyframe"))
        {
            DeleteSelectedKeyframe();
        }
        GUI.backgroundColor = Color.white;
    }
    
    private void HandleGlobalInput()
    {
        Event e = Event.current;
        
        // Playhead dragging - handle this FIRST and globally
        if (isDraggingPlayhead)
        {
            if (e.type == EventType.MouseDrag)
            {
                UpdatePlayheadFromMouse(e.mousePosition.x);
                e.Use();
            }
            else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                isDraggingPlayhead = false;
                e.Use();
            }
            
            // While dragging playhead, don't process other inputs
            if (e.type == EventType.MouseDrag || e.type == EventType.MouseUp)
            {
                return;
            }
        }
        
        if (isDraggingKeyframe)
        {
            if (e.type == EventType.MouseDrag)
            {
                if (selectedTrackIndex >= 0 && selectedKeyframeIndex >= 0 &&
                    currentClip != null && selectedTrackIndex < currentClip.partTracks.Count)
                {
                    var track = currentClip.partTracks[selectedTrackIndex];
                    if (track.keyframes != null && selectedKeyframeIndex < track.keyframes.Count)
                    {
                        var kf = track.keyframes[selectedKeyframeIndex];
                        
                        float timelineWidth = position.width - INSPECTOR_WIDTH - TRACK_HEADER_WIDTH - 40f;
                        float scaledWidth = timelineWidth * timelineZoom;
                        
                        if (scaledWidth > 0)
                        {
                            float delta = e.delta.x / scaledWidth;
                            kf.normalizedTime = Mathf.Clamp01(kf.normalizedTime + delta);
                            
                            EditorUtility.SetDirty(currentClip);
                            Repaint();
                        }
                    }
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                isDraggingKeyframe = false;
                
                if (selectedTrackIndex >= 0 && selectedKeyframeIndex >= 0 && currentClip != null)
                {
                    var track = currentClip.partTracks[selectedTrackIndex];
                    if (track.keyframes != null && selectedKeyframeIndex < track.keyframes.Count)
                    {
                        var kf = track.keyframes[selectedKeyframeIndex];
                        track.keyframes = track.keyframes.OrderBy(k => k.normalizedTime).ToList();
                        selectedKeyframeIndex = track.keyframes.IndexOf(kf);
                    }
                }
                
                e.Use();
            }
        }
        
        if (e.type == EventType.KeyDown)
        {
            bool used = false;
            
            switch (e.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (selectedKeyframeIndex >= 0)
                    {
                        DeleteSelectedKeyframe();
                        used = true;
                    }
                    break;
                    
                case KeyCode.D:
                    if (e.control && selectedKeyframeIndex >= 0 && selectedTrackIndex >= 0 && currentClip != null)
                    {
                        var track = currentClip.partTracks[selectedTrackIndex];
                        if (track.keyframes != null && selectedKeyframeIndex < track.keyframes.Count)
                        {
                            var kf = track.keyframes[selectedKeyframeIndex];
                            DuplicateKeyframe(track, kf);
                            used = true;
                        }
                    }
                    break;
                    
                case KeyCode.Space:
                    if (Application.isPlaying && previewController != null)
                    {
                        if (previewController.isPlaying)
                            previewController.Pause();
                        else
                            previewController.Play();
                        used = true;
                    }
                    break;
                    
                case KeyCode.LeftArrow:
                    if (Application.isPlaying && previewController != null)
                    {
                        previewController.PreviousFrame();
                        used = true;
                    }
                    break;
                    
                case KeyCode.RightArrow:
                    if (Application.isPlaying && previewController != null)
                    {
                        previewController.NextFrame();
                        used = true;
                    }
                    break;
                    
                case KeyCode.Home:
                    if (Application.isPlaying && previewController != null)
                    {
                        previewController.SetTime(0f);
                        used = true;
                    }
                    break;
                    
                case KeyCode.End:
                    if (Application.isPlaying && previewController != null)
                    {
                        previewController.SetTime(1f);
                        used = true;
                    }
                    break;
            }
            
            if (used)
            {
                e.Use();
                Repaint();
            }
        }
        
        if (e.type == EventType.ScrollWheel && e.control)
        {
            timelineZoom = Mathf.Clamp(timelineZoom - e.delta.y * 0.1f, 0.5f, 4f);
            e.Use();
            Repaint();
        }
    }
    
    private void AddNewTrack()
    {
        if (currentClip == null) return;
        
        Undo.RecordObject(currentClip, "Add Track");
        
        if (currentClip.partTracks == null)
        {
            currentClip.partTracks = new List<AnimationClipSO.PartTrack>();
        }
        
        AnimationTarget newPart = AnimationTarget.Body;
        var usedParts = currentClip.partTracks.Select(t => t.animationTarget).ToHashSet();
        foreach (AnimationTarget part in System.Enum.GetValues(typeof(AnimationTarget)))
        {
            if (!usedParts.Contains(part))
            {
                newPart = part;
                break;
            }
        }
        
        var newTrack = new AnimationClipSO.PartTrack
        {
            animationTarget = newPart,
            blendMode = BlendMode.Additive,
            interpolation = InterpolationMode.Linear,
            animatedProperties = AnimatedProperties.All,
            keyframes = new List<AnimationClipSO.Keyframe>()
        };
        
        currentClip.partTracks.Add(newTrack);
        selectedTrackIndex = currentClip.partTracks.Count - 1;
        selectedKeyframeIndex = -1;
        
        EditorUtility.SetDirty(currentClip);
        Repaint();
    }
    
    private void AddKeyframe(AnimationClipSO.PartTrack track, float normalizedTime)
    {
        Undo.RecordObject(currentClip, "Add Keyframe");
        
        if (track.keyframes == null)
        {
            track.keyframes = new List<AnimationClipSO.Keyframe>();
        }
        
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
        Repaint();
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
        Repaint();
    }
    
    private void DeleteSelectedKeyframe()
    {
        if (selectedTrackIndex < 0 || selectedKeyframeIndex < 0) return;
        if (selectedTrackIndex >= currentClip.partTracks.Count) return;
        
        var track = currentClip.partTracks[selectedTrackIndex];
        if (selectedKeyframeIndex >= track.keyframes.Count) return;
        
        Undo.RecordObject(currentClip, "Delete Keyframe");
        
        track.keyframes.RemoveAt(selectedKeyframeIndex);
        selectedKeyframeIndex = Mathf.Min(selectedKeyframeIndex, track.keyframes.Count - 1);
        
        EditorUtility.SetDirty(currentClip);
        Repaint();
    }
    
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
        
        Debug.Log("[AnimationClipEditor] Keyframe copied");
    }
    
    private void PasteKeyframeValues(AnimationClipSO.Keyframe target)
    {
        if (copiedKeyframe == null)
        {
            Debug.LogWarning("[AnimationClipEditor] No keyframe copied");
            return;
        }
        
        Undo.RecordObject(currentClip, "Paste Keyframe Values");
        
        target.position = copiedKeyframe.position;
        target.rotation = copiedKeyframe.rotation;
        target.scale = copiedKeyframe.scale;
        target.imageIndex = copiedKeyframe.imageIndex;
        target.overrideInterpolation = copiedKeyframe.overrideInterpolation;
        target.interpolationOverride = copiedKeyframe.interpolationOverride;
        
        EditorUtility.SetDirty(currentClip);
        Repaint();
        
        Debug.Log("[AnimationClipEditor] Keyframe values pasted");
    }
}
#endif