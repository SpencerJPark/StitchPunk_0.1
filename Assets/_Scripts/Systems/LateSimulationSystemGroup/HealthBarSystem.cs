using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial struct HealthBarSystem : ISystem {


    private ComponentLookup<LocalTransform> localTransformComponentLookup;
    private ComponentLookup<Health> healthComponentLookup;
    private ComponentLookup<PostTransformMatrix> postTransformMatrixComponentLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        localTransformComponentLookup = state.GetComponentLookup<LocalTransform>();
        healthComponentLookup = state.GetComponentLookup<Health>(true);
        postTransformMatrixComponentLookup = state.GetComponentLookup<PostTransformMatrix>(false);
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state) {
        Vector3 cameraForward = Vector3.zero;
        if (Camera.main != null) {
            cameraForward = Camera.main.transform.forward;
        }

        localTransformComponentLookup.Update(ref state);
        healthComponentLookup.Update(ref state);
        postTransformMatrixComponentLookup.Update(ref state);
        HealthBarJob healthBarJob = new HealthBarJob {
            cameraForward = cameraForward,
            localTransformComponentLookup = localTransformComponentLookup,
            healthComponentLookup = healthComponentLookup,
            postTransformMatrixComponentLookup = postTransformMatrixComponentLookup,
        };
        healthBarJob.ScheduleParallel();
    }


}


[BurstCompile]
public partial struct HealthBarJob : IJobEntity {


    [ReadOnly] public ComponentLookup<Health> healthComponentLookup;

    [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformComponentLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<PostTransformMatrix> postTransformMatrixComponentLookup;


    public float3 cameraForward;


    public void Execute(in HealthBar healthBar, Entity entity) {
        RefRW<LocalTransform> localTransform = localTransformComponentLookup.GetRefRW(entity);

        Health health = healthComponentLookup[healthBar.healthEntity];

        if (!health.onHealthChanged) {
            return;
        }

        float healthNormalized = (float)health.healthAmount / health.healthAmountMax;

        if (healthNormalized == 1f) {
            localTransform.ValueRW.Scale = 0f;
        } else {
            localTransform.ValueRW.Scale = 1f;
        }

        RefRW<PostTransformMatrix> barVisualPostTransformMatrix =
            postTransformMatrixComponentLookup.GetRefRW(healthBar.barVisualEntity);

        barVisualPostTransformMatrix.ValueRW.Value = float4x4.Scale(healthNormalized, 1, 1);
    }
}