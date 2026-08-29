using DotsMovementToolkit;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsMovementToolkit.Authoring
{
    public class PathfindingAuthoring : MonoBehaviour
    {
        [Header("Pathfinding Settings")]
        [Tooltip("Preferred algorithm when not in a horde")]
        public PathfindingMode preferredMode = PathfindingMode.DStarLite;

        [Header("D* Lite Settings (for individual pathfinding)")]
        public float repathInterval = 0.5f;

        [Header("Horde Settings")]
        [Tooltip("If this many entities share the same target, consider forming/joining a horde")]
        public int hordeFormationThreshold = 10;

        public class Baker : Baker<PathfindingAuthoring>
        {
            public override void Bake(PathfindingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new PathfindingAgent
                {
                    preferredMode = authoring.preferredMode,
                    currentMode = authoring.preferredMode,
                    repathInterval = authoring.repathInterval,
                    timeSinceLastRepath = 0f,
                    hordeFormationThreshold = authoring.hordeFormationThreshold
                });

                // Add D* Lite component (disabled by default)
                AddComponent(entity, new DStarLiteFollower());
                SetComponentEnabled<DStarLiteFollower>(entity, false);

                // Add Flow Field component (disabled by default)
                AddComponent(entity, new FlowFieldFollower());
                SetComponentEnabled<FlowFieldFollower>(entity, false);

                // Add horde membership (disabled by default - not in a horde)
                AddComponent(entity, new HordeMembership
                {
                    hordeId = -1,
                    hordeEntity = Entity.Null,
                    formationOffset = float2.zero,
                    priority = int.MaxValue
                });
                SetComponentEnabled<HordeMembership>(entity, false);

                // Add path request component
                AddComponent(entity, new PathRequest());
                SetComponentEnabled<PathRequest>(entity, false);

                AddComponent(entity, new StuckDetector());

                AddComponent(entity, new MovementStuck());
                SetComponentEnabled<MovementStuck>(entity, false);
            }
        }
    }
}
