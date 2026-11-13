using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// Put on a render child of a HarvestableNodeAuthoring tree: the whole tree mesh (off) or the stump mesh (on).
public class HarvestedNodeVisualPartAuthoring : MonoBehaviour
{
    [Tooltip("On = this part appears only after the tree is cut (the stump); off = it disappears when cut")]
    public bool showWhenStump;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<HarvestedNodeVisualPartAuthoring>
    {
        public override void Bake(HarvestedNodeVisualPartAuthoring authoring)
        {
            Entity partEntity = GetEntity(TransformUsageFlags.Dynamic);
            HarvestableNodeAuthoring nodeAuthoring = GetComponentInParent<HarvestableNodeAuthoring>();
            if (nodeAuthoring == null)
            {
                return;
            }

            Entity nodeEntity = GetEntity(nodeAuthoring, TransformUsageFlags.Dynamic);
            AddComponent(partEntity, new HarvestedNodeVisualPart
            {
                node = nodeEntity,
                showWhenStump = (byte)(authoring.showWhenStump ? 1 : 0)
            });
            AddComponent(partEntity, new PostTransformMatrix
            {
                Value = authoring.showWhenStump ? float4x4.Scale(0.001f) : float4x4.identity
            });
        }
    }
}
