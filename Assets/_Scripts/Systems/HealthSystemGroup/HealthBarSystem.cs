using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup), OrderLast = true)]
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

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        localTransformComponentLookup.Update(ref state);
        healthComponentLookup.Update(ref state);
        postTransformMatrixComponentLookup.Update(ref state);
        HealthBarJob healthBarJob = new HealthBarJob {
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


    public void Execute(in HealthBar healthBar, Entity entity) {
        RefRW<LocalTransform> localTransform = localTransformComponentLookup.GetRefRW(entity);

        Health health = healthComponentLookup[healthBar.healthEntity];

        // if (!health.onHealthChanged) {
        //     return;
        // }

        float healthNormalized = (float)health.healthAmount / health.healthAmountMax;

        if (healthNormalized >= 1f || health.healthAmount <= 0) {
            localTransform.ValueRW.Scale = 0f;
            return;
        }

        localTransform.ValueRW.Scale = 1f;

        RefRW<PostTransformMatrix> barVisualPostTransformMatrix =
            postTransformMatrixComponentLookup.GetRefRW(healthBar.barVisualEntity);

        barVisualPostTransformMatrix.ValueRW.Value = float4x4.Scale(math.max(healthNormalized, 0f), 1, 1);
    }
}