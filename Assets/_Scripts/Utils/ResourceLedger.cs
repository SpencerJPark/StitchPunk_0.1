using Unity.Entities;

// Burst-friendly reads and spends against the player ledger. Spends read the LIVE stack, not
// pending deltas: two spends in one frame can both pass (accepted, see Gotchas.md).
public static class ResourceLedger
{
    public static int Get(in DynamicBuffer<ResourceStack> stack, ResourceType resourceType)
    {
        for (int rowIndex = 0; rowIndex < stack.Length; rowIndex++)
            if (stack[rowIndex].resourceType == resourceType) return stack[rowIndex].amount;
        return 0;
    }

    public static bool CanAfford(in DynamicBuffer<ResourceStack> stack, ResourceType resourceType, int amount)
    {
        return amount <= 0 || Get(in stack, resourceType) >= amount;
    }

    // Checks every line first, then queues all negative deltas — or stamps the first shortfall and queues nothing.
    public static bool TrySpend(
        in DynamicBuffer<ResourceStack> stack,
        ref DynamicBuffer<ResourceChangeRequest> requests,
        in SummonCostBlob cost,
        bool chargeCorpses,
        double elapsedTime,
        ref ResourceShortfall shortfall,
        EnabledRefRW<ResourceShortfall> shortfallEnabled)
    {
        int corpseCost = chargeCorpses ? cost.corpses : 0;
        if (!CheckLine(in stack, ResourceType.Corpse, corpseCost, elapsedTime, ref shortfall, shortfallEnabled)) return false;
        if (!CheckLine(in stack, ResourceType.ScrapMetal, cost.scrapMetal, elapsedTime, ref shortfall, shortfallEnabled)) return false;
        if (!CheckLine(in stack, ResourceType.Electricity, cost.electricity, elapsedTime, ref shortfall, shortfallEnabled)) return false;

        QueueSpend(ref requests, ResourceType.Corpse, corpseCost);
        QueueSpend(ref requests, ResourceType.ScrapMetal, cost.scrapMetal);
        QueueSpend(ref requests, ResourceType.Electricity, cost.electricity);
        return true;
    }

    public static bool TrySpendSingle(
        in DynamicBuffer<ResourceStack> stack,
        ref DynamicBuffer<ResourceChangeRequest> requests,
        ResourceType resourceType,
        int amount,
        double elapsedTime,
        ref ResourceShortfall shortfall,
        EnabledRefRW<ResourceShortfall> shortfallEnabled)
    {
        if (!CheckLine(in stack, resourceType, amount, elapsedTime, ref shortfall, shortfallEnabled)) return false;
        QueueSpend(ref requests, resourceType, amount);
        return true;
    }

    public static void QueueAdd(ref DynamicBuffer<ResourceChangeRequest> requests, ResourceType resourceType, int amount)
    {
        if (amount == 0) return;
        requests.Add(new ResourceChangeRequest { resourceType = resourceType, changeType = ResourceChangeType.Add, value = amount });
    }

    private static void QueueSpend(ref DynamicBuffer<ResourceChangeRequest> requests, ResourceType resourceType, int amount)
    {
        if (amount <= 0) return;
        QueueAdd(ref requests, resourceType, -amount);
    }

    private static bool CheckLine(
        in DynamicBuffer<ResourceStack> stack,
        ResourceType resourceType,
        int amount,
        double elapsedTime,
        ref ResourceShortfall shortfall,
        EnabledRefRW<ResourceShortfall> shortfallEnabled)
    {
        if (amount <= 0) return true;
        int available = Get(in stack, resourceType);
        if (available >= amount) return true;

        shortfall = new ResourceShortfall
        {
            resourceType = resourceType,
            missing      = amount - available,
            stampedTime  = elapsedTime,
        };
        shortfallEnabled.ValueRW = true;
        return false;
    }
}
