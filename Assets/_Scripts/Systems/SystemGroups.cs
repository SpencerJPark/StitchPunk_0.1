using Unity.Entities;

// main groups
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AISystemGroup))]
public partial class GameManagerSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MovementSystemGroup))]
public partial class AISystemGroup : ComponentSystemGroup { }

        // SubGroups
        [UpdateInGroup(typeof(AISystemGroup))]
        [UpdateBefore(typeof(AIScoringSystemGroup))]
        public partial class AIAwarenessSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AISystemGroup))]
        [UpdateAfter(typeof(AIAwarenessSystemGroup))]
        [UpdateBefore(typeof(AISelectionSystemGroup))]
        public partial class AIScoringSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AISystemGroup))]
        [UpdateAfter(typeof(AIScoringSystemGroup))]
        [UpdateBefore(typeof(AIExecutionSystemGroup))]
        public partial class AISelectionSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AISystemGroup))]
        [UpdateAfter(typeof(AISelectionSystemGroup))]
        public partial class AIExecutionSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AnimationSystemGroup))]
public partial class MovementSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(MovementSystemGroup))]
        [UpdateBefore(typeof(MovementRoutingSystemGroup))]
        public partial class MovementCoordinatorSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(MovementSystemGroup))]
        [UpdateBefore(typeof(MovementFollowerSystemGroup))]
        public partial class MovementRoutingSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(MovementSystemGroup))]
        [UpdateBefore(typeof(MovementExecutionSystemGroup))]
        public partial class MovementFollowerSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(MovementSystemGroup))]
        public partial class MovementExecutionSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimationSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AnimationSystemGroup), OrderFirst = true)]
        public partial class AnimationAssignmentSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true)]
        public partial class AnimationExecutionSystemGroup : ComponentSystemGroup { }

// Late Simulation System Group
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(DespawnSystemGroup))]
public partial class SpawnSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class DespawnSystemGroup : ComponentSystemGroup { }