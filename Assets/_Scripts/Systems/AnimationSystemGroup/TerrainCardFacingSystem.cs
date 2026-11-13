using DotsAnimationToolkit;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Not Burst-compiled: Shader.SetGlobalVector is a managed API.
[UpdateInGroup(typeof(AnimationSystemGroup))]
public partial struct TerrainCardFacingSystem : ISystem
{
    private static readonly int terrainCardFacingRightPropertyId = Shader.PropertyToID("_TerrainCardFacingRight");

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationToolkitCameraData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float3 cameraForward = SystemAPI.GetSingleton<AnimationToolkitCameraData>().forward;
        float3 flatCameraForward = new float3(cameraForward.x, 0f, cameraForward.z);
        if (math.lengthsq(flatCameraForward) < 1e-6f)
        {
            return;
        }

        float3 cardRight = math.normalize(math.cross(math.up(), flatCameraForward));
        Shader.SetGlobalVector(terrainCardFacingRightPropertyId, new Vector4(cardRight.x, 0f, cardRight.z, 1f));
    }

    public void OnDestroy(ref SystemState state)
    {
        Shader.SetGlobalVector(terrainCardFacingRightPropertyId, Vector4.zero);
    }
}
