using Unity.Entities;
using UnityEngine;

// Put beside ResourceNodeAuthoring on bandit loot; sealed until the camp is cleared (CampLoop Phase 5).
public class CampLootAuthoring : MonoBehaviour
{
    public GameObject camp;

    public class Baker : Baker<CampLootAuthoring>
    {
        public override void Bake(CampLootAuthoring authoring)
        {
            if (authoring.camp == null) return;
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new CampLootLocked
            {
                camp = GetEntity(authoring.camp, TransformUsageFlags.Dynamic)
            });
        }
    }
}
