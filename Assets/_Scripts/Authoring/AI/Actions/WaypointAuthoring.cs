using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class WaypointAuthoring : MonoBehaviour
{
    [Header("Settings")]
    public float interactionRange = 1.5f;
    public float broadcastRadius = 20f;
    public int maxOccupants = 1;
    public Transform approachPoint;

    [Header("Actions this waypoint offers")]
    public List<WaypointActionDef> actions;

    [System.Serializable]
    public struct WaypointActionDef
    {
        public ActionType actionType;
        public AnimationType animation;
        public float duration;

        [Header("Need Modifiers (positive = improves need)")]
        public float hunger;
        public float energy;
        public float entertainment;
        public float social;
        public float comfort;
        public float bladder;
        public float safety;
        public float movement;
    }

    public class Baker : Baker<WaypointAuthoring>
    {
        public override void Bake(WaypointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            Entity approachEntity = authoring.approachPoint != null
                ? GetEntity(authoring.approachPoint, TransformUsageFlags.Dynamic)
                : Entity.Null;

            AddComponent(entity, new Waypoint
            {
                interactionRange = authoring.interactionRange,
                broadcastRadius = authoring.broadcastRadius,
                maxOccupants = authoring.maxOccupants,
                approachPoint = approachEntity,
                currentOccupants = 0
            });

            DynamicBuffer<WaypointAction> actionBuffer = AddBuffer<WaypointAction>(entity);
            foreach (var action in authoring.actions)
            {
                actionBuffer.Add(new WaypointAction
                {
                    actionType = action.actionType,
                    animation = action.animation,
                    duration = action.duration,
                    needModifiers = new NeedModifiers
                    {
                        hunger = action.hunger,
                        energy = action.energy,
                        entertainment = action.entertainment,
                        social = action.social,
                        comfort = action.comfort,
                        bladder = action.bladder,
                        safety = action.safety,
                        movement = action.movement
                    }
                });
            }

            AddBuffer<WaypointOccupant>(entity);
        }
    }
}

public struct Waypoint : IComponentData
{
    public float interactionRange;
    public float broadcastRadius;
    public int maxOccupants;
    public int currentOccupants;
    public Entity approachPoint;
}

public struct WaypointAction : IBufferElementData
{
    public ActionType actionType;
    public AnimationType animation;
    public float duration;
    public NeedModifiers needModifiers;
}

public struct WaypointOccupant : IBufferElementData
{
    public Entity brain;
}