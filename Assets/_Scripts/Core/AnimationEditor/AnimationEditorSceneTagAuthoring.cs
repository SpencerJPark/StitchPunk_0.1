// =====================================
// CONDITIONAL SYSTEM EXECUTION
// =====================================

using Unity.Entities;
using UnityEngine;

// Add this component to signal systems should run
public struct AnimationEditorActive : IComponentData { }

// Authoring for the editor scene tag
public class AnimationEditorSceneTagAuthoring : MonoBehaviour
{
    public class Baker : Baker<AnimationEditorSceneTagAuthoring>
    {
        public override void Bake(AnimationEditorSceneTagAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent<AnimationEditorActive>(entity);
        }
    }
}