using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Writes the global WorldMood singleton from what the camera can see: an active attack in view →
// Combat; otherwise a threatened unit (non-empty ThreatEntry buffer) in view → Tension; else Explore.
// Written idempotently through WorldMoodUtil (no write when unchanged). Read by MusicStateSystem now
// and NPC behaviours later.
[BurstCompile]
[UpdateInGroup(typeof(SoundSystemGroup))]
public partial struct WorldMoodSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        Entity singleton = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singleton, new WorldMood { state = WorldMoodState.Explore });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<CameraView>())
            return;

        CameraView view = SystemAPI.GetSingleton<CameraView>();
        float radiusSq = view.viewRadius * view.viewRadius;

        bool combat = false;
        foreach (var (attack, xform) in
            SystemAPI.Query<RefRO<AttackRequest>, RefRO<LocalTransform>>())
        {
            if (math.distancesq(xform.ValueRO.Position, view.center) <= radiusSq)
            {
                combat = true;
                break;
            }
        }

        bool tension = false;
        if (!combat)
        {
            foreach (var (threats, xform) in
                SystemAPI.Query<DynamicBuffer<ThreatEntry>, RefRO<LocalTransform>>())
            {
                if (threats.Length > 0 && math.distancesq(xform.ValueRO.Position, view.center) <= radiusSq)
                {
                    tension = true;
                    break;
                }
            }
        }

        WorldMoodState desired = combat
            ? WorldMoodState.Combat
            : tension ? WorldMoodState.Tension : WorldMoodState.Explore;

        WorldMoodUtil.Set(ref state, desired);
    }
}
