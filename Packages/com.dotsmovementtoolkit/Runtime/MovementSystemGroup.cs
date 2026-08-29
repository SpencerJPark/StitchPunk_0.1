using Unity.Entities;

namespace DotsMovementToolkit
{
    // The movement toolkit's own group manifest — the package-internal counterpart to the
    // game's Systems/SystemGroups.cs. Gating: OnCreate requires the baked NavGridSettings
    // singleton (added Phase 2) instead of a game-specific scene tag, so a consumer project
    // with no grid config baked simply never runs this group.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MovementSystemGroup : ComponentSystemGroup
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NavGridSettings>();
        }
    }

    [UpdateInGroup(typeof(MovementSystemGroup))]
    [UpdateBefore(typeof(MovementRoutingSystemGroup))]
    public partial class MovementCoordinatorSystemGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(MovementSystemGroup))]
    [UpdateBefore(typeof(MovementFollowerSystemGroup))]
    public partial class MovementRoutingSystemGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(MovementSystemGroup))]
    [UpdateBefore(typeof(MovementSteeringSystemGroup))]
    public partial class MovementFollowerSystemGroup : ComponentSystemGroup { }

    // Declared slot for future "natural movement" work (arrival easing, avoidance) — empty for
    // now. The unused PathfindingUtils.GetFlowDirectionSmooth bilinear sampler is the first
    // candidate to land here; see the package README's Known Issues.
    [UpdateInGroup(typeof(MovementSystemGroup))]
    [UpdateBefore(typeof(MovementExecutionSystemGroup))]
    public partial class MovementSteeringSystemGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(MovementSystemGroup))]
    public partial class MovementExecutionSystemGroup : ComponentSystemGroup { }
}
