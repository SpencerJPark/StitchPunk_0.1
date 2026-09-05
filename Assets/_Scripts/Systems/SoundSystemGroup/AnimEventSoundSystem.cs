using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

        EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

        EntityCommandBuffer.ParallelWriter parallelEcb =
            ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        state.Dependency = new AnimEventSoundJob
        {
            library = library,
            ecb     = parallelEcb,
        }.ScheduleParallel(state.Dependency);

        // Cutscene events belong to no actor — a second, single-threaded pass plays them
        // non-positionally (at the listener) instead of PlayOn-following a transform-less
        // request entity.
        float3 listenerPosition = SystemAPI.TryGetSingleton(out ListenerPosition listener)
            ? listener.value
            : float3.zero;

        state.Dependency = new CutsceneAnimEventSoundJob
        {
            library          = library,
            listenerPosition = listenerPosition,
            ecb              = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged),
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AnimEventsPending))]
[WithNone(typeof(CutscenePlay))]
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

[BurstCompile]
[WithAll(typeof(CutscenePlay), typeof(AnimEventsPending))]
public partial struct CutsceneAnimEventSoundJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AnimSoundEventMappingBlob> library;
    [ReadOnly] public float3 listenerPosition;
    public EntityCommandBuffer ecb;

    public void Execute(in DynamicBuffer<AnimEventOutput> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            if (library.Value.TryGetSound(events[i].eventKey, out SoundType sound))
                SoundUtil.Play(ref ecb, sound, listenerPosition);
        }
    }
}
