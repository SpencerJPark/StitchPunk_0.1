using Unity.Entities;

// Payout and reputation bookkeeping for a delivered order, kept out of TradeDeliverySystem so the system reads as the flow.
public static class TradePayout
{
    public static void QueuePayout(
        TradeOrder order, bool libraryIsAvailable, in BlobAssetReference<ClientLibraryBlob> library,
        DynamicBuffer<ResourceChangeRequest> ledgerRequests)
    {
        ledgerRequests.Add(new ResourceChangeRequest
        {
            resourceType = ResourceType.Currency,
            changeType = ResourceChangeType.Add,
            value = order.currencyReward,
        });

        if (!libraryIsAvailable || !library.IsCreated)
            return;

        ref ClientLibraryBlob libraryBlob = ref library.Value;
        ref BlobArray<OrderTemplateBlob> pool = ref ResolvePool(ref libraryBlob, order.clientId, out bool poolFound);
        if (!poolFound || order.templateIndex < 0 || order.templateIndex >= pool.Length)
            return;

        ref BlobArray<BonusResourceBlob> bonuses = ref pool[order.templateIndex].bonusResources;
        for (int bonusIndex = 0; bonusIndex < bonuses.Length; bonusIndex++)
        {
            ledgerRequests.Add(new ResourceChangeRequest
            {
                resourceType = bonuses[bonusIndex].resourceType,
                changeType = ResourceChangeType.Add,
                value = bonuses[bonusIndex].amount,
            });
        }
    }

    // Household one-offs (negative id) read the shared household table; recurring clients their own pool.
    private static ref BlobArray<OrderTemplateBlob> ResolvePool(ref ClientLibraryBlob library, int clientId, out bool found)
    {
        if (clientId < 0)
        {
            found = true;
            return ref library.household.templates;
        }

        for (int clientIndex = 0; clientIndex < library.clients.Length; clientIndex++)
        {
            if (library.clients[clientIndex].clientId == clientId)
            {
                found = true;
                return ref library.clients[clientIndex].orderPool;
            }
        }

        found = false;
        return ref library.household.templates;
    }

    // Recurring clients only: household one-offs never earn a relationship row.
    public static void RecordDelivery(
        DynamicBuffer<ClientRelationship> relationships, int clientId, bool deliveredEarly, in TradeTuningBlob tuning)
    {
        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
        {
            if (relationships[relationshipIndex].clientId != clientId)
                continue;

            ClientRelationship relationship = relationships[relationshipIndex];
            int gain = tuning.reputationGainDelivered + (deliveredEarly ? tuning.reputationGainDeliveredEarly : 0);
            relationship.reputation = System.Math.Min(100, relationship.reputation + gain);
            relationship.ordersCompleted++;
            relationships[relationshipIndex] = relationship;
            return;
        }
    }
}
