using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(AnimationExecutionSystemGroup))]
[UpdateAfter(typeof(ApplyAnimatedPoseSystem))]
partial struct BillboardSystem : ISystem {


    private ComponentLookup<LocalTransform> localTransformComponentLookup;
    private ComponentLookup<Dead>           deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        localTransformComponentLookup = state.GetComponentLookup<LocalTransform>();
        deadLookup                    = state.GetComponentLookup<Dead>(true);
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state) {
        Vector3 cameraForward = Vector3.zero;
        if (Camera.main != null) {
            cameraForward = Camera.main.transform.forward;
        }

        localTransformComponentLookup.Update(ref state);
        deadLookup.Update(ref state);
        BillboardJob billboardJob = new BillboardJob {
            cameraForward                 = cameraForward,
            localTransformComponentLookup = localTransformComponentLookup,
            deadLookup                    = deadLookup,
        };
        billboardJob.ScheduleParallel();
    }


}


[BurstCompile]
public partial struct BillboardJob : IJobEntity {

    [ReadOnly] public ComponentLookup<Dead> deadLookup;

    [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformComponentLookup;

    public float3 cameraForward;

    public void Execute(in Billboard billboard, Entity entity) {
        Entity parent = billboard.parentEntity;

        bool isDead = deadLookup.HasComponent(parent) && deadLookup.IsComponentEnabled(parent);

        LocalTransform        parentTransform = localTransformComponentLookup[parent];
        RefRW<LocalTransform> localTransform  = localTransformComponentLookup.GetRefRW(entity);

        // Full billboard target (local space)
        quaternion targetLocal = parentTransform.InverseTransformRotation(
            quaternion.LookRotation(cameraForward, math.up()));

        if (!isDead)
        {
            // Alive: full billboard — always track camera on all axes
            localTransform.ValueRW.Rotation = targetLocal;
            return;
        }

        // Dead: freeze Y (no horizontal spin) but keep camera pitch (X tilt).
        // Decompose target into yaw (Y) and pitch (X), then substitute the entity's current yaw.

        // Target yaw = horizontal component of the billboard target
        float3 targetFwd     = math.rotate(targetLocal, math.forward());
        float3 targetFlatFwd = math.normalizesafe(new float3(targetFwd.x, 0f, targetFwd.z));
        if (math.lengthsq(targetFlatFwd) < 0.001f) return; // camera pointing straight up/down — skip
        quaternion targetYaw   = quaternion.LookRotationSafe(targetFlatFwd, math.up());
        quaternion targetPitch = math.mul(math.inverse(targetYaw), targetLocal); // pitch relative to yaw

        // Current yaw = entity's current horizontal facing direction
        float3 currentFwd     = math.rotate(localTransform.ValueRO.Rotation, math.forward());
        float3 currentFlatFwd = math.normalizesafe(new float3(currentFwd.x, 0f, currentFwd.z));
        quaternion currentYaw = math.lengthsq(currentFlatFwd) > 0.001f
            ? quaternion.LookRotationSafe(currentFlatFwd, math.up())
            : targetYaw; // no valid current forward — fall back to camera yaw

        // Keep current Y, apply camera pitch
        localTransform.ValueRW.Rotation = math.mul(currentYaw, targetPitch);
    }
}