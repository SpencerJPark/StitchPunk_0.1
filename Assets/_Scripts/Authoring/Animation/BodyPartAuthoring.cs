using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class BodyPartAuthoring : MonoBehaviour
{
    public BodyPart bodyPart;
    public GameObject characterRoot;
    public int baseImageIndex;
    
    public class Baker : Baker<BodyPartAuthoring>
    {
        public override void Bake(BodyPartAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Entity characterEntity = GetEntity(authoring.characterRoot, TransformUsageFlags.Dynamic);
            
            var transform = authoring.transform;
            
            AddComponent(entity, new BodyPartTag { part = authoring.bodyPart });
            AddComponent(entity, new ParentCharacter { character = characterEntity });
            
            AddComponent(entity, new PartRestPose
            {
                localPosition = transform.localPosition,
                rotation = transform.localEulerAngles.z,
                scale = new float2(transform.localScale.x, transform.localScale.y),
                baseImageIndex = authoring.baseImageIndex,
            });
            
            AddComponent(entity, new PartAnimatedPose());
        }
    }
}

public struct BodyPartTag : IComponentData
{
    public BodyPart part;
}

public struct ParentCharacter : IComponentData
{
    public Entity character;
}

// Rest pose - set during authoring, doesn't change at runtime
public struct PartRestPose : IComponentData
{
    public float3 localPosition;
    public float rotation;
    public float2 scale;
    public int baseImageIndex;
}

// Computed each frame by the animation system
public struct PartAnimatedPose : IComponentData
{
    public float3 localPosition;
    public float rotation;
    public float2 scale;
    public int imageIndex;
}
