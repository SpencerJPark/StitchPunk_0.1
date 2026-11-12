using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Converts a living unit into its zombie form via SwapBrainRequest and ChangeDesignRequest.
// Corpses are ReviveRequestSystem's job.
// Ordered before SwapBrainSystem so the swap and the re-skin both land on the frame it is requested.
[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveRequestSystem))]
[UpdateBefore(typeof(SwapBrainSystem))]
public partial struct ZombifySystem : ISystem
{
    private ComponentLookup<SwapBrainRequest>    swapBrainLookup;
    private ComponentLookup<ChangeDesignRequest> changeDesignLookup;
    private ComponentLookup<Undead>              undeadLookup;

    // Skin shape tag moves to the zombie designs; palette entries switch to alternate colours.
    private FixedString32Bytes skinPaletteGroup;
    private FixedString32Bytes zombiePaletteTag;

    // Deliberately NOT [BurstCompile]: building a FixedString from a string is managed code and
    // fails inside Burst with BC1016, so the two convention names are materialised once here and
    // handed to the job as data.
    public void OnCreate(ref SystemState state)
    {
        skinPaletteGroup = new FixedString32Bytes("Skin");
        zombiePaletteTag = new FixedString32Bytes("Zombie");

        state.RequireForUpdate<UnitDataLibrary>();
        swapBrainLookup    = state.GetComponentLookup<SwapBrainRequest>(false);
        changeDesignLookup = state.GetComponentLookup<ChangeDesignRequest>(false);
        undeadLookup       = state.GetComponentLookup<Undead>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        swapBrainLookup.Update(ref state);
        changeDesignLookup.Update(ref state);
        undeadLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new ZombifyJob
        {
            swapBrainLookup    = swapBrainLookup,
            changeDesignLookup = changeDesignLookup,
            undeadLookup       = undeadLookup,
            unitLibrary        = unitLibrary,
            skinPaletteGroup   = skinPaletteGroup,
            zombiePaletteTag   = zombiePaletteTag,
            deltaTime          = SystemAPI.Time.DeltaTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
// Dead disabled = alive. Units with no Dead component are filtered out rather than half-converted.
[WithDisabled(typeof(Dead))]
public partial struct ZombifyJob : IJobEntity
{
    public ComponentLookup<SwapBrainRequest>    swapBrainLookup;
    public ComponentLookup<ChangeDesignRequest> changeDesignLookup;
    public ComponentLookup<Undead>              undeadLookup;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> unitLibrary;
    public FixedString32Bytes skinPaletteGroup;
    public FixedString32Bytes zombiePaletteTag;
    public float deltaTime;

    // Single-threaded because the ComponentLookup writes below are unrestricted.
    public void Execute(
        Entity entity,
        ref ZombifyRequest zombifyRequest,
        in UnitData unitData,
        EnabledRefRW<ZombifyRequest> zombifyRequestEnabled)
    {
        if (zombifyRequest.delaySeconds > 0f)
        {
            zombifyRequest.delaySeconds -= deltaTime;
            if (zombifyRequest.delaySeconds > 0f)
                return;
        }

        // No converted form (or already in it): consume the request and stay as-is.
        UnitType targetUnitType = zombifyRequest.targetUnitType;
        if (targetUnitType == UnitType.None)
        {
            int sourceIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            targetUnitType  = sourceIndex >= 0
                ? unitLibrary.Value.units[sourceIndex].becomesUnitType
                : UnitType.None;
        }

        if (targetUnitType == UnitType.None
            || targetUnitType == unitData.unitType
            || !swapBrainLookup.HasComponent(entity))
        {
            zombifyRequestEnabled.ValueRW = false;
            return;
        }

        // A swap is already in flight (a revive stamped one). Overwriting it would lose that
        // conversion, so wait a frame with this request still enabled.
        if (swapBrainLookup.IsComponentEnabled(entity))
            return;

        swapBrainLookup[entity] = new SwapBrainRequest { newUnit = targetUnitType };
        swapBrainLookup.SetComponentEnabled(entity, true);

        if (changeDesignLookup.HasComponent(entity))
        {
            ChangeDesignRequest changeDesignRequest = new ChangeDesignRequest
            {
                alternateColorMode = AlternateColorMode.Enable,
            };
            changeDesignRequest.paletteChanges.Add(new PaletteEntry
            {
                group = skinPaletteGroup,
                tag   = zombiePaletteTag,
            });

            changeDesignLookup[entity] = changeDesignRequest;
            changeDesignLookup.SetComponentEnabled(entity, true);
        }

        if (undeadLookup.HasComponent(entity))
            undeadLookup.SetComponentEnabled(entity, true);

        zombifyRequestEnabled.ValueRW = false;
    }
}
