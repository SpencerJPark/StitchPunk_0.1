using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Rolls a random valid texture-array index per declared body part on first spawn, writing the
// result into PersistedDesign. Runs after AnimatorTargetInitSystem and before MinionRestoreApplySystem
// so a restored minion's saved indices overwrite the (wasted) roll before DesignApplySystem fans them
// out. One-shot: consumes RandomizeDesign by disabling it (pooled/reclaimed units keep their look).
[BurstCompile]
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(AnimatorTargetInitSystem))]
[UpdateBefore(typeof(MinionRestoreApplySystem))]
public partial struct DesignRandomizeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        uint seedBase = (uint)(SystemAPI.Time.ElapsedTime * 1000.0) + 1u; // never 0
        state.Dependency = new DesignRandomizeJob { seedBase = seedBase }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(NewlySpawned), typeof(RandomizeDesign))]
public partial struct DesignRandomizeJob : IJobEntity
{
    public uint seedBase;

    public void Execute(
        [EntityIndexInQuery] int idx,
        in DynamicBuffer<DesignPart> parts,
        in DynamicBuffer<DesignRange> ranges,
        ref PersistedDesign persistedDesign,
        EnabledRefRW<RandomizeDesign> randomizeDesignEnabled)
    {
        Random rng = Random.CreateFromIndex(seedBase + (uint)idx);
        persistedDesign.slots.Clear();

        for (int p = 0; p < parts.Length; p++)
        {
            DesignPart part = parts[p];
            if (part.rangeCount <= 0)
                continue;

            // Uniform over the union of valid slices: weight each range by its size.
            int total = 0;
            for (int r = 0; r < part.rangeCount; r++)
            {
                DesignRange range = ranges[part.rangeStart + r];
                total += range.max - range.min + 1;
            }
            if (total <= 0)
                continue;

            int roll = rng.NextInt(0, total); // [0, total)
            int chosen = ranges[part.rangeStart].min;
            for (int r = 0; r < part.rangeCount; r++)
            {
                DesignRange range = ranges[part.rangeStart + r];
                int size = range.max - range.min + 1;
                if (roll < size)
                {
                    chosen = range.min + roll;
                    break;
                }
                roll -= size;
            }

            persistedDesign.slots.Add(new DesignSlot { target = (int)part.target, imageIndex = chosen });
        }

        randomizeDesignEnabled.ValueRW = false;
    }
}
