using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Rolls a per-character palette (one randomizable tag per group, e.g. Skin=Tan, Hair=Blonde) once and
// a random shape offset per design-driven body part on first spawn. Tags + counts are data-driven from
// the PartLibrary blob ranges (no enum reflection in Burst). The palette is shared, so hair + mustache
// always match. Runs after BodyPartInitSystem (buffer built) and before MinionRestoreApplySystem (a
// restored minion's saved palette/shapes overwrite the wasted roll before DesignApplySystem fans it
// out). One-shot: consumes RandomizeDesign by disabling it.
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

        // Roll one randomizable tag per group, shared across every part in that group.
        palette.groups.Clear();
        FixedList512Bytes<FixedString32Bytes> groups = default;
        DesignApplyUtil.CollectGroups(parts, ref library.Value, ref groups);
        for (int g = 0; g < groups.Length; g++)
        {
            FixedList512Bytes<FixedString32Bytes> tags = default;
            DesignApplyUtil.CollectRandomTags(parts, groups[g], ref library.Value, ref tags);
            if (tags.Length == 0)
                continue;

            FixedString32Bytes chosen = tags[random.NextInt(0, tags.Length)];
            palette.groups.Add(new PaletteEntry { group = groups[g], tag = chosen });
        }

        // Roll a shape offset within each part's tag pool.
        persistedDesign.slots.Clear();
        for (int p = 0; p < parts.Length; p++)
        {
            if ((parts[p].flags & BodyPartFlags.DesignSlot) == 0)
                continue;

            int defIndex = (int)parts[p].partDef;
            if (defIndex < 0 || defIndex >= library.Value.parts.Length)
                continue;

            ref PartDef def = ref library.Value.parts[defIndex];
            FixedString32Bytes tag = DesignApplyUtil.GetTag(palette.groups, def.group);
            int poolSize = DesignApplyUtil.TagPoolSize(ref def, tag);
            if (poolSize <= 0)
                continue;

            int offset = random.NextInt(0, poolSize);
            persistedDesign.slots.Add(new DesignSlot { target = (int)parts[p].target, shapeIndex = offset });
        }

        randomizeDesignEnabled.ValueRW = false;
    }
}
