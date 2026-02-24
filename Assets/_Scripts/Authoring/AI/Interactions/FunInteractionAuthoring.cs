using Unity.Entities;
using UnityEngine;

public class FunInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<FunInteractionAuthoring> {

        public override void Bake(FunInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FunInteraction
            {
                value = authoring.value,
            });
        }
    }
}

