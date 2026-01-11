// =====================================
// EDITOR TIME CONTROL AUTHORING
// =====================================

using Unity.Entities;
using UnityEngine;

public class EditorAnimationTimeControlAuthoring : MonoBehaviour
{
    [Header("Initial State")]
    public bool startPaused = true;
    public float playbackSpeed = 1f;
    public bool forceLoop = true;
    
    [Header("Live Edit Source")]
    public AnimationLibrarySO animationLibrary;
    
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
                forceLoop = authoring.forceLoop,
                soloLayerIndex = -1 // -1 means no solo, play all layers
            });
            
            AddComponentObject(entity, new EditorAnimationLibraryManaged
            {
                library = authoring.animationLibrary
            });
        }
    }
}

public struct EditorAnimationTimeControl : IComponentData
{
    public bool isPaused;
    public float normalizedTime;
    public float playbackSpeed;
    public bool forceLoop;
    public int soloLayerIndex; // -1 = play all, 0+ = solo specific layer
    
    public static EditorAnimationTimeControl Default => new EditorAnimationTimeControl
    {
        isPaused = true,
        normalizedTime = 0f,
        playbackSpeed = 1f,
        forceLoop = true,
        soloLayerIndex = -1
    };
}

public class EditorAnimationLibraryManaged : IComponentData
{
    public AnimationLibrarySO library;
}