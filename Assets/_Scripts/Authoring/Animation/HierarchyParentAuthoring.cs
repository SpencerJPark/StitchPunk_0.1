using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class HierarchyParentAuthoring : MonoBehaviour
{
    public bool includeAllDescendants = true;
    
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<HierarchyParentAuthoring>
    {
        public override void Bake(HierarchyParentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            if (authoring.includeAllDescendants)
            {
                BakeAllDescendants(authoring.transform);
            }
            else
            {
                foreach (Transform child in authoring.transform)
                {
                    GetEntity(child, TransformUsageFlags.Dynamic);
                }
            }
        }
        
        private void BakeAllDescendants(Transform parent)
        {
            foreach (Transform child in parent)
            {
                GetEntity(child, TransformUsageFlags.Dynamic);
                BakeAllDescendants(child);
            }
        }
    }
}