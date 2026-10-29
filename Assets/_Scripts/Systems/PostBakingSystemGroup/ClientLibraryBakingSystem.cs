using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct ClientLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ClientLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        ClientLibrarySO librarySO = null;
        foreach (RefRO<ClientLibraryReference> reference in SystemAPI.Query<RefRO<ClientLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;
        if (librarySO.tuning == null)
        {
            UnityEngine.Debug.LogError("[ClientLibraryBaking] The client library has no TradeTuningSO assigned.", librarySO);
            return;
        }

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref ClientLibraryBlob root = ref builder.ConstructRoot<ClientLibraryBlob>();

        TradeTuningSO tuning = librarySO.tuning;
        root.tuning = new TradeTuningBlob
        {
            corpseValue = tuning.corpseValue,
            scrapMetalValue = tuning.scrapMetalValue,
            electricityValue = tuning.electricityValue,
            healthDifficultyWeight = tuning.healthDifficultyWeight,
            deadlineWeight = tuning.deadlineWeight,
            dangerWeight = tuning.dangerWeight,
            currencyPerDifficulty = tuning.currencyPerDifficulty,
            payoutMultiplierByTier = tuning.PayoutMultipliers(),
            minOffersByTier = tuning.MinOffers(),
            maxOffersByTier = tuning.MaxOffers(),
            householdOffersPerWeek = tuning.householdOffersPerWeek,
            startingReputation = tuning.startingReputation,
        };

        int clientCount = librarySO.clients != null ? librarySO.clients.Length : 0;
        BlobBuilderArray<ClientBlob> clientsBuilder = builder.Allocate(ref root.clients, clientCount);
        for (int clientIndex = 0; clientIndex < clientCount; clientIndex++)
        {
            ClientSO client = librarySO.clients[clientIndex];
            ref ClientBlob clientBlob = ref clientsBuilder[clientIndex];
            if (client == null)
            {
                builder.Allocate(ref clientBlob.orderPool, 0);
                continue;
            }

            clientBlob.clientId = client.clientId;
            clientBlob.kind = client.kind;
            clientBlob.homeDropOffId = client.homeDropOffId;
            clientBlob.baseOrderChancePerWeek = client.baseOrderChancePerWeek;
            clientBlob.tierThresholds = client.tiers.ToInt3();
            FillTemplates(builder, ref clientBlob.orderPool, client.orderPool);
        }

        HouseholdOrderTablesSO household = librarySO.household;
        root.household.nameCount = household != null && household.householdNames != null ? household.householdNames.Length : 0;
        FillTemplates(builder, ref root.household.templates, household != null ? household.templates : null);

        BlobAssetReference<ClientLibraryBlob> blobReference =
            builder.CreateBlobAssetReference<ClientLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<ClientLibrary> holder in SystemAPI.Query<RefRW<ClientLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobReference;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<ClientLibrary> holder in SystemAPI.Query<RefRW<ClientLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    private static void FillTemplates(BlobBuilder builder, ref BlobArray<OrderTemplateBlob> destination, OrderTemplateSO[] templates)
    {
        int templateCount = templates != null ? templates.Length : 0;
        BlobBuilderArray<OrderTemplateBlob> templatesBuilder = builder.Allocate(ref destination, templateCount);
        for (int templateIndex = 0; templateIndex < templateCount; templateIndex++)
        {
            OrderTemplateSO template = templates[templateIndex];
            ref OrderTemplateBlob templateBlob = ref templatesBuilder[templateIndex];
            if (template == null)
            {
                builder.Allocate(ref templateBlob.bonusResources, 0);
                continue;
            }

            templateBlob.unitType = template.unitType;
            templateBlob.quantityRange = template.quantityRange;
            templateBlob.minHealthFraction = template.minHealthFraction;
            templateBlob.dueDayRange = template.dueDayRange;
            templateBlob.minimumTier = template.minimumTier;
            templateBlob.weight = template.weight;

            int bonusCount = template.bonusResources != null ? template.bonusResources.Length : 0;
            BlobBuilderArray<BonusResourceBlob> bonusBuilder = builder.Allocate(ref templateBlob.bonusResources, bonusCount);
            for (int bonusIndex = 0; bonusIndex < bonusCount; bonusIndex++)
            {
                bonusBuilder[bonusIndex] = new BonusResourceBlob
                {
                    resourceType = template.bonusResources[bonusIndex].resourceType,
                    amount = template.bonusResources[bonusIndex].amount,
                };
            }
        }
    }
}
