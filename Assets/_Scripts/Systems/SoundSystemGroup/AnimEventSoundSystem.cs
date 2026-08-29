using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// First real AnimEvent consumer: maps a clip's authored event keys to SoundType via
// AnimSoundEventLibrary and fires the sound on the emitting actor. Runs in SoundSystemGroup
// (LateSimulation) — after the toolkit's own emission the same frame, so no added latency versus
// the legacy AnimationSoundMarkerSystem it replaces. Written as the template the animation-event
// timing plan's consumers will follow: read AnimEventOutput, map the key, fire-and-forget.
[BurstCompile]
[UpdateInGroup(typeof(SoundSystemGroup))]
public partial struct AnimEventSoundSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimSoundEventLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<AnimSoundEventMappingBlob> library =
            SystemAPI.GetSingleton<AnimSoundEventLibrary>().blob;
        if (!library.IsCreated) return;

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new AnimEventSoundJob
        {
            library = library,
            ecb     = ecb,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AnimEventsPending))]
public partial struct AnimEventSoundJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimSoundEventMappingBlob> library;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        [EntityIndexInQuery] int sortKey,
        Entity entity,
        in DynamicBuffer<AnimEventOutput> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            if (library.Value.TryGetSound(events[i].eventKey, out SoundType sound))
                SoundUtil.PlayOn(ref ecb, sortKey, sound, entity);
        }
    }
}
