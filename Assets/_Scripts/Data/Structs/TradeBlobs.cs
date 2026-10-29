using Unity.Entities;
using Unity.Mathematics;

// Plain value struct (no BlobArray) so TradeMath stays EditMode-testable without a BlobBuilder.
public struct TradeTuningBlob
{
    public float corpseValue;
    public float scrapMetalValue;
    public float electricityValue;
    public float healthDifficultyWeight;
    public float deadlineWeight;
    public float dangerWeight;
    public float currencyPerDifficulty;
    public float4 payoutMultiplierByTier;
    public int4 minOffersByTier;
    public int4 maxOffersByTier;
    public int householdOffersPerWeek;
    public int startingReputation;
}

public struct BonusResourceBlob
{
    public ResourceType resourceType;
    public int amount;
}

public struct OrderTemplateBlob
{
    public UnitType unitType;
    public int2 quantityRange;
    public float minHealthFraction;
    public int2 dueDayRange;
    public ReputationTier minimumTier;
    public float weight;
    public BlobArray<BonusResourceBlob> bonusResources;
}

public struct ClientBlob
{
    public int clientId;
    public ClientKind kind;
    public int homeDropOffId;
    public float baseOrderChancePerWeek;
    public int3 tierThresholds;   // Known / Trusted / Partner
    public BlobArray<OrderTemplateBlob> orderPool;
}

public struct HouseholdTablesBlob
{
    public int nameCount;
    public BlobArray<OrderTemplateBlob> templates;
}

// Display strings stay out of the blob: the UI looks names and flavour text up from the SOs by id.
public struct ClientLibraryBlob
{
    public BlobArray<ClientBlob> clients;
    public HouseholdTablesBlob household;
    public TradeTuningBlob tuning;
}
