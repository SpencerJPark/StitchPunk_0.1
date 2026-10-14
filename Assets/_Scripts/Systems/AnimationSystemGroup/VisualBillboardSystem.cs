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
        AnimationToolkitCameraData cameraData = SystemAPI.GetSingleton<AnimationToolkitCameraData>();
        localTransformLookup.Update(ref state);
        parentLookup.Update(ref state);
        localToWorldLookup.Update(ref state);

        FaceCameraJob faceCameraJob = new FaceCameraJob
        {
            cameraPosition = cameraData.position,
            cameraForward = cameraData.forward,
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
    public float3 cameraPosition;
    public float3 cameraForward;

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
        float3 parentWorldPosition;
        if (localTransformLookup.HasComponent(parent) && !parentLookup.HasComponent(parent))
        {
            // Root parent: its LocalTransform is already this frame's, LocalToWorld would lag a frame.
            LocalTransform parentTransform = localTransformLookup[parent];
            parentWorldRotation = parentTransform.Rotation;
            parentWorldPosition = parentTransform.Position;
        }
        else if (localToWorldLookup.HasComponent(parent))
        {
            LocalToWorld parentLocalToWorld = localToWorldLookup[parent];
            parentWorldRotation = parentLocalToWorld.Rotation;
            parentWorldPosition = parentLocalToWorld.Position;
        }
        else
        {
            return;
        }

        BillboardSettings settings = new BillboardSettings
        {
            mode = BillboardMode.ScreenAligned,
            enabled = true,
            blendWeight = 1f,
            clampHalfArcRadians = -1f
        };

        quaternion desiredWorldRotation;
        bool resolved = BillboardMath.TryResolve(
            settings, parentWorldPosition, cameraPosition, cameraForward, parentWorldRotation, out desiredWorldRotation);
        if (!resolved)
        {
            return;
        }

        RefRW<LocalTransform> visualTransform = localTransformLookup.GetRefRW(entity);
        visualTransform.ValueRW.Rotation = math.mul(math.inverse(parentWorldRotation), desiredWorldRotation);
    }
}
