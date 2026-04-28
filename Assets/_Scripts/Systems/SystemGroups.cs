using Unity.Entities;

// main groups
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(PlayerInputSystemGroup))]
public partial class GameManagerSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AISystemGroup))]
public partial class PlayerSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup), OrderFirst = true)]
        public partial class PlayerInputSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup))]
        [UpdateAfter(typeof(PlayerInputSystemGroup))]
        [UpdateBefore(typeof(DialogueSystemGroup))]
        public partial class NarrativeSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup))]
        [UpdateAfter(typeof(NarrativeSystemGroup))]
        [UpdateBefore(typeof(PlayerEquipmentSystemGroup))]
        public partial class DialogueSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup), OrderLast = true)]
        public partial class PlayerEquipmentSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ItemSystemGroup))]
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
        [UpdateBefore(typeof(AIActionSystemGroup))]
        public partial class AISelectionSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AISystemGroup))]
        [UpdateAfter(typeof(AISelectionSystemGroup))]
        public partial class AIActionSystemGroup : ComponentSystemGroup { }


[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MovementSystemGroup))]
public partial class ItemSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(ItemSystemGroup), OrderFirst = true)]
        public partial class ItemEquiptSystemGroup : ComponentSystemGroup { }


[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(BuildingsSystemGroup))]
public partial class MovementSystemGroup : ComponentSystemGroup { }


[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(CombatSystemGroup))]
public partial class BuildingsSystemGroup : ComponentSystemGroup { }

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
[UpdateBefore(typeof(HealthSystemGroup))]
public partial class CombatSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(CombatSystemGroup))]
        [UpdateBefore(typeof(CombatReactionSystemGroup))]
        public partial class CombatResolutionSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(CombatSystemGroup))]
        [UpdateAfter(typeof(CombatResolutionSystemGroup))]
        public partial class CombatReactionSystemGroup : ComponentSystemGroup { }


[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AnimationSystemGroup))]
public partial class HealthSystemGroup : ComponentSystemGroup { }


[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimationSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AnimationSystemGroup), OrderFirst = true)]
        public partial class AnimationAssignmentSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true)]
        public partial class AnimationExecutionSystemGroup : ComponentSystemGroup { }



// Late Simulation System Group
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(SpawnInitSystemGroup))]
public partial class SpawnSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(DespawnSystemGroup))]
public partial class SpawnInitSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class DespawnSystemGroup : ComponentSystemGroup { }


// Runs last in LateSimulationSystemGroup — all spawns, despawns, and game logic are settled.
[UpdateInGroup(typeof(LateSimulationSystemGroup), OrderLast = true)]
public partial class SaveSystemGroup : ComponentSystemGroup { }