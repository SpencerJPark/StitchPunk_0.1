// =====================================
// EDITOR TIME CONTROL AUTHORING (Updated)
// =====================================

using Unity.Entities;
using UnityEngine;

/// <summary>
/// Add this to your animation editor subscene.
/// Creates the EditorAnimationTimeControl singleton.
/// </summary>
public class EditorAnimationTimeControlAuthoring : MonoBehaviour
{
    [Header("Initial State")]
    public bool startPaused = true;
    public float playbackSpeed = 1f;
    public bool forceLoop = true;
    
    public class Baker : Baker<EditorAnimationTimeControlAuthoring>
    {
        public override void Bake(EditorAnimationTimeControlAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EditorAnimationTimeControl
            {
                isPaused = authoring.startPaused,
                normalizedTime = 0f,
                playbackSpeed = authoring.playbackSpeed,
                forceLoop = authoring.forceLoop
            });
        }
    }
}

public struct EditorAnimationTimeControl : IComponentData
{
    public bool isPaused;
    public float normalizedTime;    // 0-1 range, always authoritative when paused
    public float playbackSpeed;
    public bool forceLoop;
    
    public static EditorAnimationTimeControl Default => new EditorAnimationTimeControl
    {
        isPaused = true,
        normalizedTime = 0f,
        playbackSpeed = 1f,
        forceLoop = true
    };
}