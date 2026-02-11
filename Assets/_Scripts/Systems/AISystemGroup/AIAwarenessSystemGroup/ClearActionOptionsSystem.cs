// using Unity.Burst;
// using Unity.Entities;
//
// [BurstCompile]
// [UpdateInGroup(typeof(AIAwarenessSystemGroup))]
// public partial struct ClearActionOptionsSystem : ISystem
// {
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         new ClearActionOptionsJob().ScheduleParallel(state.Dependency);
//     }
// }
//
// [BurstCompile]
// public partial struct ClearActionOptionsJob : IJobEntity
// {
//     public void Execute(
//         ref DynamicBuffer<ActionOption> options,
//         in ActionLock actionLock)
//     {
//         // Only clear when making a new decision
//         bool needsDecision = actionLock.lockedAction == ActionType.None ||
//                              actionLock.isComplete ||
//                              actionLock.decisionTimer <= 0.05f;
//
//         if (needsDecision)
//         {
//             options.Clear();
//         }
//     }
// }