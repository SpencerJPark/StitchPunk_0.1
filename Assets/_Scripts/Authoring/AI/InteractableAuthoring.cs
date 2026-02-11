using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class InteractableAuthoring : MonoBehaviour
{
    [Header("Basic Settings")]
    public InteractableType type;
    public Transform approachPoint;
    public float interactionRange = 1.5f;

    [Header("Broadcast Settings")]
    public float broadcastRadius = 15f;
    public int maxOccupants = 1;
    public int maxOffers = 3;

    [Header("Actions")]
    public List<ActionDefinition> providedActions;

    [System.Serializable]
    public struct ActionDefinition
    {
        public ActionType actionType;
        public AnimationType animation;
        public float duration;

        [Header("Need Modifiers (per second)")]
        public float hungerModifier;
        public float energyModifier;
        public float entertainmentModifier;
        public float socialModifier;
        public float comfortModifier;
        public float bladderModifier;
        public float safetyModifier;
    }

    public class Baker : Baker<InteractableAuthoring>
    {
        public override void Bake(InteractableAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            Entity approachEntity = authoring.approachPoint != null
                ? GetEntity(authoring.approachPoint, TransformUsageFlags.Dynamic)
                : Entity.Null;

            AddComponent(entity, new Interactable
            {
                type = authoring.type,
                approachPoint = approachEntity,
                interactionRange = authoring.interactionRange,
                broadcastRadius = authoring.broadcastRadius,
                maxOccupants = authoring.maxOccupants,
                maxOffers = authoring.maxOffers,
                currentOccupants = 0
            });

            DynamicBuffer<InteractableAction> actions = AddBuffer<InteractableAction>(entity);
            foreach (ActionDefinition action in authoring.providedActions)
            {
                actions.Add(new InteractableAction
                {
                    actionType = action.actionType,
                    animation = action.animation,
                    duration = action.duration,
                    needModifiers = new NeedModifiers
                    {
                        hunger = action.hungerModifier,
                        energy = action.energyModifier,
                        entertainment = action.entertainmentModifier,
                        social = action.socialModifier,
                        comfort = action.comfortModifier,
                        bladder = action.bladderModifier,
                        safety = action.safetyModifier
                    }
                });
            }

            AddBuffer<OccupantEntity>(entity);
            AddBuffer<InteractableOffer>(entity);
        }
    }
}

public struct Interactable : IComponentData
{
    public InteractableType type;
    public Entity approachPoint;
    public float interactionRange;
    public float broadcastRadius;
    public int maxOccupants;
    public int maxOffers;
    public int currentOccupants;
}

public struct InteractableAction : IBufferElementData
{
    public ActionType actionType;
    public AnimationType animation;
    public float duration;
    public NeedModifiers needModifiers;
}

public struct NeedModifiers
{
    // Positive values INCREASE the need (improve it toward 1)
    // Negative values DECREASE the need (worsen it toward 0)
    public float hunger;
    public float energy;
    public float entertainment;
    public float social;
    public float comfort;
    public float bladder;
    public float safety;
    public float movement;
}

public struct OccupantEntity : IBufferElementData
{
    public Entity entity;
}

public struct InteractableOffer : IBufferElementData
{
    public Entity brain;
    public float distance;
}

public enum InteractableType
{
    None,
    Food,
    Bed,
    Workstation,
    Bar,
    SmokingSpot,
    Bathroom,
    Seat,
    Entertainment
}