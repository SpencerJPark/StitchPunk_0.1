using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// The weekly roll as a pure function over the board buffers, so a fixture or the debug menu can run it without the system.
// Randomness is seeded from (worldSeed, weekNumber) only (TR-A8): same seed + week -> same orders.
public static class TradeOrderRoller
{
    private const int MaxOrdersPerWeek = TradeMath.OrderIdWeekStride - 1;

    private struct RollContext
    {
        public Random random;
        public TradeTuningBlob tuning;
        public BlobAssetReference<UnitLibraryBlob> unitLibrary;
        public NativeArray<TradeDropOff> dropOffs;
        public int weekNumber;
        public int issuedThisWeek;
    }

    public static void RollWeek(
        ref ClientLibraryBlob library,
        BlobAssetReference<UnitLibraryBlob> unitLibrary,
        NativeArray<TradeDropOff> dropOffs,
        DynamicBuffer<TradeOrder> orders,
        DynamicBuffer<ClientRelationship> relationships,
        uint worldSeed,
        int weekNumber)
    {
        RetireOldOrders(orders, weekNumber);

        RollContext context = new RollContext
        {
            random = Random.CreateFromIndex(TradeMath.RollSeed(worldSeed, weekNumber)),
            tuning = library.tuning,
            unitLibrary = unitLibrary,
            dropOffs = dropOffs,
            weekNumber = weekNumber,
            issuedThisWeek = 0,
        };

        for (int clientIndex = 0; clientIndex < library.clients.Length; clientIndex++)
        {
            ref ClientBlob client = ref library.clients[clientIndex];
            if (client.kind != ClientKind.Recurring)
                continue;

            int relationshipIndex = FindOrAddRelationship(relationships, client.clientId, context.tuning.startingReputation);
            ReputationTier tier = TradeMath.TierFromReputation(relationships[relationshipIndex].reputation, client.tierThresholds);

            int offerCount = context.tuning.minOffersByTier[(int)tier];
            for (int extraSlot = offerCount; extraSlot < context.tuning.maxOffersByTier[(int)tier]; extraSlot++)
            {
                if (context.random.NextFloat() < client.baseOrderChancePerWeek)
                    offerCount++;
            }

            for (int offerIndex = 0; offerIndex < offerCount; offerIndex++)
                TryAppendOrder(ref context, ref client.orderPool, client.clientId, client.homeDropOffId, false, tier, orders);
        }

        for (int householdIndex = 0; householdIndex < context.tuning.householdOffersPerWeek; householdIndex++)
        {
            int dropOffId = PickDropOffId(ref context, DropOffTag.Household);
            TryAppendOrder(ref context, ref library.household.templates, 0, dropOffId, true, ReputationTier.Stranger, orders);
        }
    }

    // Removes this week's rows (a re-roll), expires leftover offers, and prunes finished rows older than one week.
    // Accepted rows from earlier weeks stay: TradeOrderExpirySystem decides their fate.
    private static void RetireOldOrders(DynamicBuffer<TradeOrder> orders, int weekNumber)
    {
        for (int orderIndex = orders.Length - 1; orderIndex >= 0; orderIndex--)
        {
            TradeOrder order = orders[orderIndex];
            if (order.weekIssued == weekNumber)
            {
                orders.RemoveAt(orderIndex);
                continue;
            }

            if (order.state == TradeOrderState.Offered)
            {
                order.state = TradeOrderState.Expired;
                orders[orderIndex] = order;
            }

            if (order.state != TradeOrderState.Accepted && order.weekIssued < weekNumber - 1)
                orders.RemoveAt(orderIndex);
        }
    }

    private static int FindOrAddRelationship(DynamicBuffer<ClientRelationship> relationships, int clientId, int startingReputation)
    {
        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
        {
            if (relationships[relationshipIndex].clientId == clientId)
                return relationshipIndex;
        }

        relationships.Add(new ClientRelationship { clientId = clientId, reputation = startingReputation });
        return relationships.Length - 1;
    }

    private static int PickDropOffId(ref RollContext context, DropOffTag tag)
    {
        int matchCount = 0;
        for (int dropOffIndex = 0; dropOffIndex < context.dropOffs.Length; dropOffIndex++)
        {
            if (context.dropOffs[dropOffIndex].tag == tag)
                matchCount++;
        }

        if (matchCount == 0)
            return -1;

        int pickedMatch = context.random.NextInt(0, matchCount);
        for (int dropOffIndex = 0; dropOffIndex < context.dropOffs.Length; dropOffIndex++)
        {
            if (context.dropOffs[dropOffIndex].tag != tag)
                continue;
            if (pickedMatch == 0)
                return context.dropOffs[dropOffIndex].dropOffId;
            pickedMatch--;
        }

        return -1;
    }

    private static int DangerRatingOf(in RollContext context, int dropOffId)
    {
        for (int dropOffIndex = 0; dropOffIndex < context.dropOffs.Length; dropOffIndex++)
        {
            if (context.dropOffs[dropOffIndex].dropOffId == dropOffId)
                return context.dropOffs[dropOffIndex].dangerRating;
        }

        return 0;
    }

    private static bool TryAppendOrder(
        ref RollContext context,
        ref BlobArray<OrderTemplateBlob> pool,
        int authoredClientId,
        int dropOffId,
        bool isHouseholdOneOff,
        ReputationTier tier,
        DynamicBuffer<TradeOrder> orders)
    {
        if (context.issuedThisWeek >= MaxOrdersPerWeek)
            return false;

        float totalWeight = 0f;
        for (int templateIndex = 0; templateIndex < pool.Length; templateIndex++)
        {
            if (pool[templateIndex].minimumTier <= tier)
                totalWeight += pool[templateIndex].weight;
        }

        if (totalWeight <= 0f)
            return false;

        float pickedWeight = context.random.NextFloat() * totalWeight;
        int pickedTemplate = -1;
        for (int templateIndex = 0; templateIndex < pool.Length; templateIndex++)
        {
            if (pool[templateIndex].minimumTier > tier)
                continue;
            pickedTemplate = templateIndex;
            pickedWeight -= pool[templateIndex].weight;
            if (pickedWeight <= 0f)
                break;
        }

        ref OrderTemplateBlob template = ref pool[pickedTemplate];
        int quantityCap = math.max(template.quantityRange.x, TradeMath.MaxQuantityForTier(template.quantityRange, tier));
        int quantity = context.random.NextInt(template.quantityRange.x, quantityCap + 1);
        int daysAllowed = math.clamp(
            context.random.NextInt(template.dueDayRange.x, template.dueDayRange.y + 1), 1, TradeMath.DaysPerWeek);

        context.issuedThisWeek++;
        int orderId = TradeMath.OrderId(context.weekNumber, context.issuedThisWeek);

        float unitTierWeight = 1f;
        int unitIndex = context.unitLibrary.IsCreated ? context.unitLibrary.Value.FindByUnitType(template.unitType) : -1;
        if (unitIndex >= 0)
        {
            ref SummonCostBlob summonCost = ref context.unitLibrary.Value.units[unitIndex].summonCost;
            unitTierWeight = TradeMath.UnitTierWeight(summonCost.corpses, summonCost.scrapMetal, summonCost.electricity, in context.tuning);
        }

        float difficulty = TradeMath.ComputeDifficulty(
            quantity, unitTierWeight, template.minHealthFraction, daysAllowed, DangerRatingOf(in context, dropOffId), in context.tuning);

        orders.Add(new TradeOrder
        {
            orderId = orderId,
            clientId = isHouseholdOneOff ? -orderId : authoredClientId,
            templateIndex = pickedTemplate,
            dropOffId = dropOffId,
            unitType = template.unitType,
            quantity = (byte)quantity,
            minHealthFraction = template.minHealthFraction,
            daysAllowed = (byte)daysAllowed,
            weekIssued = context.weekNumber,
            difficulty = difficulty,
            currencyReward = TradeMath.ComputeCurrencyReward(difficulty, tier, in context.tuning),
            state = TradeOrderState.Offered,
        });
        return true;
    }
}
