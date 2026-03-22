using Unity.Entities;
using UnityEngine;
using System;

public class InteractionAuthoring : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("How close the NPC must be to start the action")]
    public float interactionRange = 1.5f;

    public int maxOccupant = 1;

    public float maxTime = 0.5f;

    [Tooltip("Interaction ActionEnum for when performed")]
    public ActionType actionType = ActionType.Interact;

    [Header("Motivation Types Provided")]
    [Tooltip("Which NPC motivations this interaction satisfies. Must have at least one for the AI scoring systems to find it.")]
    public bool providesMovement;
    public bool providesHunger;
    public bool providesEnergy;
    public bool providesFun;
    public bool providesSocial;
    public bool providesComfort;
    public bool providesSafety;
    public bool providesBladder;

    public class Baker : Baker<InteractionAuthoring>
    {
        public override void Bake(InteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Interaction
            {
                interactionRange = authoring.interactionRange,
                actionType = authoring.actionType,
                maxOccupants = authoring.maxOccupant
            });

            AddComponent(entity, new InteractionTimer
            {
                maxTime = authoring.maxTime,
            });
            SetComponentEnabled<InteractionTimer>(entity, false);

            AddComponent(entity, new InteractionProvider());
            SetComponentEnabled<InteractionProvider>(entity, true);

            AddBuffer<InteractionOccupant>(entity);

            AddComponent(entity, new InteractionHandled());
            SetComponentEnabled<InteractionHandled>(entity, false);

            // Motivation-type tags — SpatialHashSystem only registers this entity
            // in interactionCells for motivations it actually has a component for.
            if (authoring.providesMovement) AddComponent(entity, new MovementInteraction { value = 1 });
            if (authoring.providesHunger)   AddComponent(entity, new HungerInteraction   { value = 1 });
            if (authoring.providesEnergy)   AddComponent(entity, new EnergyInteraction   { value = 1 });
            if (authoring.providesFun)      AddComponent(entity, new FunInteraction      { value = 1 });
            if (authoring.providesSocial)   AddComponent(entity, new SocialInteraction   { value = 1 });
            if (authoring.providesComfort)  AddComponent(entity, new ComfortInteraction  { value = 1 });
            if (authoring.providesSafety)   AddComponent(entity, new SafetyInteraction   { value = 1 });
            if (authoring.providesBladder)  AddComponent(entity, new BladderInteraction  { value = 1 });
        }
    }
}

