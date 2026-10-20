using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

// Managed because it builds a RenderMeshArray from UnityEngine Mesh/Material objects.
// Icon material index for EmoteIcon icon = (int)icon - 1 + EmotePoolConfig.firstIconMaterialIndex.
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(EmotePresentationSystem))]
public partial class EmotePoolSetupSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<EmotePoolConfig>();
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.QueryBuilder().WithAll<EmotePoolSlot>().Build().IsEmptyIgnoreFilter)
        {
            Enabled = false;
            return;
        }

        Entity configEntity = SystemAPI.GetSingletonEntity<EmotePoolConfig>();
        EmotePoolConfig config = EntityManager.GetComponentData<EmotePoolConfig>(configEntity);
        EmotePoolAssets assets = EntityManager.GetComponentData<EmotePoolAssets>(configEntity);
        DynamicBuffer<EmoteIconMaterial> materialBuffer = EntityManager.GetBuffer<EmoteIconMaterial>(configEntity);

        Material[] materials = new Material[materialBuffer.Length];
        for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
        {
            materials[materialIndex] = materialBuffer[materialIndex].material.Value;
        }

        Mesh[] meshes = new Mesh[] { assets.quadMesh.Value };
        RenderMeshArray renderMeshArray = new RenderMeshArray(materials, meshes);
        RenderMeshDescription description = new RenderMeshDescription(ShadowCastingMode.Off, false, MotionVectorGenerationMode.Camera, 0);

        for (int slotIndex = 0; slotIndex < config.poolSize; slotIndex++)
        {
            Entity slotEntity = EntityManager.CreateEntity();
            RenderMeshUtility.AddComponents(slotEntity, EntityManager, description, renderMeshArray, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            EntityManager.AddComponentData(slotEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 0f });
            EntityManager.AddComponentData(slotEntity, new LocalToWorld { Value = float4x4.identity });
            EntityManager.AddComponentData(slotEntity, new URPMaterialPropertyBaseColor { Value = new float4(1f, 1f, 1f, 0f) });
            EntityManager.AddComponentData(slotEntity, new EmotePoolSlot { slotIndex = slotIndex, shownFor = 0f, owner = Entity.Null });
#if UNITY_EDITOR
            EntityManager.SetName(slotEntity, $"EmoteSlot_{slotIndex}");
#endif
        }
    }
}
