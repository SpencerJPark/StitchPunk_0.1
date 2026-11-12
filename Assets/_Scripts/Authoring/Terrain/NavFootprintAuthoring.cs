using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Stitch Punk/Terrain/Nav Footprint")]
public class NavFootprintAuthoring : MonoBehaviour
{
    public Vector2 halfExtentsMetres = new Vector2(2f, 2f);
    public float rotationDegrees;
    [Tooltip("255 blocks; lower values only discourage.")]
    public byte cost = 255;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.Rotate(Quaternion.Euler(0f, rotationDegrees, 0f));
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(2f * halfExtentsMetres.x, 0.1f, 2f * halfExtentsMetres.y));
    }
#endif

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<NavFootprintAuthoring>
    {
        public override void Bake(NavFootprintAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NavFootprint
            {
                halfExtents = new Unity.Mathematics.float2(authoring.halfExtentsMetres.x, authoring.halfExtentsMetres.y),
                rotationRadians = authoring.rotationDegrees * Mathf.Deg2Rad,
                cost = authoring.cost
            });
        }
    }
}
