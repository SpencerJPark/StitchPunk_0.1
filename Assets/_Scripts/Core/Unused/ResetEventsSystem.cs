// using Unity.Burst;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Jobs;
//
// [UpdateInGroup(typeof(LateSimulationSystemGroup), OrderLast = true)]
// partial struct ResetEventsSystem : ISystem
// {
//     private NativeList<Entity> onBarracksUnitQueueChangedEntityList;
//     private NativeList<Entity> onHealthDeadEntityList;
//
//     public void OnCreate(ref SystemState state)
//     {
//         onBarracksUnitQueueChangedEntityList = new NativeList<Entity>(64, Allocator.Persistent);
//         onHealthDeadEntityList = new NativeList<Entity>(64, Allocator.Persistent);
//     }
//
//     public void OnDestroy(ref SystemState state)
//     {
//         onBarracksUnitQueueChangedEntityList.Dispose();
//         onHealthDeadEntityList.Dispose();
//     }
//
//     public void OnUpdate(ref SystemState state)
//     {
//         EndGameCheck(ref state);
//
//         // Schedule all parallel jobs that don't need sync
//         JobHandle resetSelectedHandle = new ResetSelectedEventsJob().ScheduleParallel(state.Dependency);
//         JobHandle resetImageHandle = new ResetImageIndexUpdateJob().ScheduleParallel(state.Dependency);
//
//         // Combine independent jobs
//         JobHandle combinedResetJobs = JobHandle.CombineDependencies(
//             JobHandle.CombineDependencies(resetSelectedHandle, resetShootHandle),
//             JobHandle.CombineDependencies(resetMeleeHandle, resetImageHandle)
//         );
//
//         // Handle events that need to pass to GameObjects
//         PassEventsToGameObjects(ref state);
//
//         state.Dependency = combinedResetJobs;
//     }
//
//     private void EndGameCheck(ref SystemState state)
//     {
//         if (!SystemAPI.HasSingleton<BuildingHQ>())
//         {
//             return;
//         }
//         
//         Entity hqEntity = SystemAPI.GetSingletonEntity<BuildingHQ>();
//         Health hqHealth = SystemAPI.GetComponent<Health>(hqEntity);
//         
//         if (hqHealth.onDead)
//         {
//             DOTSEventsManager.Instance?.TriggerOnHQDead();
//         }
//     }
//
//     private void PassEventsToGameObjects(ref SystemState state)
//     {
//         // Barracks events - use single-threaded job to avoid parallel writer overhead
//         onBarracksUnitQueueChangedEntityList.Clear();
//         new CollectBarracksEventsJob
//         {
//             OnUnitQueueChangedEntityList = onBarracksUnitQueueChangedEntityList
//         }.Schedule(state.Dependency).Complete();
//
//         DOTSEventsManager.Instance?.TriggerOnBarracksUnitQueueChanged(onBarracksUnitQueueChangedEntityList);
//
//         // Health events - use single-threaded job to avoid parallel writer overhead
//         onHealthDeadEntityList.Clear();
//         new CollectHealthEventsJob
//         {
//             OnHealthDeadEntityList = onHealthDeadEntityList
//         }.Schedule(state.Dependency).Complete();
//
//         DOTSEventsManager.Instance?.TriggerOnHealthDead(onHealthDeadEntityList);
//     }
// }
//
// [BurstCompile]
// partial struct ResetImageIndexUpdateJob : IJobEntity
// {
//     public void Execute(ref ImageIndex imageIndex)
//     {
//         imageIndex.onUpdate = false;
//     }
// }
//
//
// [BurstCompile]
// partial struct CollectHealthEventsJob : IJobEntity
// {
//     public NativeList<Entity> OnHealthDeadEntityList;
//
//     public void Execute(Entity entity, ref Health health)
//     {
//         if (health.onDead)
//         {
//             OnHealthDeadEntityList.Add(entity);
//         }
//
//         health.onHealthChanged = false;
//         health.onDead = false;
//     }
// }
//
// [BurstCompile]
// [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
// partial struct ResetSelectedEventsJob : IJobEntity
// {
//     public void Execute(ref Selected selected)
//     {
//         selected.onSelected = false;
//         selected.onDeselected = false;
//     }
// }
//
//
// // [BurstCompile]
// // partial struct CollectBarracksEventsJob : IJobEntity
// // {
// //     public NativeList<Entity> OnUnitQueueChangedEntityList;
// //
// //     public void Execute(Entity entity, ref BuildingBarracks buildingBarracks)
// //     {
// //         if (buildingBarracks.onUnitQueueChanged)
// //         {
// //             OnUnitQueueChangedEntityList.Add(entity);
// //         }
// //
// //         buildingBarracks.onUnitQueueChanged = false;
// //     }
// // }