// using Unity.Burst;
// using Unity.Collections;
// using Unity.Entities;
//
// // Both structs must be passed by 'in' (read-only reference) for Burst compatibility
// public delegate void ActionActivationDelegate(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled);
//
// [BurstCompile]
// public static class SelectionFunctions
// {
//     [BurstCompile]
//     public static void NullEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
//     {
//     }
//
//     [BurstCompile]
//     public static void DeathEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb,
//         bool enabled)
//     {
//         ecb.SetComponentEnabled<DeathAction>(index, entity, enabled);
//         ecb.SetComponentEnabled<SocialAvailable>(index, entity, !enabled);
//     }
//
//     [BurstCompile]
//     public static void IdleEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
//     {
//         ecb.SetComponentEnabled<IdleAction>(index, entity, enabled);
//         if (enabled)
//             ecb.SetComponentEnabled<SocialAvailable>(index, entity, true);
//     }
//
//     [BurstCompile]
//     public static void WanderEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb,
//         bool enabled)
//     {
//         ecb.SetComponentEnabled<WanderAction>(index, entity, enabled);
//         if (enabled)
//             ecb.SetComponentEnabled<SocialAvailable>(index, entity, true);
//     }
//
//     [BurstCompile]
//     public static void InteractEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb,
//         bool enabled)
//         => ecb.SetComponentEnabled<InteractAction>(index, entity, enabled);
//
//     [BurstCompile]
//     public static void MeleeEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb,
//         bool enabled)
//     {
//         ecb.SetComponentEnabled<MeleeContinuousAction>(index, entity, enabled);
//         ecb.SetComponentEnabled<SocialAvailable>(index, entity, !enabled);
//     }
//
//     [BurstCompile]
//     public static void MeleeSingleEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb,
//         bool enabled)
//     {
//         ecb.SetComponentEnabled<MeleeSingleAction>(index, entity, enabled);
//         ecb.SetComponentEnabled<SocialAvailable>(index, entity, !enabled);
//     }
//
//     [BurstCompile]
//     public static void FleeEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
//     {
//         ecb.SetComponentEnabled<FleeAction>(index, entity, enabled);
//         ecb.SetComponentEnabled<SocialAvailable>(index, entity, !enabled);
//     }
//
//     [BurstCompile]
//     public static void SitEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
//         => ecb.SetComponentEnabled<SitAction>(index, entity, enabled);
//
//     [BurstCompile]
//     public static void TalkEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
//     {
//         ecb.SetComponentEnabled<TalkAction>(index, entity, enabled);
//         if (!enabled)
//         {
//             ecb.SetComponent(index, entity, new ConversationContext
//             {
//                 conversationPartner = Entity.Null,
//                 isResponder = false,
//             });
//             ecb.SetComponentEnabled<SocialAvailable>(index, entity, true);
//         }
//     }
//
//     [BurstCompile]
//     public static void PickupItemEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb,
//         bool enabled)
//         => ecb.SetComponentEnabled<PickupItemAction>(index, entity, enabled);
//
//     public static void PopulateFunctionTable(ref NativeArray<FunctionPointer<ActionActivationDelegate>> table)
//     {
//         table[(int)ActionType.Idle]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(IdleEnable);
//         table[(int)ActionType.Wander]          = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(WanderEnable);
//         table[(int)ActionType.Interact]        = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(InteractEnable);
//         table[(int)ActionType.Flee]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(FleeEnable);
//         table[(int)ActionType.MeleeContinuous] = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(MeleeEnable);
//         table[(int)ActionType.MeleeSingle]     = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(MeleeSingleEnable);
//         table[(int)ActionType.Sit]             = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SitEnable);
//         table[(int)ActionType.Bathroom]        = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(InteractEnable);
//         table[(int)ActionType.Talk]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(TalkEnable);
//         table[(int)ActionType.Death]           = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(DeathEnable);
//         table[(int)ActionType.EquipWeapon]     = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(PickupItemEnable);
//         table[(int)ActionType.UseHealingItem]  = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(PickupItemEnable);
//         table[(int)ActionType.Eat]             = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(PickupItemEnable);
//         table[(int)ActionType.Drink]           = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(PickupItemEnable);
//     }
// }