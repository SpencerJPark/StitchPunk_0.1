using Unity.Entities;
using UnityEngine;

public class EmotePoolAuthoring : MonoBehaviour
{
    public Mesh quadMesh;

    [Tooltip("One per EmoteIcon, in enum order starting at Happy (index = (int)icon - 1).")]
    public Material[] iconMaterials;

    public int poolSize = 24;
    public float viewDistance = 35f;
    public float headHeight = 2.1f;

    public class Baker : Baker<EmotePoolAuthoring>
    {
        public override void Bake(EmotePoolAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new EmotePoolConfig
            {
                poolSize = authoring.poolSize,
                viewDistance = authoring.viewDistance,
                headHeight = authoring.headHeight,
                firstIconMaterialIndex = 0,
            });
            AddComponent(entity, new EmotePoolAssets { quadMesh = authoring.quadMesh });

            DependsOn(authoring.quadMesh);
            DynamicBuffer<EmoteIconMaterial> materialBuffer = AddBuffer<EmoteIconMaterial>(entity);
            bool hasMissingMaterial = false;
            if (authoring.iconMaterials != null)
            {
                for (int materialIndex = 0; materialIndex < authoring.iconMaterials.Length; materialIndex++)
                {
                    Material iconMaterial = authoring.iconMaterials[materialIndex];
                    DependsOn(iconMaterial);
                    if (iconMaterial == null)
                    {
                        hasMissingMaterial = true;
                    }
                    // Added even when null so buffer indices stay aligned with (int)icon - 1.
                    materialBuffer.Add(new EmoteIconMaterial { material = iconMaterial });
                }
            }

            if (hasMissingMaterial)
            {
                Debug.LogWarning("EmotePoolAuthoring has a null entry in iconMaterials; that icon will not render.", authoring);
            }
        }
    }
}
