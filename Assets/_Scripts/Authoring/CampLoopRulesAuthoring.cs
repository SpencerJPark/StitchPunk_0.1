using Unity.Entities;
using UnityEngine;

// One per playtest subscene; bakes the disabled lose-condition singleton.
public class CampLoopRulesAuthoring : MonoBehaviour
{
    public class Baker : Baker<CampLoopRulesAuthoring>
    {
        public override void Bake(CampLoopRulesAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new CampLoopGameOver());
            SetComponentEnabled<CampLoopGameOver>(entity, false);
        }
    }
}
