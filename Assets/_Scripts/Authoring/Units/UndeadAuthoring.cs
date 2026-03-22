using Unity.Entities;
using UnityEngine;

public class UndeadAuthoring : MonoBehaviour
{
    public bool startUndead;
    public GameObject selectedVisual;
    public float showScale;
    
    public class Baker : Baker<UndeadAuthoring>
    {
        public override void Bake(UndeadAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Undead>(entity);
            SetComponentEnabled<Undead>(entity, authoring.startUndead);
            AddComponent<Revive>(entity);
            SetComponentEnabled<Revive>(entity, false);
            AddComponent<Minion>(entity);
            SetComponentEnabled<Minion>(entity, false);
            
            AddComponent(entity, new Selected {
                visualEntity = GetEntity(authoring.selectedVisual, TransformUsageFlags.Dynamic),
                showScale = authoring.showScale,
            });
            SetComponentEnabled<Selected>(entity, false);
        }
    }
}
