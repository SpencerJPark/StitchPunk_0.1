using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Centralizes all enableable action component lookups in one place.
/// AIExecutionSystem owns an instance of this and calls SetActionEnabled / ClearAllActiveActions.
///
/// WHEN ADDING A NEW ACTION TYPE:
/// 1. Add the ActiveXxx struct in CapabilityAndTraitTags.cs
/// 2. Add a ComponentLookup field below
/// 3. Initialize it in OnCreate
/// 4. Update it in UpdateLookups
/// 5. Add a case in SetActionEnabled
/// 6. Add a disable call in ClearAllActiveActions
/// 7. Add to brain authoring Bakers (disabled by default)
///
/// That's it. No other systems need to change.
/// </summary>
public struct ActionEnableHelper
{
    // --- Behavior lookups ---
    public ComponentLookup<ActiveWanderArea> activeWanderAreaLookup;
    public ComponentLookup<ActiveAnimateInPlace> activeAnimateInPlaceLookup;
    public ComponentLookup<ActiveIdleInPlace> activeIdleInPlaceLookup;

    // --- Action-specific lookups ---
    public ComponentLookup<ActiveEat> activeEatLookup;
    public ComponentLookup<ActiveSleep> activeSleepLookup;
    public ComponentLookup<ActiveWork> activeWorkLookup;
    public ComponentLookup<ActiveSocialize> activeSocializeLookup;
    public ComponentLookup<ActiveSit> activeSitLookup;
    public ComponentLookup<ActiveDrink> activeDrinkLookup;
    public ComponentLookup<ActiveSmoke> activeSmokeLookup;
    public ComponentLookup<ActiveUseBathroom> activeUseBathroomLookup;
    public ComponentLookup<ActivePatrol> activePatrolLookup;
    public ComponentLookup<ActiveRoam> activeRoamLookup;
    public ComponentLookup<ActiveAttack> activeAttackLookup;
    public ComponentLookup<ActiveIdle> activeIdleLookup;
    public ComponentLookup<ActiveWander> activeWanderLookup;

    public static ActionEnableHelper Create(ref SystemState state)
    {
        ActionEnableHelper helper = new ActionEnableHelper
        {
            activeWanderAreaLookup = state.GetComponentLookup<ActiveWanderArea>(false),
            activeAnimateInPlaceLookup = state.GetComponentLookup<ActiveAnimateInPlace>(false),
            activeIdleInPlaceLookup = state.GetComponentLookup<ActiveIdleInPlace>(false),

            activeEatLookup = state.GetComponentLookup<ActiveEat>(false),
            activeSleepLookup = state.GetComponentLookup<ActiveSleep>(false),
            activeWorkLookup = state.GetComponentLookup<ActiveWork>(false),
            activeSocializeLookup = state.GetComponentLookup<ActiveSocialize>(false),
            activeSitLookup = state.GetComponentLookup<ActiveSit>(false),
            activeDrinkLookup = state.GetComponentLookup<ActiveDrink>(false),
            activeSmokeLookup = state.GetComponentLookup<ActiveSmoke>(false),
            activeUseBathroomLookup = state.GetComponentLookup<ActiveUseBathroom>(false),
            activePatrolLookup = state.GetComponentLookup<ActivePatrol>(false),
            activeRoamLookup = state.GetComponentLookup<ActiveRoam>(false),
            activeAttackLookup = state.GetComponentLookup<ActiveAttack>(false),
            activeIdleLookup = state.GetComponentLookup<ActiveIdle>(false),
            activeWanderLookup = state.GetComponentLookup<ActiveWander>(false),
        };
        return helper;
    }

    public void UpdateLookups(ref SystemState state)
    {
        activeWanderAreaLookup.Update(ref state);
        activeAnimateInPlaceLookup.Update(ref state);
        activeIdleInPlaceLookup.Update(ref state);

        activeEatLookup.Update(ref state);
        activeSleepLookup.Update(ref state);
        activeWorkLookup.Update(ref state);
        activeSocializeLookup.Update(ref state);
        activeSitLookup.Update(ref state);
        activeDrinkLookup.Update(ref state);
        activeSmokeLookup.Update(ref state);
        activeUseBathroomLookup.Update(ref state);
        activePatrolLookup.Update(ref state);
        activeRoamLookup.Update(ref state);
        activeAttackLookup.Update(ref state);
        activeIdleLookup.Update(ref state);
        activeWanderLookup.Update(ref state);
    }

    /// <summary>
    /// Enables the behavior-level component matching the WaypointActionBehavior.
    /// Call this when the NPC arrives at the waypoint and starts performing.
    /// </summary>
    public void SetBehaviorEnabled(Entity entity, InteractionActionBehavior behavior, bool enabled)
    {
        switch (behavior)
        {
            case InteractionActionBehavior.AnimateInPlace:
                SetIfExists(ref activeAnimateInPlaceLookup, entity, enabled);
                break;
            case InteractionActionBehavior.WanderArea:
                SetIfExists(ref activeWanderAreaLookup, entity, enabled);
                break;
            case InteractionActionBehavior.IdleInPlace:
                SetIfExists(ref activeIdleInPlaceLookup, entity, enabled);
                break;
        }
    }

    /// <summary>
    /// Enables the action-specific component matching the ActionType.
    /// Call this when the NPC arrives at the waypoint and starts performing.
    /// </summary>
    public void SetActionEnabled(Entity entity, ActionType actionType, bool enabled)
    {
        switch (actionType)
        {
            case ActionType.Eat:
                SetIfExists(ref activeEatLookup, entity, enabled);
                break;
            case ActionType.Sleep:
                SetIfExists(ref activeSleepLookup, entity, enabled);
                break;
            case ActionType.Work:
                SetIfExists(ref activeWorkLookup, entity, enabled);
                break;
            case ActionType.Socialize:
                SetIfExists(ref activeSocializeLookup, entity, enabled);
                break;
            case ActionType.Sit:
                SetIfExists(ref activeSitLookup, entity, enabled);
                break;
            case ActionType.Drink:
                SetIfExists(ref activeDrinkLookup, entity, enabled);
                break;
            case ActionType.Smoke:
                SetIfExists(ref activeSmokeLookup, entity, enabled);
                break;
            case ActionType.UseBathroom:
                SetIfExists(ref activeUseBathroomLookup, entity, enabled);
                break;
            case ActionType.Patrol:
                SetIfExists(ref activePatrolLookup, entity, enabled);
                break;
            case ActionType.Roam:
                SetIfExists(ref activeRoamLookup, entity, enabled);
                break;
            case ActionType.Attack:
                SetIfExists(ref activeAttackLookup, entity, enabled);
                break;
            case ActionType.Idle:
                SetIfExists(ref activeIdleLookup, entity, enabled);
                break;
            case ActionType.Wander:
                SetIfExists(ref activeWanderLookup, entity, enabled);
                break;
        }
    }

    /// <summary>
    /// Disables ALL active action and behavior components on this entity.
    /// Call this when the NPC finishes an action before starting the next one.
    /// Safe to call even if the entity doesn't have a particular component.
    /// </summary>
    public void ClearAllActiveActions(Entity entity)
    {
        // Behaviors
        SetIfExists(ref activeWanderAreaLookup, entity, false);
        SetIfExists(ref activeAnimateInPlaceLookup, entity, false);
        SetIfExists(ref activeIdleInPlaceLookup, entity, false);

        // Actions
        SetIfExists(ref activeEatLookup, entity, false);
        SetIfExists(ref activeSleepLookup, entity, false);
        SetIfExists(ref activeWorkLookup, entity, false);
        SetIfExists(ref activeSocializeLookup, entity, false);
        SetIfExists(ref activeSitLookup, entity, false);
        SetIfExists(ref activeDrinkLookup, entity, false);
        SetIfExists(ref activeSmokeLookup, entity, false);
        SetIfExists(ref activeUseBathroomLookup, entity, false);
        SetIfExists(ref activePatrolLookup, entity, false);
        SetIfExists(ref activeRoamLookup, entity, false);
        SetIfExists(ref activeAttackLookup, entity, false);
        SetIfExists(ref activeIdleLookup, entity, false);
        SetIfExists(ref activeWanderLookup, entity, false);
    }

    private static void SetIfExists<T>(ref ComponentLookup<T> lookup, Entity entity, bool enabled)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        if (lookup.HasComponent(entity))
        {
            lookup.SetComponentEnabled(entity, enabled);
        }
    }
}