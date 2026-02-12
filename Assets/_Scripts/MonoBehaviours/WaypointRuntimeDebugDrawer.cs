using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Runtime debug drawer that reads ECS waypoint data and draws gizmos in play mode.
/// Place this MonoBehaviour on a GameObject in your scene.
/// Works alongside WaypointDebugAuthoring (which draws in edit mode via authoring data).
/// 
/// Shows:
///   - Broadcast radius as blue circles on the ground
///   - Interaction range as green circles
///   - Lines from NPCs to their current waypoint target (magenta)
/// </summary>
public class WaypointRuntimeDebugDrawer : MonoBehaviour
{
    [Header("Settings")]
    public bool showBroadcastRadius = true;
    public bool showInteractionRange = true;
    public bool showNPCTargetLines = true;

    [Header("Colors")]
    public Color broadcastColor = new Color(0.3f, 0.5f, 1f, 0.4f);
    public Color interactionColor = new Color(0.3f, 1f, 0.3f, 0.6f);
    public Color npcTargetLineColor = new Color(1f, 0f, 1f, 0.5f);

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying)
            return;

        if (!InteractionDebugAuthoring.ShowDebug)
            return;

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Draw waypoint radii
        if (showBroadcastRadius || showInteractionRange)
        {
            EntityQuery waypointQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InteractionProvider>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            NativeArray<Entity> waypointEntities = waypointQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < waypointEntities.Length; i++)
            {
                Entity waypointEntity = waypointEntities[i];
                InteractionProvider interactionProvider = entityManager.GetComponentData<InteractionProvider>(waypointEntity);
                LocalTransform waypointTransform = entityManager.GetComponentData<LocalTransform>(waypointEntity);
                Vector3 position = waypointTransform.Position;

                if (showBroadcastRadius)
                {
                    Gizmos.color = broadcastColor;
                    Gizmos.DrawWireSphere(position, interactionProvider.broadcastRadius);
                }

                if (showInteractionRange)
                {
                    Gizmos.color = interactionColor;
                    Gizmos.DrawWireSphere(position, interactionProvider.interactionRange);
                }
            }

            waypointEntities.Dispose();
            waypointQuery.Dispose();
        }

        // Draw NPC → waypoint target lines
        if (showNPCTargetLines)
        {
            EntityQuery brainQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CurrentInteraction>(),
                ComponentType.ReadOnly<BrainLink>()
            );

            NativeArray<Entity> brainEntities = brainQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < brainEntities.Length; i++)
            {
                Entity brainEntity = brainEntities[i];
                CurrentInteraction interaction = entityManager.GetComponentData<CurrentInteraction>(brainEntity);

                if (interaction.target == Entity.Null)
                    continue;

                BrainLink brainLink = entityManager.GetComponentData<BrainLink>(brainEntity);

                if (!entityManager.HasComponent<LocalTransform>(brainLink.body))
                    continue;

                LocalTransform bodyTransform = entityManager.GetComponentData<LocalTransform>(brainLink.body);

                if (!entityManager.HasComponent<LocalTransform>(interaction.target))
                    continue;

                LocalTransform targetTransform = entityManager.GetComponentData<LocalTransform>(interaction.target);

                Gizmos.color = npcTargetLineColor;
                Gizmos.DrawLine(
                    (Vector3)bodyTransform.Position,
                    (Vector3)targetTransform.Position
                );

                // Draw small sphere at NPC to show interaction state
                if (interaction.isInRange)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere((Vector3)bodyTransform.Position + Vector3.up * 2f, 0.3f);
                }
                else
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere((Vector3)bodyTransform.Position + Vector3.up * 2f, 0.3f);
                }
            }

            brainEntities.Dispose();
            brainQuery.Dispose();
        }
    }
}