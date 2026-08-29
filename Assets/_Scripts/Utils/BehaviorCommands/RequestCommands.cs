using Unity.Burst;
using Unity.Entities;

// RequestAttack / RequestSocialResponse / ModifyMotivation — all fire-and-advance. ModifyMotivation
// takes its dependencies directly (not BehaviorCommandContext): BehaviorInterruptSystem's
// interruptionCleanup pass reuses it and doesn't carry the execution job's full lookup set.
[BurstCompile]
public static class RequestCommands
{
    public static void RunRequestAttack(
        ref BehaviorCommandContext        context,
        Entity                             unit,
        ref StateMachine                   stateMachine,
        in DynamicBuffer<AvailableAttack>  availableAttacks)
    {
        // AttackRequestSystem reads targetEntity/damageSource and the swing timer, so a fresh
        // request must be written each swing — enabling alone would reuse stale hitFired/elapsed.
        if (!context.attackRequestLookup.HasComponent(unit)) return;

        DamageSource damageSource = DamageSource.None;
        for (int i = 0; i < availableAttacks.Length; i++)
        {
            if (availableAttacks[i].actionType != stateMachine.action) continue;
            damageSource = availableAttacks[i].damageSource;
            break;
        }

        if (damageSource != DamageSource.None && stateMachine.targetEntity != Entity.Null)
        {
            context.attackRequestLookup[unit] = new AttackRequest
            {
                targetEntity = stateMachine.targetEntity,
                damageSource = damageSource,
                hitFired     = false,
                elapsed      = 0f,
            };
            context.attackRequestLookup.SetComponentEnabled(unit, true);
        }
        else if (context.loggingEnabled)
        {
            LogUtil.Log(ref context.ecb, context.entityIndex,
                $"[BehaviorExecution] RequestAttack skipped — no attack mapped to {stateMachine.action.Name()} or no target",
                LogLevel.Warning, context.timestamp, category: LogCategory.StateMachine);
        }
    }

    public static void RunRequestSocialResponse(
        ref BehaviorCommandContext context,
        Entity                     unit,
        in StateMachine            stateMachine)
    {
        // Written via ECB, not a lookup: multiple initiators may target the same invitee
        // in one frame, so the "one owner" rule that makes the other lookups safe doesn't
        // hold. SocialResponseSystem consumes the invite next frame.
        if (stateMachine.targetEntity != Entity.Null
            && context.socialInviteLookup.HasComponent(stateMachine.targetEntity))
        {
            context.ecb.SetComponent(context.entityIndex, stateMachine.targetEntity,
                new SocialInvite { initiator = unit });
            context.ecb.SetComponentEnabled<SocialInvite>(context.entityIndex, stateMachine.targetEntity, true);
        }
    }

    public static void RunModifyMotivation(
        EntityCommandBuffer.ParallelWriter ecb,
        int                                 entityIndex,
        Entity                              unit,
        in BehaviorCommand                  cmd)
    {
        ecb.AppendToBuffer(entityIndex, unit, new MotivationChangeRequest
        {
            needType   = (NeedType)cmd.IntParam,
            changeType = MotivationChangeType.Add,
            value      = cmd.FloatParam,
        });
    }
}
