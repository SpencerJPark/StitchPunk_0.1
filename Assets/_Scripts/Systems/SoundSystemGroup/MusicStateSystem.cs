using Unity.Burst;
using Unity.Entities;

// Maps the semantic WorldMood to per-layer target weights for the music stems. The AudioManager
// crossfades its layer volumes toward these targets each LateUpdate. Idempotent — only writes the
// MusicState singleton when the target actually changes.
[BurstCompile]
[UpdateInGroup(typeof(SoundSystemGroup))]
[UpdateAfter(typeof(WorldMoodSystem))]
public partial struct MusicStateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        Entity singleton = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singleton, new MusicState
        {
            exploreWeight = 1f,
            tensionWeight = 0f,
            combatWeight  = 0f,
        });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<WorldMood>())
            return;

        WorldMoodState mood = SystemAPI.GetSingleton<WorldMood>().state;

        MusicState target = mood switch
        {
            WorldMoodState.Combat  => new MusicState { exploreWeight = 0f, tensionWeight = 0f, combatWeight = 1f },
            WorldMoodState.Tension => new MusicState { exploreWeight = 0f, tensionWeight = 1f, combatWeight = 0f },
            _                      => new MusicState { exploreWeight = 1f, tensionWeight = 0f, combatWeight = 0f },
        };

        MusicState current = SystemAPI.GetSingleton<MusicState>();
        if (current.exploreWeight != target.exploreWeight ||
            current.tensionWeight != target.tensionWeight ||
            current.combatWeight  != target.combatWeight)
        {
            SystemAPI.GetSingletonRW<MusicState>().ValueRW = target;
        }
    }
}
