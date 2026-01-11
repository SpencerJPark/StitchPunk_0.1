// =====================================
// ANIMATION PREVIEW CONTROLLER
// =====================================

using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

public enum PreviewMode
{
    ClipEdit,
    ClipPreview
}

public class AnimationPreviewController : MonoBehaviour
{
    [Header("Mode")]
    public PreviewMode mode = PreviewMode.ClipEdit;
    
    [Header("Clip Edit Mode")]
    public AnimationClipSO editClip;
    
    [Header("Clip Preview Mode - Layers (index 0 = base, higher = override)")]
    public List<AnimationClipSO> previewLayers = new List<AnimationClipSO>();
    
    [Header("Playback Controls")]
    [Range(0f, 1f)]
    public float normalizedTime = 0f;
    public float playbackSpeed = 1f;
    public bool isPlaying = false;
    public bool loop = true;
    
    [Header("Debug")]
    public bool showDebugLog = true;
    public bool showKeyframeMarkers = true;
    
    [Header("Manual Controls")]
    [SerializeField] private bool forceRebuild = false;
    
    public System.Action<float> OnTimeChanged;
    public System.Action<AnimationClipSO> OnClipChanged;
    
    private EntityManager entityManager;
    private Entity timeControlEntity = Entity.Null;
    private World cachedWorld = null;
    private bool worldInitialized = false;
    
    private float lastSentNormalizedTime = -1f;
    private bool isBeingDestroyed = false;
    private PreviewMode lastMode;
    private int lastPreviewLayerCount = 0;
    private AnimationClipSO lastEditClip;
    private List<AnimationClipSO> lastPreviewLayersList = new List<AnimationClipSO>();
    
    private void Start()
    {
        var editorScene = GetComponent<AnimationEditorScene>();
        if (editorScene != null && editorScene.autoPlayOnStart)
        {
            isPlaying = true;
        }
        
        lastMode = mode;
        lastEditClip = editClip;
        CopyPreviewLayers();
    }
    
    private void CopyPreviewLayers()
    {
        lastPreviewLayersList.Clear();
        foreach (var clip in previewLayers)
        {
            lastPreviewLayersList.Add(clip);
        }
        lastPreviewLayerCount = previewLayers.Count;
    }
    
    private bool PreviewLayersChanged()
    {
        if (previewLayers.Count != lastPreviewLayersList.Count) return true;
        
        for (int i = 0; i < previewLayers.Count; i++)
        {
            if (previewLayers[i] != lastPreviewLayersList[i]) return true;
        }
        
        return false;
    }
    
    private void Update()
    {
        if (!Application.isPlaying || isBeingDestroyed) return;
        
        // Manual force rebuild from inspector
        if (forceRebuild)
        {
            forceRebuild = false;
            if (showDebugLog) Debug.Log("[PreviewController] Force rebuild triggered");
            RebuildLayers();
        }
        
        TryInitializeWorld();
        
        if (!worldInitialized)
        {
            return;
        }
        
        if (!IsWorldValid())
        {
            if (showDebugLog) Debug.LogWarning("[PreviewController] World not valid, resetting");
            worldInitialized = false;
            cachedWorld = null;
            timeControlEntity = Entity.Null;
            return;
        }
        
        // Detect changes
        bool modeChanged = mode != lastMode;
        bool clipChanged = editClip != lastEditClip;
        bool layersChanged = PreviewLayersChanged();
        
        if (modeChanged || clipChanged || layersChanged)
        {
            if (showDebugLog) Debug.Log($"[PreviewController] Change detected - Mode:{modeChanged} Clip:{clipChanged} Layers:{layersChanged}");
            RebuildLayers();
            lastMode = mode;
            lastEditClip = editClip;
            CopyPreviewLayers();
        }
        
        // Update time control
        var timeControl = entityManager.GetComponentData<EditorAnimationTimeControl>(timeControlEntity);
        
        bool userScrubbed = Mathf.Abs(normalizedTime - lastSentNormalizedTime) > 0.0001f && lastSentNormalizedTime >= 0f;
        
        if (userScrubbed)
        {
            if (showDebugLog) Debug.Log($"[PreviewController] User scrubbed to {normalizedTime}");
            timeControl.normalizedTime = normalizedTime;
            SyncTimeToLayers(normalizedTime);
            OnTimeChanged?.Invoke(normalizedTime);
        }
        else
        {
            normalizedTime = timeControl.normalizedTime;
        }
        
        timeControl.isPaused = !isPlaying;
        timeControl.playbackSpeed = playbackSpeed;
        timeControl.forceLoop = loop;
        timeControl.soloLayerIndex = -1;
        
        entityManager.SetComponentData(timeControlEntity, timeControl);
        lastSentNormalizedTime = timeControl.normalizedTime;
        
        // Debug: Show current layer state every few seconds
        if (showDebugLog && Time.frameCount % 300 == 0)
        {
            LogCurrentState();
        }
    }
    
    private void LogCurrentState()
    {
        if (!IsWorldValid()) return;
        
        try
        {
            using var query = entityManager.CreateEntityQuery(typeof(AnimationLayer));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            Debug.Log($"[PreviewController] === STATE ===");
            Debug.Log($"[PreviewController] Mode: {mode}, IsPlaying: {isPlaying}, Time: {normalizedTime:F3}");
            Debug.Log($"[PreviewController] EditClip: {editClip?.name ?? "null"}, PreviewLayers: {previewLayers.Count}");
            
            foreach (var entity in entities)
            {
                var layers = entityManager.GetBuffer<AnimationLayer>(entity);
                Debug.Log($"[PreviewController] Entity has {layers.Length} layers:");
                
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    Debug.Log($"  [{i}] {layer.layer}: {layer.animation}, active={layer.active}, time={layer.time:F3}, speed={layer.speed}");
                }
            }
            
            entities.Dispose();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PreviewController] LogCurrentState error: {e.Message}");
        }
    }
    
    private void RebuildLayers()
    {
        if (!IsWorldValid())
        {
            if (showDebugLog) Debug.LogWarning("[PreviewController] Cannot rebuild - world not valid");
            return;
        }
        
        try
        {
            using var query = entityManager.CreateEntityQuery(typeof(AnimationLayer), typeof(AnimatorTarget));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            if (showDebugLog) Debug.Log($"[PreviewController] RebuildLayers: Found {entities.Length} animator entities");
            
            if (entities.Length == 0)
            {
                Debug.LogError("[PreviewController] No animator entities found! Make sure your character has AnimatorAuthoring component.");
                entities.Dispose();
                return;
            }
            
            foreach (var entity in entities)
            {
                var layers = entityManager.GetBuffer<AnimationLayer>(entity);
                layers.Clear();
                
                if (mode == PreviewMode.ClipEdit)
                {
                    if (editClip != null)
                    {
                        layers.Add(new AnimationLayer
                        {
                            layer = AnimationLayerType.Base,
                            animation = editClip.animationType,
                            time = 0f,
                            speed = 1f,
                            active = true,
                            looping = editClip.looping || loop
                        });
                        
                        if (showDebugLog) Debug.Log($"[PreviewController] Added edit clip: {editClip.animationType} (looping={editClip.looping || loop})");
                    }
                    else
                    {
                        Debug.LogWarning("[PreviewController] ClipEdit mode but no editClip assigned!");
                    }
                }
                else // ClipPreview
                {
                    if (previewLayers.Count == 0)
                    {
                        Debug.LogWarning("[PreviewController] ClipPreview mode but no preview layers assigned!");
                    }
                    
                    for (int i = 0; i < previewLayers.Count; i++)
                    {
                        var clip = previewLayers[i];
                        if (clip == null)
                        {
                            if (showDebugLog) Debug.LogWarning($"[PreviewController] Preview layer {i} is null, skipping");
                            continue;
                        }
                        
                        AnimationLayerType layerType = (AnimationLayerType)Mathf.Min(i, (int)AnimationLayerType.Override);
                        
                        layers.Add(new AnimationLayer
                        {
                            layer = layerType,
                            animation = clip.animationType,
                            time = 0f,
                            speed = 1f,
                            active = true,
                            looping = clip.looping || loop
                        });
                        
                        if (showDebugLog) Debug.Log($"[PreviewController] Added preview layer {i}: {layerType} = {clip.animationType}");
                    }
                }
                
                if (showDebugLog) Debug.Log($"[PreviewController] Final layer count: {layers.Length}");
            }
            
            entities.Dispose();
            
            normalizedTime = 0f;
            lastSentNormalizedTime = -1f;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PreviewController] RebuildLayers error: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private void SyncTimeToLayers(float time)
    {
        if (!IsWorldValid()) return;
        
        try
        {
            using var query = entityManager.CreateEntityQuery(typeof(AnimationLayer));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            foreach (var entity in entities)
            {
                var layers = entityManager.GetBuffer<AnimationLayer>(entity);
                
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    
                    AnimationClipSO clip = GetClipForLayer(i);
                    if (clip != null && clip.duration > 0)
                    {
                        layer.time = time * clip.duration;
                        layers[i] = layer;
                    }
                }
            }
            
            entities.Dispose();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PreviewController] SyncTimeToLayers error: {e.Message}");
        }
    }
    
    private AnimationClipSO GetClipForLayer(int layerIndex)
    {
        if (mode == PreviewMode.ClipEdit)
        {
            return layerIndex == 0 ? editClip : null;
        }
        else
        {
            return layerIndex < previewLayers.Count ? previewLayers[layerIndex] : null;
        }
    }
    
    private bool IsWorldValid()
    {
        if (cachedWorld == null || !cachedWorld.IsCreated) return false;
        
        try
        {
            return entityManager.Exists(timeControlEntity);
        }
        catch
        {
            return false;
        }
    }
    
    private void TryInitializeWorld()
    {
        if (worldInitialized) return;
        
        cachedWorld = World.DefaultGameObjectInjectionWorld;
        if (cachedWorld == null || !cachedWorld.IsCreated)
        {
            return;
        }
        
        entityManager = cachedWorld.EntityManager;
        
        try
        {
            using var timeQuery = entityManager.CreateEntityQuery(typeof(EditorAnimationTimeControl));
            if (timeQuery.CalculateEntityCount() == 0)
            {
                if (showDebugLog) Debug.LogWarning("[PreviewController] No EditorAnimationTimeControl found. Add EditorAnimationTimeControlAuthoring to scene.");
                return;
            }
            
            timeControlEntity = timeQuery.GetSingletonEntity();
            
            using var animQuery = entityManager.CreateEntityQuery(typeof(AnimationLayer), typeof(AnimatorTarget));
            int animCount = animQuery.CalculateEntityCount();
            
            if (animCount == 0)
            {
                if (showDebugLog) Debug.LogWarning("[PreviewController] No animator entities found. Add AnimatorAuthoring to your character.");
                return;
            }
            
            worldInitialized = true;
            
            var timeControl = entityManager.GetComponentData<EditorAnimationTimeControl>(timeControlEntity);
            timeControl.isPaused = !isPlaying;
            timeControl.normalizedTime = normalizedTime;
            timeControl.playbackSpeed = playbackSpeed;
            timeControl.forceLoop = loop;
            timeControl.soloLayerIndex = -1;
            entityManager.SetComponentData(timeControlEntity, timeControl);
            lastSentNormalizedTime = normalizedTime;
            
            if (showDebugLog) Debug.Log($"[PreviewController] Initialized. TimeControl entity found. {animCount} animator entities.");
            
            RebuildLayers();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PreviewController] TryInitializeWorld error: {e.Message}\n{e.StackTrace}");
            worldInitialized = false;
        }
    }
    
    public void Play()
    {
        isPlaying = true;
        if (showDebugLog) Debug.Log("[PreviewController] Play");
    }
    
    public void Pause()
    {
        isPlaying = false;
        if (showDebugLog) Debug.Log("[PreviewController] Pause");
    }
    
    public void Stop()
    {
        isPlaying = false;
        normalizedTime = 0f;
        lastSentNormalizedTime = -1f;
        SyncTimeToLayers(0f);
        if (showDebugLog) Debug.Log("[PreviewController] Stop");
    }
    
    public void SetTime(float time)
    {
        normalizedTime = Mathf.Clamp01(time);
        lastSentNormalizedTime = -1f;
        SyncTimeToLayers(normalizedTime);
        OnTimeChanged?.Invoke(normalizedTime);
        if (showDebugLog) Debug.Log($"[PreviewController] SetTime: {normalizedTime}");
    }
    
    public void NextFrame()
    {
        AnimationClipSO clip = mode == PreviewMode.ClipEdit ? editClip : (previewLayers.Count > 0 ? previewLayers[0] : null);
        if (clip == null || clip.partTracks == null || clip.partTracks.Count == 0) return;
        
        float nextTime = 1f;
        foreach (var track in clip.partTracks)
        {
            if (track.keyframes == null) continue;
            foreach (var kf in track.keyframes)
            {
                if (kf.normalizedTime > normalizedTime + 0.001f && kf.normalizedTime < nextTime)
                {
                    nextTime = kf.normalizedTime;
                }
            }
        }
        SetTime(nextTime);
    }
    
    public void PreviousFrame()
    {
        AnimationClipSO clip = mode == PreviewMode.ClipEdit ? editClip : (previewLayers.Count > 0 ? previewLayers[0] : null);
        if (clip == null || clip.partTracks == null || clip.partTracks.Count == 0) return;
        
        float prevTime = 0f;
        foreach (var track in clip.partTracks)
        {
            if (track.keyframes == null) continue;
            foreach (var kf in track.keyframes)
            {
                if (kf.normalizedTime < normalizedTime - 0.001f && kf.normalizedTime > prevTime)
                {
                    prevTime = kf.normalizedTime;
                }
            }
        }
        SetTime(prevTime);
    }
    
    public void SetEditClip(AnimationClipSO clip)
    {
        editClip = clip;
        mode = PreviewMode.ClipEdit;
        if (showDebugLog) Debug.Log($"[PreviewController] SetEditClip: {clip?.name}");
        OnClipChanged?.Invoke(clip);
    }
    
    public void AddPreviewLayer(AnimationClipSO clip)
    {
        if (clip == null) return;
        previewLayers.Add(clip);
    }
    
    public void RemovePreviewLayer(int index)
    {
        if (index < 0 || index >= previewLayers.Count) return;
        previewLayers.RemoveAt(index);
    }
    
    public void ClearPreviewLayers()
    {
        previewLayers.Clear();
    }
    
    public List<AnimationLayer> GetCurrentLayers()
    {
        var result = new List<AnimationLayer>();
        
        if (!IsWorldValid()) return result;
        
        try
        {
            using var query = entityManager.CreateEntityQuery(typeof(AnimationLayer));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            if (entities.Length > 0)
            {
                var layers = entityManager.GetBuffer<AnimationLayer>(entities[0]);
                for (int i = 0; i < layers.Length; i++)
                {
                    result.Add(layers[i]);
                }
            }
            
            entities.Dispose();
        }
        catch
        {
            // Ignore
        }
        
        return result;
    }
    
    private void OnDestroy()
    {
        isBeingDestroyed = true;
        
        if (IsWorldValid())
        {
            try
            {
                entityManager.SetComponentData(timeControlEntity, EditorAnimationTimeControl.Default);
            }
            catch
            {
                // Ignore
            }
        }
    }
    
    private void OnApplicationQuit()
    {
        isBeingDestroyed = true;
    }
    
    private void OnValidate()
    {
        // When values change in inspector during play mode, trigger rebuild
        if (Application.isPlaying && worldInitialized)
        {
            // Can't call RebuildLayers directly from OnValidate, so set flag
            forceRebuild = true;
        }
    }
}