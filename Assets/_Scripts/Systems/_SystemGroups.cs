using Unity.Entities;

// main groups
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AISystemGroup))]
public partial class GameManagerSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MovementSystemGroup))]
public partial class AISystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AnimationSystemGroup))]
public partial class MovementSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AnimationSystemGroup : ComponentSystemGroup { }


// AI sub groups
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