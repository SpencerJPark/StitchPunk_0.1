using DotsAnimationToolkit;
using Unity.Entities;

/// <summary>
/// Cleans up the ragdoll when a unit is revived. Runs after ReviveRequestSystem (which disables
/// Dead). Disabling RagdollActor is the whole job — the toolkit itself restores the pose captured
/// the moment it was enabled (ragdoll.md: "before" means before this drop, not the rig's rest pose,
/// so a character knocked over mid-swing and revived comes back to that swing).
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveRequestSystem))]
public partial struct RagdollReviveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Default enableable-component query filtering already restricts this to entities where
        // RagdollActor is currently enabled — no IgnoreComponentEnabledState needed.
        foreach (EnabledRefRW<RagdollActor> ragdollActorEnabled in
            SystemAPI.Query<EnabledRefRW<RagdollActor>>().WithDisabled<Dead>())
        {
            ragdollActorEnabled.ValueRW = false;
        }
    }
}
