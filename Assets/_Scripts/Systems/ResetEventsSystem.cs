using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

[UpdateInGroup(typeof(LateSimulationSystemGroup), OrderLast = true)]
partial struct ResetEventsSystem : ISystem
{
    // Update count when adding more jobs
    private const int NUMBER_OF_RESET_EVENT_JOBS = 4;
    private NativeArray<JobHandle> jobHandleNativeArray;
    
    // Lists to pass Entities to GameObjects World
    private NativeList<Entity> onBarracksUnitQueueChangedEntityList;
    private NativeList<Entity> onHealthDeadEntityList;


    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        jobHandleNativeArray = new NativeArray<JobHandle>(NUMBER_OF_RESET_EVENT_JOBS, Allocator.Persistent);
        onBarracksUnitQueueChangedEntityList = new NativeList<Entity>(Allocator.Persistent);
        onHealthDeadEntityList = new NativeList<Entity>(Allocator.Persistent);
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state) {
        jobHandleNativeArray.Dispose();
        onBarracksUnitQueueChangedEntityList.Dispose();
        onHealthDeadEntityList.Dispose();
    }

    public void OnUpdate(ref SystemState state) {

        EndGameCheck(ref state);
        
        ScheduleResetEventJobs(ref state);
        
        PassEventsToGameObjects(ref state);

        // Run Jobs Array
        state.Dependency = JobHandle.CombineDependencies(jobHandleNativeArray);
    }
    
    private void EndGameCheck(ref SystemState state)
    {
        if (SystemAPI.HasSingleton<BuildingHQ>()) {
            Health hqHealth = SystemAPI.GetComponent<Health>(SystemAPI.GetSingletonEntity<BuildingHQ>());
            if (hqHealth.onDead) {
                DOTSEventsManager.Instance?.TriggerOnHQDead();
            }
        }
    }

    private void ScheduleResetEventJobs(ref SystemState state)
    {
        jobHandleNativeArray[0] = new ResetSelectedEventsJob().ScheduleParallel(state.Dependency);
        jobHandleNativeArray[1] = new ResetShootAttackEventsJob().ScheduleParallel(state.Dependency);
        jobHandleNativeArray[2] = new ResetMeleeAttackEventsJob().ScheduleParallel(state.Dependency);
        jobHandleNativeArray[3] = new ResetImageIndexUpdateJob().ScheduleParallel(state.Dependency);
    }
    
    private void PassEventsToGameObjects(ref SystemState state)
    {
        onBarracksUnitQueueChangedEntityList.Clear();
        new ResetBuildingBarracksEventsJob() {
            onUnitQueueChangedEntityList = onBarracksUnitQueueChangedEntityList.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency).Complete();

        DOTSEventsManager.Instance?.TriggerOnBarracksUnitQueueChanged(onBarracksUnitQueueChangedEntityList);
        
        onHealthDeadEntityList.Clear();
        new ResetHealthEventsJob() {
            onHealthDeadEntityList = onHealthDeadEntityList.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency).Complete();

        DOTSEventsManager.Instance?.TriggerOnHealthDead(onHealthDeadEntityList);
    }

}


[BurstCompile]
public partial struct ResetImageIndexUpdateJob : IJobEntity {

    public void Execute(ref ImageIndex imageIndex) {
        imageIndex.onUpdate = false;
    }
}

[BurstCompile]
public partial struct ResetShootAttackEventsJob : IJobEntity {

    public void Execute(ref ShootAttack shootAttack) {
        shootAttack.onShoot.isTriggered = false;
    }
}


[BurstCompile]
public partial struct ResetHealthEventsJob : IJobEntity {

    public NativeList<Entity>.ParallelWriter onHealthDeadEntityList;
    
    public void Execute(ref Health health, Entity entity) {
        
        if (health.onDead) {
            onHealthDeadEntityList.AddNoResize(entity);
        }
        
        health.onHealthChanged = false;
        health.onDead = false;
    }

}


[BurstCompile]
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
public partial struct ResetSelectedEventsJob : IJobEntity {


    public void Execute(ref Selected selected) {
        selected.onSelected = false;
        selected.onDeselected = false;
    }

}


[BurstCompile]
public partial struct ResetMeleeAttackEventsJob : IJobEntity {


    public void Execute(ref MeleeAttack meleeAttack) {
        meleeAttack.onAttacked = false;
    }

}


[BurstCompile]
public partial struct ResetBuildingBarracksEventsJob : IJobEntity {


    public NativeList<Entity>.ParallelWriter onUnitQueueChangedEntityList;


    public void Execute(ref BuildingBarracks buildingBarracks, Entity entity) {
        if (buildingBarracks.onUnitQueueChanged) {
            onUnitQueueChangedEntityList.AddNoResize(entity);
        }

        buildingBarracks.onUnitQueueChanged = false;

    }

}