using Unity.Entities;
using UnityEngine;

public class UndeadAuthoring : MonoBehaviour
{
    public bool startMinion;
    public bool startUndead;
    public GameObject selectedVisual;
    public float showScale;
    
    public class Baker : Baker<UndeadAuthoring>
    {
        public override void Bake(UndeadAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent<Minion>(entity);
            SetComponentEnabled<Minion>(entity, authoring.startMinion);

            if (authoring.startMinion)
            {
                authoring.startUndead = true;
            }
            
            AddComponent<Undead>(entity);
            SetComponentEnabled<Undead>(entity, authoring.startUndead);
            
            AddComponent<Revive>(entity);
            SetComponentEnabled<Revive>(entity, false);
            
            AddComponent(entity, new Selected {
                visualEntity = GetEntity(authoring.selectedVisual, TransformUsageFlags.Dynamic),
                showScale = authoring.showScale,
            });
            SetComponentEnabled<Selected>(entity, false);
        }
    }
}
