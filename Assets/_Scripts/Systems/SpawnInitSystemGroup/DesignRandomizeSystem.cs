using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Rolls a per-character palette once on first spawn, then disables RandomizeDesign. Only tags in the rig's
// RandomTagOption pool roll, so conversion-only tags (e.g. "Zombie") never appear on a random spawn.
// Runs before MinionRestoreApplySystem so a restored minion's saved palette overwrites the wasted roll.
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
        state.RequireForUpdate<ColorPaletteLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PartLibrary library = SystemAPI.GetSingleton<PartLibrary>();
        ColorPaletteLibrary colorLibrary = SystemAPI.GetSingleton<ColorPaletteLibrary>();
        if (!library.library.IsCreated || !colorLibrary.blob.IsCreated)
            return;

        uint seedBase = (uint)(SystemAPI.Time.ElapsedTime * 1000.0) + 1u; // never 0
        state.Dependency = new DesignRandomizeJob
        {
            seedBase     = seedBase,
            library      = library.library,
            colorLibrary = colorLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(NewlySpawned), typeof(RandomizeDesign))]
public partial struct DesignRandomizeJob : IJobEntity
{
    public uint seedBase;
    [ReadOnly] public BlobAssetReference<PartLibraryBlob> library;
    [ReadOnly] public BlobAssetReference<ColorPaletteLibraryBlob> colorLibrary;

    public void Execute(
        [EntityIndexInQuery] int indexInQuery,
        in DynamicBuffer<BodyPart> parts,
        in DynamicBuffer<RandomTagOption> randomTags,
        ref CharacterPalette palette,
        ref PersistedDesign persistedDesign,
        EnabledRefRW<RandomizeDesign> randomizeDesignEnabled)
    {
        Random random = Random.CreateFromIndex(seedBase + (uint)indexInQuery);

        palette.groups.Clear();
        for (int optionIndex = 0; optionIndex < randomTags.Length; optionIndex++)
        {
            FixedString32Bytes group = randomTags[optionIndex].group;
            if (group.Length == 0) continue;
            if (GetTagCount(palette.groups, group) > 0) continue;

            int candidateCount = 0;
            for (int countIndex = 0; countIndex < randomTags.Length; countIndex++)
                if (randomTags[countIndex].group == group)
                    candidateCount++;

            int chosenOffset = random.NextInt(0, candidateCount);
            for (int pickIndex = 0; pickIndex < randomTags.Length; pickIndex++)
            {
                if (randomTags[pickIndex].group != group) continue;
                if (chosenOffset == 0)
                {
                    palette.groups.Add(new PaletteEntry { group = group, tag = randomTags[pickIndex].tag });
                    break;
                }
                chosenOffset--;
            }
        }

        // Full palette length: each design slot narrows the shared roll through its own [min,max] window at apply.
        palette.colors.Clear();
        palette.useAlternateColors = 0;
        FixedList64Bytes<ColorPaletteType> rolledPalettes = default;
        DesignApplyUtil.CollectPalettes(parts, ref library.Value, ref rolledPalettes);
        for (int paletteIndex = 0; paletteIndex < rolledPalettes.Length; paletteIndex++)
        {
            int typeIndex = (int)rolledPalettes[paletteIndex];
            if (typeIndex <= 0 || typeIndex >= colorLibrary.Value.palettes.Length)
                continue;

            int colorCount = math.min(colorLibrary.Value.palettes[typeIndex].colors.Length, byte.MaxValue + 1);
            if (colorCount <= 0)
                continue;

            byte chosenColorIndex = (byte)random.NextInt(0, colorCount);
            DesignApplyUtil.SetColorIndex(ref palette.colors, rolledPalettes[paletteIndex], chosenColorIndex);
        }

        persistedDesign.slots.Clear();
        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            if ((parts[partIndex].flags & BodyPartFlags.DesignSlot) == 0)
                continue;

            int defIndex = (int)parts[partIndex].unitPart;
            if (defIndex < 0 || defIndex >= library.Value.parts.Length)
                continue;

            ref PartDef def = ref library.Value.parts[defIndex];
            FixedString32Bytes tag = DesignApplyUtil.GetTag(palette.groups, def.group);
            int poolSize = DesignApplyUtil.TagPoolSize(ref def, tag);
            if (poolSize <= 0)
                continue;

            int offset = random.NextInt(0, poolSize);
            persistedDesign.slots.Add(new DesignSlot { target = (int)parts[partIndex].target, shapeIndex = offset });
        }

        randomizeDesignEnabled.ValueRW = false;
    }

    // GetTag cannot distinguish "unset" from "rolled an empty tag".
    private static int GetTagCount(in FixedList512Bytes<PaletteEntry> groups, in FixedString32Bytes group)
    {
        for (int entryIndex = 0; entryIndex < groups.Length; entryIndex++)
            if (groups[entryIndex].group == group)
                return 1;
        return 0;
    }
}
