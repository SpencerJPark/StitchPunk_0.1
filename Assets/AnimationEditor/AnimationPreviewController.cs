// =====================================
// ANIMATION PREVIEW CONTROLLER (Fixed)
// =====================================

using UnityEngine;
using Unity.Entities;

/// <summary>
/// MonoBehaviour bridge between Unity Editor UI and ECS animation systems.
/// Controls the EditorAnimationTimeControl singleton.
/// </summary>
public class AnimationPreviewController : MonoBehaviour
{
    [Header("Current Preview State")]
    public AnimationClipSO currentClip;
    public AnimationType currentAnimation;
    
    [Header("Playback Controls")]
    [Range(0f, 1f)]
    public float normalizedTime = 0f;
    public float playbackSpeed = 1f;
    public bool isPlaying = false;
    public bool loop = true;
    
    [Header("Debug Display")]
    public bool showKeyframeMarkers = true;
    
    // Events for editor window
    public System.Action<float> OnTimeChanged;
    public System.Action<AnimationClipSO> OnClipChanged;
    
    // Internal state
    private EntityManager entityManager;
    private Entity timeControlEntity = Entity.Null;
    private World cachedWorld = null;
    private bool worldInitialized = false;
    
    // Track what we last sent to ECS to detect external changes
    private float lastSentNormalizedTime = -1f;
    private bool isBeingDestroyed = false;
    
    private void Start()
    {
        var editorScene = GetComponent<AnimationEditorScene>();
        if (editorScene != null && editorScene.autoPlayOnStart)
        {
            currentAnimation = editorScene.defaultAnimation;
            isPlaying = true;
        }
    }
    
    private void Update()
    {
        if (!Application.isPlaying || isBeingDestroyed) return;
        
        TryInitializeWorld();
        if (!worldInitialized) return;
        
        // Validate world and entity are still valid
        if (!IsWorldValid())
        {
            worldInitialized = false;
            cachedWorld = null;
            timeControlEntity = Entity.Null;
            return;
        }
        
        var timeControl = entityManager.GetComponentData<EditorAnimationTimeControl>(timeControlEntity);
        
        // Check if user changed normalizedTime in inspector/timeline (scrubbing)
        bool userScrubbed = Mathf.Abs(normalizedTime - lastSentNormalizedTime) > 0.0001f && lastSentNormalizedTime >= 0f;
        
        if (userScrubbed)
        {
            // User moved the slider - push to ECS
            timeControl.normalizedTime = normalizedTime;
            OnTimeChanged?.Invoke(normalizedTime);
        }
        else if (!isPlaying)
        {
            // Paused but not scrubbing - keep UI in sync with ECS
            normalizedTime = timeControl.normalizedTime;
        }
        else
        {
            // Playing - read back from ECS so slider moves
            normalizedTime = timeControl.normalizedTime;
        }
        
        // Always update these
        timeControl.isPaused = !isPlaying;
        timeControl.playbackSpeed = playbackSpeed;
        timeControl.forceLoop = loop;
        
        entityManager.SetComponentData(timeControlEntity, timeControl);
        lastSentNormalizedTime = timeControl.normalizedTime;
    }
    
    private bool IsWorldValid()
    {
        if (cachedWorld == null || !cachedWorld.IsCreated)
        {
            return false;
        }
        
        try
        {
            // Try to check if entity exists - this will throw if EntityManager is disposed
            return entityManager.Exists(timeControlEntity);
        }
        catch (System.ObjectDisposedException)
        {
            return false;
        }
        catch (System.Exception)
        {
            return false;
        }
    }
    
    private void TryInitializeWorld()
    {
        if (worldInitialized) return;
        
        cachedWorld = World.DefaultGameObjectInjectionWorld;
        if (cachedWorld == null || !cachedWorld.IsCreated) return;
        
        entityManager = cachedWorld.EntityManager;
        
        try
        {
            using var query = entityManager.CreateEntityQuery(typeof(EditorAnimationTimeControl));
            if (query.CalculateEntityCount() > 0)
            {
                timeControlEntity = query.GetSingletonEntity();
                worldInitialized = true;
                
                // Push initial state
                var timeControl = entityManager.GetComponentData<EditorAnimationTimeControl>(timeControlEntity);
                timeControl.isPaused = !isPlaying;
                timeControl.normalizedTime = normalizedTime;
                timeControl.playbackSpeed = playbackSpeed;
                timeControl.forceLoop = loop;
                entityManager.SetComponentData(timeControlEntity, timeControl);
                lastSentNormalizedTime = normalizedTime;
                
                Debug.Log("[AnimationPreviewController] Connected to ECS time control");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AnimationPreviewController] Failed to initialize: {e.Message}");
            worldInitialized = false;
        }
    }
    
    public void Play()
    {
        isPlaying = true;
    }
    
    public void Pause()
    {
        isPlaying = false;
    }
    
    public void Stop()
    {
        isPlaying = false;
        normalizedTime = 0f;
        lastSentNormalizedTime = -1f; // Force push on next update
    }
    
    public void SetTime(float time)
    {
        normalizedTime = Mathf.Clamp01(time);
        lastSentNormalizedTime = -1f; // Force push on next update
        OnTimeChanged?.Invoke(normalizedTime);
        
        // Immediately push to ECS if possible
        if (IsWorldValid())
        {
            try
            {
                var timeControl = entityManager.GetComponentData<EditorAnimationTimeControl>(timeControlEntity);
                timeControl.normalizedTime = normalizedTime;
                entityManager.SetComponentData(timeControlEntity, timeControl);
                lastSentNormalizedTime = normalizedTime;
            }
            catch (System.Exception)
            {
                // Ignore - will be handled next frame
            }
        }
    }
    
    public void NextFrame()
    {
        if (currentClip == null || currentClip.partTracks == null || currentClip.partTracks.Count == 0) return;
        
        float nextTime = 1f;
        foreach (var track in currentClip.partTracks)
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
        if (currentClip == null || currentClip.partTracks == null || currentClip.partTracks.Count == 0) return;
        
        float prevTime = 0f;
        foreach (var track in currentClip.partTracks)
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
    
    public void SetClip(AnimationClipSO clip)
    {
        currentClip = clip;
        currentAnimation = clip != null ? clip.animationType : AnimationType.None;
        normalizedTime = 0f;
        lastSentNormalizedTime = -1f;
        OnClipChanged?.Invoke(clip);
        
        // Update character animation in ECS
        if (IsWorldValid())
        {
            try
            {
                using var query = entityManager.CreateEntityQuery(typeof(CharacterAnimation));
                var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
                
                foreach (var entity in entities)
                {
                    var anim = entityManager.GetComponentData<CharacterAnimation>(entity);
                    anim.currentAnimation = currentAnimation;
                    anim.time = 0f;
                    anim.requestedAnimation = AnimationType.None;
                    entityManager.SetComponentData(entity, anim);
                }
                
                entities.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AnimationPreviewController] Failed to set clip: {e.Message}");
            }
        }
    }
    
    private void OnDestroy()
    {
        isBeingDestroyed = true;
        
        // Only try to reset if world is still valid
        if (IsWorldValid())
        {
            try
            {
                entityManager.SetComponentData(timeControlEntity, EditorAnimationTimeControl.Default);
            }
            catch (System.Exception)
            {
                // Ignore - world is being destroyed
            }
        }
    }
    
    private void OnApplicationQuit()
    {
        isBeingDestroyed = true;
    }
    
    #if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showKeyframeMarkers || currentClip == null) return;
        
        Vector3 timelineStart = transform.position + Vector3.down * 3f + Vector3.left * 5f;
        Vector3 timelineEnd = timelineStart + Vector3.right * 10f;
        
        Gizmos.color = Color.gray;
        Gizmos.DrawLine(timelineStart, timelineEnd);
        
        Gizmos.color = isPlaying ? Color.green : Color.red;
        Vector3 currentPos = Vector3.Lerp(timelineStart, timelineEnd, normalizedTime);
        Gizmos.DrawLine(currentPos + Vector3.down * 0.3f, currentPos + Vector3.up * 0.3f);
        
        string status = isPlaying ? "▶ PLAYING" : "⏸ PAUSED";
        UnityEditor.Handles.Label(
            currentPos + Vector3.up * 0.5f, 
            $"{status} ({normalizedTime:F2})",
            new GUIStyle 
            { 
                fontSize = 12, 
                normal = { textColor = isPlaying ? Color.green : Color.yellow } 
            }
        );
        
        if (currentClip.partTracks == null) return;
        
        var keyframeTimes = new System.Collections.Generic.HashSet<float>();
        foreach (var track in currentClip.partTracks)
        {
            if (track.keyframes == null) continue;
            foreach (var kf in track.keyframes)
            {
                keyframeTimes.Add(kf.normalizedTime);
            }
        }
        
        Gizmos.color = Color.yellow;
        foreach (float t in keyframeTimes)
        {
            Vector3 pos = Vector3.Lerp(timelineStart, timelineEnd, t);
            Gizmos.DrawSphere(pos, 0.08f);
        }
    }
    #endif
}