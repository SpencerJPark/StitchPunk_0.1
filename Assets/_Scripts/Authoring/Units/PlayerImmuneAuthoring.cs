using Unity.Entities;
using UnityEngine;

public class PlayerImmuneAuthoring : MonoBehaviour 
{
    public class Baker : Baker<PlayerImmuneAuthoring> {
        
        public override void Bake(PlayerImmuneAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerImmune());
            SetComponentEnabled<PlayerImmune>(entity, true);
        }
    }
}