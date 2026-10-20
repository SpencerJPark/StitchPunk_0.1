using Unity.Collections;
using Unity.Entities;

// Capacity rules for exclusive targets (AF-D6): an interaction holds InteractionSO.maxOccupants units
// (0 = unlimited) but only for units performing THAT interaction's action, a loose item holds one, anything
// else is unlimited. The action match matters: every unit prefab inherits Interaction(Talk) from MaleCitizen,
// and keying on the component alone made each unit a one-attacker target, deadlocking every fight.
public static class TargetReservationUtils
{
    public const int UnlimitedCapacity = int.MaxValue;

    public static int CapacityFromMaxOccupants(int maxOccupants)
    {
        return maxOccupants > 0 ? maxOccupants : UnlimitedCapacity;
    }

    public static int GetCapacity(
        Entity                         target,
        ActionType                     unitAction,
        in ComponentLookup<Interaction> interactionLookup,
        in ComponentLookup<Item>       itemLookup,
        ref InteractionLibraryBlob     interactionLibrary)
    {
        if (target == Entity.Null) return UnlimitedCapacity;

        if (interactionLookup.TryGetComponent(target, out Interaction interaction))
        {
            if (interaction.action != unitAction) return UnlimitedCapacity;
            int interactionIndex = (int)interaction.action;
            if (interactionIndex < 0 || interactionIndex >= interactionLibrary.interactions.Length)
                return UnlimitedCapacity;
            return CapacityFromMaxOccupants(interactionLibrary.interactions[interactionIndex].maxOccupants);
        }

        return itemLookup.HasComponent(target) ? 1 : UnlimitedCapacity;
    }

    public static int CountOccupantsOtherThan(
        in NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget,
        Entity                                        target,
        Entity                                        unit)
    {
        int occupantCount = 0;
        if (!occupantsByTarget.TryGetFirstValue(target, out Entity occupant,
                out NativeParallelMultiHashMapIterator<Entity> iterator))
            return 0;
        do
        {
            if (occupant != unit) occupantCount++;
        }
        while (occupantsByTarget.TryGetNextValue(out occupant, ref iterator));
        return occupantCount;
    }

    public static bool IsOccupiedBy(
        in NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget,
        Entity                                        target,
        Entity                                        unit)
    {
        if (!occupantsByTarget.TryGetFirstValue(target, out Entity occupant,
                out NativeParallelMultiHashMapIterator<Entity> iterator))
            return false;
        do
        {
            if (occupant == unit) return true;
        }
        while (occupantsByTarget.TryGetNextValue(out occupant, ref iterator));
        return false;
    }

    public static bool IsFullForUnit(
        in NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget,
        Entity                                        target,
        Entity                                        unit,
        int                                           capacity)
    {
        if (capacity == UnlimitedCapacity) return false;
        return CountOccupantsOtherThan(in occupantsByTarget, target, unit) >= capacity;
    }
}
