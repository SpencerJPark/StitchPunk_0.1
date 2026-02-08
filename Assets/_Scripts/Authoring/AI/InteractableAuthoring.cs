using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class InteractableAuthoring : MonoBehaviour
{
    public InteractableType type;
    public Transform approachPoint;
    public float interactionRange = 1.5f;
    public List<ActionDefinition> providedActions;

    [System.Serializable]
    public struct ActionDefinition
    {
        public ActionType actionType;
        public AnimationType animation;
        public float duration;
        public float needModifier;
        public NeedType needAffected;
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
                interactionRange = authoring.interactionRange
            });

            var actions = AddBuffer<InteractableAction>(entity);
            foreach (var action in authoring.providedActions)
            {
                actions.Add(new InteractableAction
                {
                    actionType = action.actionType,
                    animation = action.animation,
                    duration = action.duration,
                    needModifier = action.needModifier,
                    needAffected = action.needAffected
                });
            }
        }
    }
}

public struct Interactable : IComponentData
{
    public InteractableType type;
    public Entity approachPoint;
    public float interactionRange;
}

public struct InteractableAction : IBufferElementData
{
    public ActionType actionType;
    public AnimationType animation;
    public float duration;
    public float needModifier;
    public NeedType needAffected;
}

public enum InteractableType
{
    Food,
    Bed,
    Workstation,
    Bar,
    SmokingSpot,
    Toilet,
    Entertainment,
    Bathroom
}

public enum NeedType
{
    Hunger,
    Energy,
    Comfort,
    Entertainment,
    Bladder,
    Social,
    Safety,
}