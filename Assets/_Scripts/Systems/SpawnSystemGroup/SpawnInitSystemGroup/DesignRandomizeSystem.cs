using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Rolls a per-character palette (skin + hair colour) once and a random shape per design-driven body
// part on first spawn, writing the shapes into PersistedDesign and the colours into CharacterPalette.
// Colour counts and shape counts come from the PartLibrary blob (data-driven, no enum reflection in
// Burst). Runs after BodyPartInitSystem (buffer built) and before MinionRestoreApplySystem (a restored
// minion's saved shapes/palette overwrite the wasted roll before DesignApplySystem fans them out).
// One-shot: consumes RandomizeDesign by disabling it.
[BurstCompile]
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(BodyPartInitSystem))]
[UpdateBefore(typeof(MinionRestoreApplySystem))]
public partial struct DesignRandomizeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<PartLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PartLibrary library = SystemAPI.GetSingleton<PartLibrary>();
        if (!library.library.IsCreated)
            return;

        uint seedBase = (uint)(SystemAPI.Time.ElapsedTime * 1000.0) + 1u; // never 0
        state.Dependency = new DesignRandomizeJob
        {
            seedBase = seedBase,
            library  = library.library,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(NewlySpawned), typeof(RandomizeDesign))]
public partial struct DesignRandomizeJob : IJobEntity
{
    public uint seedBase;
    [ReadOnly] public BlobAssetReference<PartLibraryBlob> library;

    public void Execute(
        [EntityIndexInQuery] int indexInQuery,
        in DynamicBuffer<BodyPart> parts,
        ref CharacterPalette palette,
        ref PersistedDesign persistedDesign,
        EnabledRefRW<RandomizeDesign> randomizeDesignEnabled)
    {
        Random random = Random.CreateFromIndex(seedBase + (uint)indexInQuery);

        // Colour counts are data-driven: widest column set across parts on each axis.
        int skinColorCount = 1;
        int hairColorCount = 1;
        for (int i = 0; i < parts.Length; i++)
        {
            if ((parts[i].flags & BodyPartFlags.DesignSlot) == 0)
                continue;

            int defIndex = (int)parts[i].partDef;
            if (defIndex < 0 || defIndex >= library.Value.parts.Length)
                continue;

            ref PartDef def = ref library.Value.parts[defIndex];
            if (def.colorAxis == PaletteGroup.SkinColor)
                skinColorCount = math.max(skinColorCount, def.colorCount);
            else if (def.colorAxis == PaletteGroup.HairColor)
                hairColorCount = math.max(hairColorCount, def.colorCount);
        }

        palette.skinColor = (byte)random.NextInt(0, math.max(1, skinColorCount));
        palette.hairColor = (byte)random.NextInt(0, math.max(1, hairColorCount));

        persistedDesign.slots.Clear();
        for (int i = 0; i < parts.Length; i++)
        {
            if ((parts[i].flags & BodyPartFlags.DesignSlot) == 0)
                continue;

            int defIndex = (int)parts[i].partDef;
            if (defIndex < 0 || defIndex >= library.Value.parts.Length)
                continue;

            ref PartDef def = ref library.Value.parts[defIndex];
            int shapeCount = math.max(1, def.shapeCount);
            int shapeIndex = random.NextInt(0, shapeCount);
            persistedDesign.slots.Add(new DesignSlot { target = (int)parts[i].target, shapeIndex = shapeIndex });
        }

        randomizeDesignEnabled.ValueRW = false;
    }
}
