using Unity.Burst;
using Unity.Entities;

// RequestPickup — claims a loose item (EquipBy/AttachedTo) and enables PickupRequest +
// AttachItemRequest for ItemEquipSystemGroup to consume next frame. Fire-and-advance.
[BurstCompile]
public static class ItemCommands
{
    public static void RunRequestPickup(
        ref BehaviorCommandContext context,
        Entity                     unit,
        ref StateMachine           stateMachine)
    {
        Entity item = stateMachine.targetEntity;
        if (item == Entity.Null || !context.pickupRequestLookup.HasComponent(item)) return;

        // Re-validate: another unit may have claimed the item during the approach.
        if (context.equipByLookup.HasComponent(item)
            && context.equipByLookup[item].owner != Entity.Null
            && context.equipByLookup[item].owner != unit)
            return;

        if (context.equipByLookup.HasComponent(item))
            context.equipByLookup[item] = new EquipBy { owner = unit };

        Entity socket = Entity.Null;
        if (context.unitEquipLookup.TryGetComponent(unit, out UnitEquip unitEquip))
            socket = unitEquip.socketEntity;

        if (context.attachedToLookup.HasComponent(item))
            context.attachedToLookup[item] = new AttachedTo { socket = socket };

        context.pickupRequestLookup.SetComponentEnabled(item, true);

        if (context.attachItemRequestLookup.HasComponent(item))
            context.attachItemRequestLookup.SetComponentEnabled(item, true);
    }
}
