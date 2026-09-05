using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Converts a LIVING unit into its zombie form by composing the two mechanisms that already exist:
// SwapBrainRequest (unit type, faction, attacks, motivations) and ChangeDesignRequest (skin tag +
// alternate palette colours). Corpses are ReviveRequestSystem's job — this one only touches units
// whose Dead is disabled.
//
// Ordered between ReviveRequestSystem and SwapBrainSystem so the swap this stamps is consumed in
// the SAME frame, and DesignSystemGroup (after Health) applies the re-skin the same frame too — the
// conversion is visible on the frame it is requested.
[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveRequestSystem))]
[UpdateBefore(typeof(SwapBrainSystem))]
public partial struct ZombifySystem : ISystem
{
    private ComponentLookup<SwapBrainRequest>    swapBrainLookup;
    private ComponentLookup<ChangeDesignRequest> changeDesignLookup;
    private ComponentLookup<Undead>              undeadLookup;

    // The documented conversion convention (see CharacterPalette / ChangeDesignRequest): the skin
    // group's shape tag moves to the zombie designs, and every palette entry switches to its
    // alternative colour, so the character keeps its rolled identity in zombie form.
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
// Dead disabled = alive. A unit with no Dead component (nothing that HealthAuthoring baked) is not
// convertible and is filtered out here rather than half-converted.
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

    // Single-threaded .Schedule() — the ComponentLookup writes below are unrestricted, and a
    // conversion is a rare one-shot with nothing to gain from parallelism.
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

        // No declared converted form (or already in it) — consume and stay as-is, mirroring the
        // revive path's becomesUnitType == None rule.
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

        // A swap is already in flight this frame (a revive stamped one just before us, and
        // SwapBrainSystem will consume it below). Overwriting it would lose that conversion, and
        // SwapBrainSystem's end-of-frame ECB would disable the request we just enabled — so wait a
        // frame with the request still enabled instead.
        if (swapBrainLookup.IsComponentEnabled(entity))
            return;

        swapBrainLookup[entity] = new SwapBrainRequest { newUnit = targetUnitType };
        swapBrainLookup.SetComponentEnabled(entity, true);

        // SwapBrainSystem fires ActionInterruptRequest itself, so the live behavior is torn down
        // and re-decided with the new brain — nothing to fire here.

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

        // Same end state as a reanimated corpse: the unit is undead from here on.
        if (undeadLookup.HasComponent(entity))
            undeadLookup.SetComponentEnabled(entity, true);

        zombifyRequestEnabled.ValueRW = false;
    }
}
