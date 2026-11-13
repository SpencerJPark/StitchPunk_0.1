using Unity.Entities;
using Unity.Mathematics;

// Each Try method returns false (and writes nothing) when the minion lacks the order's component.
public static class MinionOrderWriter
{
    // Slot 0 stays the player's own spot, so callers pass slots starting at 1.
    public static bool TryGiveFollowOrder(EntityManager entityManager, Entity minion, int formationSlotIndex)
    {
        if (!entityManager.HasComponent<MinionStandingOrder>(minion)) return false;
        SquadCommandManager.EndGather(entityManager, minion);
        entityManager.SetComponentData(minion, new MinionStandingOrder
        {
            type = StandingOrderType.FollowCommander,
            formationSlotIndex = formationSlotIndex,
            engageRadius = 8f,
            lastIssuedDestination = new float3(float.MaxValue),
            lastIssuedTime = -1000,
        });
        return true;
    }

    public static bool TryGiveAttackOrder(EntityManager entityManager, Entity minion, Entity targetEntity)
    {
        if (!entityManager.HasComponent<OnMinionAttackCommand>(minion)) return false;
        entityManager.SetComponentData(minion, new OnMinionAttackCommand { targetEntity = targetEntity });
        entityManager.SetComponentEnabled<OnMinionAttackCommand>(minion, true);
        return true;
    }

    public static bool TryGiveReturnOrder(EntityManager entityManager, Entity minion)
    {
        if (!entityManager.HasComponent<OnMinionReturnCommand>(minion)) return false;
        entityManager.SetComponentEnabled<OnMinionReturnCommand>(minion, true);
        return true;
    }
}
