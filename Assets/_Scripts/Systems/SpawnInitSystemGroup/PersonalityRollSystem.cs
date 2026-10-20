using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Rolls per-instance personality here, not in the baker: every instance of one prefab shares the baked
// entity data, so a baker roll would give the whole crowd identical values. Runs on PersonalityRollPending
// (first spawn / subscene placement) and on NewlySpawned (pool reuse), which also clears drunk/talk/emote state.
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateBefore(typeof(SpawnInitCleanupSystem))]
public partial struct PersonalityRollSystem : ISystem
{
    const float ViceDecayAtFullVice = 0.6f;

    public void OnCreate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PersonalitySeed>())
        {
            Entity seedEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(seedEntity, new PersonalitySeed
            {
                value = (uint)System.Environment.TickCount | 1u,
            });
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PersonalitySeed seed = SystemAPI.GetSingleton<PersonalitySeed>();
        state.Dependency = new PersonalityRollJob
        {
            seed = seed.value,
            // Pool reuse keeps Index/Version, so salt by time or a recycled body would roll the same person again.
            rollSalt = (uint)(SystemAPI.Time.ElapsedTime * 1000.0),
            viceDecayAtFullVice = ViceDecayAtFullVice,
            newlySpawnedLookup = SystemAPI.GetComponentLookup<NewlySpawned>(true),
            motivationLookup = SystemAPI.GetBufferLookup<Motivation>(),
            intoxicationLookup = SystemAPI.GetComponentLookup<Intoxication>(),
            conversationLookup = SystemAPI.GetComponentLookup<Conversation>(),
            emoteStateLookup = SystemAPI.GetComponentLookup<EmoteState>(),
        }.ScheduleParallel(state.Dependency);
    }
}

// WithPresent: an EnabledRefRW param alone skips entities whose flag is disabled (Gotchas), and pool reuse arrives with the flag off.
[BurstCompile]
[WithPresent(typeof(PersonalityRollPending))]
public partial struct PersonalityRollJob : IJobEntity
{
    public uint seed;
    public uint rollSalt;
    public float viceDecayAtFullVice;
    [ReadOnly] public ComponentLookup<NewlySpawned> newlySpawnedLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<Motivation> motivationLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Intoxication> intoxicationLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Conversation> conversationLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<EmoteState> emoteStateLookup;

    public void Execute(
        Entity entity,
        ref DynamicBuffer<PersonalityAttributes> personalities,
        EnabledRefRW<PersonalityRollPending> personalityRollPendingEnabled)
    {
        bool isPoolReuse = newlySpawnedLookup.HasComponent(entity) && newlySpawnedLookup.IsComponentEnabled(entity);
        if (!personalityRollPendingEnabled.ValueRO && !isPoolReuse)
            return;

        Random random = Random.CreateFromIndex(math.hash(new uint4((uint)entity.Index, (uint)entity.Version, seed, rollSalt)));

        float vice = 0f;
        for (int traitIndex = 0; traitIndex < personalities.Length; traitIndex++)
        {
            PersonalityAttributes attribute = personalities[traitIndex];
            attribute.value = math.saturate(attribute.baseValue + random.NextFloat(-1f, 1f) * attribute.variance);
            personalities[traitIndex] = attribute;
            if (attribute.personality == PersonalityTypes.Partier)
                vice = attribute.value;
        }

        if (motivationLookup.HasBuffer(entity))
        {
            DynamicBuffer<Motivation> motivations = motivationLookup[entity];
            for (int motivationIndex = 0; motivationIndex < motivations.Length; motivationIndex++)
            {
                Motivation motivation = motivations[motivationIndex];
                if (motivation.needType != NeedType.Vice)
                    continue;
                motivation.decayRate = viceDecayAtFullVice * vice * vice;
                motivations[motivationIndex] = motivation;
            }
        }

        if (isPoolReuse)
        {
            if (intoxicationLookup.HasComponent(entity))
            {
                intoxicationLookup[entity] = default;
                intoxicationLookup.SetComponentEnabled(entity, false);
            }
            if (conversationLookup.HasComponent(entity))
                conversationLookup.SetComponentEnabled(entity, false);
            if (emoteStateLookup.HasComponent(entity))
                emoteStateLookup[entity] = default;
        }

        personalityRollPendingEnabled.ValueRW = false;
    }
}
