using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Force-disables Ragdoll2D and Ragdoll2DJoint on every child entity of a newly
/// spawned body at the start of its first alive frame.
///
/// WHY THIS IS NEEDED — two failure modes:
///   1. ECB.Instantiate does not reliably copy IEnableableComponent enabled bits on
///      child entities. Ragdoll2D / Ragdoll2DJoint can arrive enabled even though
///      they were disabled on the prefab.
///   2. Reclaimed pool entities retain ragdoll state from their previous death
///      (Ragdoll2D / RagdollJoint stay enabled while the entity sits in the pool).
///
/// If either component is enabled when the unit dies, Ragdoll2DInitSystem sees it as
/// "already ragdolling" and skips death-init — fallSideSign stays 0 — body freezes.
///
/// WHY LinkedEntityGroup INSTEAD OF config.visualRoot / joints[i].joint:
///   DynamicBuffer&lt;LinkedEntityGroup&gt; is the canonical Unity DOTS instantiation buffer.
///   Its entity references are ALWAYS correctly remapped by ECB.Instantiate because
///   Unity uses it internally to create all child instances. The entity references
///   stored in Ragdoll2DConfig.visualRoot and the BodyPart buffer are secondary
///   baked refs that depend on the remapping pipeline being fully correct — unreliable
///   if the prefab was not rebaked after authoring changes.
///   Scanning LinkedEntityGroup guarantees we reach the live child instances.
/// </summary>
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
public partial struct Ragdoll2DSpawnInitSystem : ISystem
{
    private ComponentLookup<Ragdoll2D>       _ragdollLookup;
    private ComponentLookup<Ragdoll2DJoint>  _jointLookup;
    private ComponentLookup<Ragdoll2DLaunch> _launchLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _ragdollLookup = state.GetComponentLookup<Ragdoll2D>(false);
        _jointLookup   = state.GetComponentLookup<Ragdoll2DJoint>(false);
        _launchLookup  = state.GetComponentLookup<Ragdoll2DLaunch>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        _ragdollLookup.Update(ref state);
        _jointLookup.Update(ref state);
        _launchLookup.Update(ref state);

        // WithAll<Ragdoll2DConfig> restricts to body entities that have a ragdoll setup.
        // We don't read Ragdoll2DConfig here — it's a filter only.
        foreach (var (linkedGroup, entity) in
            SystemAPI.Query<DynamicBuffer<LinkedEntityGroup>>()
                .WithAll<NewlySpawned, Ragdoll2DConfig>()
                .WithEntityAccess())
        {
            for (int i = 0; i < linkedGroup.Length; i++)
            {
                Entity child = linkedGroup[i].Value;
                if (child == entity) continue; // skip root

                if (_ragdollLookup.HasComponent(child))
                    _ragdollLookup.SetComponentEnabled(child, false);

                if (_jointLookup.HasComponent(child))
                    _jointLookup.SetComponentEnabled(child, false);
            }

            // Root launch: a pool-reclaimed corpse keeps airborne/sleeping flags from its previous
            // death — reset so Ragdoll2DInitSystem re-seeds cleanly on the next kill.
            if (_launchLookup.HasComponent(entity))
            {
                _launchLookup[entity] = default;
                _launchLookup.SetComponentEnabled(entity, false);
            }
        }
    }
}
