using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// The "mix" step: gathers every one-shot PlaySound + enabled LoopingSound this frame, scores each by
// priority + distance to the listener, applies a per-type maxConcurrent cap, and writes the top ≤N
// into the ResolvedVoices singleton the AudioManager reads each LateUpdate. One-shots are destroyed
// after collection (the LogMessage lifecycle); loops persist as components.
//
// Not [BurstCompile]: it performs a structural change (DestroyEntity on the PlaySound query) and is
// the cull pass — low volume relative to gameplay. Profile + optionally jobify per spec phase 4.
[UpdateInGroup(typeof(SoundSystemGroup))]
public partial struct VoiceSelectionSystem : ISystem
{
    private const int VoicePool = 32;

    private ComponentLookup<LocalTransform> _transformLookup;
    private EntityQuery _playSoundQuery;
    private Entity _singleton;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _playSoundQuery  = state.GetEntityQuery(ComponentType.ReadOnly<PlaySound>());

        _singleton = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(_singleton, new ResolvedVoices
        {
            voices = new NativeList<ResolvedVoice>(64, Allocator.Persistent)
        });
        state.EntityManager.AddComponentData(_singleton, new ListenerPosition { value = float3.zero });
        state.EntityManager.AddComponentData(_singleton, new CameraView { center = float3.zero, viewRadius = 25f });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.HasComponent<ResolvedVoices>(_singleton))
        {
            ResolvedVoices resolved = state.EntityManager.GetComponentData<ResolvedVoices>(_singleton);
            if (resolved.voices.IsCreated)
                resolved.voices.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        // No audio library yet (assets not authored): still consume one-shots so they never leak.
        if (!SystemAPI.HasSingleton<SoundLibrary>())
        {
            SystemAPI.GetSingletonRW<ResolvedVoices>().ValueRW.voices.Clear();
            state.EntityManager.DestroyEntity(_playSoundQuery);
            return;
        }

        _transformLookup.Update(ref state);

        BlobAssetReference<SoundLibraryBlob> libraryRef = SystemAPI.GetSingleton<SoundLibrary>().library;
        ref SoundLibraryBlob lib = ref libraryRef.Value;
        float3 listenerPos = SystemAPI.HasSingleton<ListenerPosition>()
            ? SystemAPI.GetSingleton<ListenerPosition>().value
            : float3.zero;

        Random rng = Random.CreateFromIndex((uint)(SystemAPI.Time.ElapsedTime * 1000.0) + 1u);

        NativeList<Candidate> candidates = new NativeList<Candidate>(64, Allocator.Temp);

        // One-shots.
        foreach (RefRO<PlaySound> playSound in SystemAPI.Query<RefRO<PlaySound>>())
        {
            PlaySound ps = playSound.ValueRO;
            int index = (int)ps.type;
            if (index < 0 || index >= lib.sounds.Length) continue;
            ref readonly SoundBlob blob = ref lib.sounds[index];

            float3 pos = ps.position;
            if (ps.followSource && _transformLookup.HasComponent(ps.source))
                pos = _transformLookup[ps.source].Position;

            float dist = math.distance(pos, listenerPos);
            if (dist > blob.maxDistance) continue;

            candidates.Add(new Candidate
            {
                voice = new ResolvedVoice
                {
                    type           = ps.type,
                    variationIndex = blob.variationCount > 0 ? rng.NextInt(0, blob.variationCount) : 0,
                    pos            = pos,
                    source         = ps.source,
                    followSource   = ps.followSource,
                    volume         = rng.NextFloat(blob.volumeMin, blob.volumeMax) * ps.volumeMul,
                    pitch          = rng.NextFloat(blob.pitchMin, blob.pitchMax) * ps.pitchMul,
                    loop           = false,
                    bus            = ps.hasBusOverride ? ps.busOverride : blob.bus,
                    stableVoiceKey = 0,
                },
                score         = Score(blob.priority, dist, blob.maxDistance, false),
                maxConcurrent = blob.maxConcurrent,
            });
        }

        // Loops (enabled only) — stable per-frame volume/pitch so they don't jitter.
        foreach (var (loopRO, xformRO, entity) in
            SystemAPI.Query<RefRO<LoopingSound>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            LoopingSound loop = loopRO.ValueRO;
            int index = (int)loop.type;
            if (index < 0 || index >= lib.sounds.Length) continue;
            ref readonly SoundBlob blob = ref lib.sounds[index];

            float3 pos = xformRO.ValueRO.Position;
            float dist = math.distance(pos, listenerPos);
            if (dist > blob.maxDistance) continue; // virtualize out-of-range loops

            candidates.Add(new Candidate
            {
                voice = new ResolvedVoice
                {
                    type           = loop.type,
                    variationIndex = 0,
                    pos            = pos,
                    source         = entity,
                    followSource   = true,
                    volume         = (blob.volumeMin + blob.volumeMax) * 0.5f * loop.volumeMul,
                    pitch          = (blob.pitchMin + blob.pitchMax) * 0.5f * loop.pitchMul,
                    loop           = true,
                    bus            = blob.bus,
                    stableVoiceKey = entity.Index,
                },
                score         = Score(blob.priority, dist, blob.maxDistance, true),
                maxConcurrent = blob.maxConcurrent,
            });
        }

        candidates.Sort(new CandidateComparer());

        // Greedy fill: highest score first, respecting the global pool and per-type cap.
        ResolvedVoices resolved = SystemAPI.GetSingletonRW<ResolvedVoices>().ValueRW;
        resolved.voices.Clear();

        NativeArray<int> perTypeCount = new NativeArray<int>(lib.sounds.Length, Allocator.Temp);
        for (int i = 0; i < candidates.Length; i++)
        {
            if (resolved.voices.Length >= VoicePool) break;

            Candidate candidate = candidates[i];
            int typeIndex = (int)candidate.voice.type;
            if (perTypeCount[typeIndex] >= candidate.maxConcurrent) continue;

            perTypeCount[typeIndex]++;
            resolved.voices.Add(candidate.voice);
        }

        perTypeCount.Dispose();
        candidates.Dispose();

        // Consume one-shots (loops persist as components).
        state.EntityManager.DestroyEntity(_playSoundQuery);
    }

    // Priority dominates; distance is a tiebreaker within a priority band. Loops get a small bonus so
    // an equal-priority one-shot never evicts an already-playing loop.
    private static float Score(byte priority, float dist, float maxDistance, bool loop)
    {
        float distNorm = maxDistance > 0f ? math.saturate(dist / maxDistance) : 0f;
        float score = priority - distNorm * 256f;
        if (loop) score += 0.5f;
        return score;
    }

    private struct Candidate
    {
        public ResolvedVoice voice;
        public float         score;
        public int           maxConcurrent;
    }

    private struct CandidateComparer : IComparer<Candidate>
    {
        public int Compare(Candidate a, Candidate b) => b.score.CompareTo(a.score);
    }
}
