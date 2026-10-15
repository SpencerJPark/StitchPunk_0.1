using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Runs after Movement has rotated the root and before TransformSystemGroup writes LocalToWorld.
[UpdateInGroup(typeof(AnimationSystemGroup))]
[BurstCompile]
public partial struct VisualBillboardSystem : ISystem
{
    private ComponentLookup<LocalTransform> localTransformLookup;
    [ReadOnly] private ComponentLookup<Parent> parentLookup;
    [ReadOnly] private ComponentLookup<LocalToWorld> localToWorldLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimationToolkitCameraData>();
        state.RequireForUpdate<VisualBillboard>();
        localTransformLookup = state.GetComponentLookup<LocalTransform>();
        parentLookup = state.GetComponentLookup<Parent>(true);
        localToWorldLookup = state.GetComponentLookup<LocalToWorld>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float3 cameraForward = SystemAPI.GetSingleton<AnimationToolkitCameraData>().forward;
        float3 flatCameraForward = new float3(cameraForward.x, 0f, cameraForward.z);
        if (math.lengthsq(flatCameraForward) < 1e-6f)
        {
            return;
        }

        localTransformLookup.Update(ref state);
        parentLookup.Update(ref state);
        localToWorldLookup.Update(ref state);

        // Upright, turned to the camera's yaw only: CameraWorldLean tilts sprites and 3D meshes by the same
        // view-matrix lean, so a screen-aligned tilt here would lean sprites twice.
        FaceCameraJob faceCameraJob = new FaceCameraJob
        {
            uprightFacingWorldRotation = quaternion.LookRotation(math.normalize(flatCameraForward), math.up()),
            localTransformLookup = localTransformLookup,
            parentLookup = parentLookup,
            localToWorldLookup = localToWorldLookup
        };
        state.Dependency = faceCameraJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(VisualBillboard))]
public partial struct FaceCameraJob : IJobEntity
{
    public quaternion uprightFacingWorldRotation;

    // Each visual only writes its own LocalTransform and reads its parent's, so parallel is safe.
    [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;
    [ReadOnly] public ComponentLookup<Parent> parentLookup;
    [ReadOnly] public ComponentLookup<LocalToWorld> localToWorldLookup;

    private void Execute(Entity entity)
    {
        if (!parentLookup.HasComponent(entity))
        {
            return;
        }
        Entity parent = parentLookup[entity].Value;

        quaternion parentWorldRotation;
        if (localTransformLookup.HasComponent(parent) && !parentLookup.HasComponent(parent))
        {
            // Root parent: its LocalTransform is already this frame's, LocalToWorld would lag a frame.
            parentWorldRotation = localTransformLookup[parent].Rotation;
        }
        else if (localToWorldLookup.HasComponent(parent))
        {
            parentWorldRotation = localToWorldLookup[parent].Rotation;
        }
        else
        {
            return;
        }

        RefRW<LocalTransform> visualTransform = localTransformLookup.GetRefRW(entity);
        visualTransform.ValueRW.Rotation = math.mul(math.inverse(parentWorldRotation), uprightFacingWorldRotation);
    }
}
