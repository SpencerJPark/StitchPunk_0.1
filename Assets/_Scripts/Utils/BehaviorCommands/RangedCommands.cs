using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// WindUp - blocking telegraph before a ranged attack. On its first frame it locks the target's position
// into RangedAim (enabled, never updated afterwards), then runs CommandTimer up to the held item's
// aiWindUpSeconds (or cmd.FloatParam). RangedAim stays enabled for RequestThrow / RequestShoot to consume.
// RequestShoot - fire-and-advance; enables AiShootRequest at the locked aim point. The gun stays in hand.
[BurstCompile]
public static class RangedCommands
{
    public static void RunWindUp(
        ref BehaviorCommandContext context,
        Entity                     unit,
        ref StateMachine           stateMachine,
        in BehaviorCommand         cmd)
    {
        if (stateMachine.CommandTimer == 0f)
        {
            if (stateMachine.targetEntity == Entity.Null
                || !context.transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform targetTransform)
                || !context.rangedAimLookup.HasComponent(unit))
            {
                stateMachine.CurrentCommandIndex++;
                stateMachine.CommandTimer = 0f;
                return;
            }

            float windUpSeconds = ResolveWindUpSeconds(ref context, unit, cmd.FloatParam);
            context.rangedAimLookup[unit] = new RangedAim
            {
                aimPoint        = targetTransform.Position,
                windUpTotal     = windUpSeconds,
                windUpRemaining = windUpSeconds,
            };
            context.rangedAimLookup.SetComponentEnabled(unit, true);
        }

        stateMachine.CommandTimer += context.deltaTime;

        RangedAim rangedAim = context.rangedAimLookup[unit];
        rangedAim.windUpRemaining = math.max(rangedAim.windUpTotal - stateMachine.CommandTimer, 0f);
        context.rangedAimLookup[unit] = rangedAim;

        if (stateMachine.CommandTimer >= rangedAim.windUpTotal)
        {
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    public static void RunRequestShoot(
        ref BehaviorCommandContext context,
        Entity                     unit,
        in StateMachine            stateMachine)
    {
        bool   hasAimPoint = false;
        float3 aimPoint    = float3.zero;
        // Cleared before the early returns so a flash can never stick on a unit that can't fire.
        if (context.rangedAimLookup.HasComponent(unit) && context.rangedAimLookup.IsComponentEnabled(unit))
        {
            aimPoint    = context.rangedAimLookup[unit].aimPoint;
            hasAimPoint = true;
            context.rangedAimLookup.SetComponentEnabled(unit, false);
        }

        if (stateMachine.targetEntity == Entity.Null) return;
        if (!context.unitEquipLookup.TryGetComponent(unit, out UnitEquip unitEquip)
            || unitEquip.equipItemEntity == Entity.Null)
            return;

        context.ecb.SetComponent(context.entityIndex, unit, new AiShootRequest
        {
            target      = stateMachine.targetEntity,
            aimPoint    = aimPoint,
            hasAimPoint = hasAimPoint,
        });
        context.ecb.SetComponentEnabled<AiShootRequest>(context.entityIndex, unit, true);
    }

    private static float ResolveWindUpSeconds(ref BehaviorCommandContext context, Entity unit, float fallbackSeconds)
    {
        if (!context.itemLibrary.IsCreated) return fallbackSeconds;
        if (!context.unitEquipLookup.TryGetComponent(unit, out UnitEquip unitEquip)
            || unitEquip.equipItemEntity == Entity.Null)
            return fallbackSeconds;
        if (!context.itemLookup.TryGetComponent(unitEquip.equipItemEntity, out Item item)) return fallbackSeconds;

        int itemIndex = (int)item.itemType;
        ref ItemLibraryBlob itemLibraryBlob = ref context.itemLibrary.Value;
        if (itemIndex < 0 || itemIndex >= itemLibraryBlob.items.Length) return fallbackSeconds;

        float blobSeconds = itemLibraryBlob.items[itemIndex].aiWindUpSeconds;
        return blobSeconds > 0f ? blobSeconds : fallbackSeconds;
    }
}
