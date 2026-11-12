using Unity.Entities;
using UnityEngine;

// The nav window follows the first entity carrying TerrainNavFocus: put it on the player or the driven caravan.
// Only one should be active at a time.
[AddComponentMenu("Stitch Punk/Terrain/Nav Focus")]
public class TerrainNavFocusAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<TerrainNavFocusAuthoring>
    {
        public override void Bake(TerrainNavFocusAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<TerrainNavFocus>(entity);
        }
    }
}
