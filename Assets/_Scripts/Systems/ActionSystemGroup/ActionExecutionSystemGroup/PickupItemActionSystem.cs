using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Executes item pickup/consume for all item-awareness ActionTypes (EquipWeapon, UseHealingItem, Eat, Drink).
// All route to PickupItemAction; the outcome is chosen from the item's ItemCategory:
//   - Weapon     -> equip (delegates to ItemEquipSystem).
//   - Consumable -> resolve consumeEffect via EffectLibrary, then HealRequest (Healing) or MotivationChangeRequest (behaviours).
//
// States (ActionTimer enabled flag):
//   Pathing  (PickupItemAction enabled, ActionTimer disabled) — path toward the item
//   Acting   (PickupItemAction enabled, ActionTimer enabled)  — animate + count down
//   Complete — apply effect, disable PickupItemAction, re-enable ActionRequest
//
// Runs single-threaded (.Schedule) because it writes to arbitrary item entities via ComponentLookup,
// mirroring ItemEquipSystem / PlayerPickupSystem.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct PickupItemActionSystem : ISystem
{
    private ComponentLookup<LocalTransform>     transformLookup;
    private ComponentLookup<Item>               itemLookup;
    private ComponentLookup<EquiptBy>           equiptByLookup;
    private ComponentLookup<AttachedTo>         attachedToLookup;
    private ComponentLookup<EquipAction>        equipActionLookup;
    private ComponentLookup<AttachItemRequest>  attachRequestLookup;
    private ComponentLookup<PlayerInteractable> playerInteractableLookup;
    private ComponentLookup<UnitEquipt>         unitEquiptLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<EffectLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();

        transformLookup          = state.GetComponentLookup<LocalTransform>(true);
        itemLookup               = state.GetComponentLookup<Item>(true);
        equiptByLookup           = state.GetComponentLookup<EquiptBy>(false);
        attachedToLookup         = state.GetComponentLookup<AttachedTo>(false);
        equipActionLookup        = state.GetComponentLookup<EquipAction>(false);
        attachRequestLookup      = state.GetComponentLookup<AttachItemRequest>(false);
        playerInteractableLookup = state.GetComponentLookup<PlayerInteractable>(false);
        unitEquiptLookup         = state.GetComponentLookup<UnitEquipt>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        itemLookup.Update(ref state);
        equiptByLookup.Update(ref state);
        attachedToLookup.Update(ref state);
        equipActionLookup.Update(ref state);
        attachRequestLookup.Update(ref state);
        playerInteractableLookup.Update(ref state);
        unitEquiptLookup.Update(ref state);

        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        BlobAssetReference<ItemLibraryBlob> itemLibrary =
            SystemAPI.GetSingleton<ItemLibrary>().library;
        BlobAssetReference<EffectLibraryBlob> effectLibrary =
            SystemAPI.GetSingleton<EffectLibrary>().library;
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new PickupItemJob
        {
            deltaTime                = SystemAPI.Time.DeltaTime,
            ecb                      = ecb,
            transformLookup          = transformLookup,
            itemLookup               = itemLookup,
            equiptByLookup           = equiptByLookup,
            attachedToLookup         = attachedToLookup,
            equipActionLookup        = equipActionLookup,
            attachRequestLookup      = attachRequestLookup,
            playerInteractableLookup = playerInteractableLookup,
            unitEquiptLookup         = unitEquiptLookup,
            itemLibrary              = itemLibrary,
            effectLibrary            = effectLibrary,
            unitLibrary              = unitLibrary,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(PickupItemAction))]
[WithDisabled(typeof(ActionRequest), typeof(ActionInterruptRequest))]
[WithPresent(typeof(Dead), typeof(PathRequest), typeof(ActionTimer),
             typeof(AnimationRequest), typeof(HealRequest))]
public partial struct PickupItemJob : IJobEntity
{
    private const float REPATH_DIST_SQ = 1.0f;

    public float               deltaTime;
    public EntityCommandBuffer ecb;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Item>           itemLookup;
    public ComponentLookup<EquiptBy>           equiptByLookup;
    public ComponentLookup<AttachedTo>         attachedToLookup;
    public ComponentLookup<EquipAction>        equipActionLookup;
    public ComponentLookup<AttachItemRequest>  attachRequestLookup;
    public ComponentLookup<PlayerInteractable> playerInteractableLookup;
    [ReadOnly] public ComponentLookup<UnitEquipt> unitEquiptLookup;
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>   itemLibrary;
    [ReadOnly] public BlobAssetReference<EffectLibraryBlob> effectLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>   unitLibrary;

    public void Execute(
        Entity                                     self,
        in LocalTransform                          localTransform,
        in UnitData                                unitData,
        in CurrentAction                           action,
        ref PathRequest                            pathRequest,
        ref ActionTimer                            actionTimer,
        ref HealRequest                            healRequest,
        ref DynamicBuffer<SetAnimation>            setAnimations,
        ref DynamicBuffer<MotivationChangeRequest> changeRequests,
        EnabledRefRW<PickupItemAction>             pickupItemActionEnabled,
        EnabledRefRW<ActionRequest>                actionRequestEnabled,
        EnabledRefRW<ActionTimer>                  actionTimerEnabled,
        EnabledRefRW<PathRequest>                  pathRequestEnabled,
        EnabledRefRW<AnimationRequest>             animationRequestEnabled,
        EnabledRefRW<HealRequest>                  healRequestEnabled,
        EnabledRefRO<Dead>                         dead)
    {
        Entity item = action.targetEntity;

        if (dead.ValueRO)
        {
            Terminate(ref pathRequest, pickupItemActionEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled);
            return;
        }

        // --- Acting state: animate + count down, apply effect on completion ---
        if (actionTimerEnabled.ValueRO)
        {
            actionTimer.time -= deltaTime;
            if (actionTimer.time <= 0f)
            {
                ApplyEffect(self, item, ref healRequest, healRequestEnabled, ref changeRequests);
                Terminate(ref pathRequest, pickupItemActionEnabled, actionRequestEnabled,
                    actionTimerEnabled, pathRequestEnabled);
            }
            return;
        }

        // --- Pathing state: validate the item still exists and is loose ---
        if (!itemLookup.TryGetComponent(item, out Item itemComp)
            || !transformLookup.TryGetComponent(item, out LocalTransform itemTransform)
            || !IsLoose(item))
        {
            Terminate(ref pathRequest, pickupItemActionEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled);
            return;
        }

        int typeIndex = (int)itemComp.itemType;
        if (typeIndex < 0 || typeIndex >= itemLibrary.Value.items.Length)
        {
            Terminate(ref pathRequest, pickupItemActionEnabled, actionRequestEnabled,
                actionTimerEnabled, pathRequestEnabled);
            return;
        }

        ref ItemBlob blob = ref itemLibrary.Value.items[typeIndex];

        float distSq  = math.distancesq(localTransform.Position, itemTransform.Position);
        float rangeSq = blob.pickupRange * blob.pickupRange;

        if (distSq <= rangeSq)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            actionTimerEnabled.ValueRW = true;
            actionTimer.time           = blob.consumeDuration;

            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            AnimationType animType = unitIndex >= 0
                ? AIUtils.GetAnimationByAction(ref unitLibrary.Value.units[unitIndex], action.actionType)
                : AnimationType.None;

            if (animType != AnimationType.None)
            {
                setAnimations.Add(new SetAnimation
                {
                    layer     = AnimationLayerType.Action,
                    animation = animType,
                    speed     = 1f,
                });
                animationRequestEnabled.ValueRW = true;
            }
            return;
        }

        // --- Repath (also drives the initial path on first entry) ---
        if (math.distancesq(pathRequest.targetPosition, itemTransform.Position) >= REPATH_DIST_SQ)
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled,
                itemTransform.Position, blob.pickupRange * 0.9f);
    }

    private void ApplyEffect(
        Entity                                     self,
        Entity                                     item,
        ref HealRequest                            healRequest,
        EnabledRefRW<HealRequest>                  healRequestEnabled,
        ref DynamicBuffer<MotivationChangeRequest> changeRequests)
    {
        // Re-validate: the item may have been taken or destroyed during the timer.
        if (!itemLookup.TryGetComponent(item, out Item itemComp) || !IsLoose(item))
            return;

        int typeIndex = (int)itemComp.itemType;
        if (typeIndex < 0 || typeIndex >= itemLibrary.Value.items.Length)
            return;

        ItemBlob blob = itemLibrary.Value.items[typeIndex];

        switch (blob.category)
        {
            case ItemCategory.Weapon:
                EquipWeapon(self, item);
                break;

            case ItemCategory.Consumable:
                ClaimItem(item, self);
                ApplyConsumableEffect(blob.consumeEffect, ref healRequest, healRequestEnabled, ref changeRequests);
                ecb.DestroyEntity(item);
                break;
        }
    }

    private void ApplyConsumableEffect(
        EffectType                                 effectType,
        ref HealRequest                            healRequest,
        EnabledRefRW<HealRequest>                  healRequestEnabled,
        ref DynamicBuffer<MotivationChangeRequest> changeRequests)
    {
        int effectIndex = (int)effectType;
        if (effectIndex < 0 || effectIndex >= effectLibrary.Value.effects.Length)
            return;

        ref EffectBlob effectBlob = ref effectLibrary.Value.effects[effectIndex];

        if (effectBlob.effectType == EffectType.Healing)
        {
            healRequest.healAmount     = (int)effectBlob.value;
            healRequestEnabled.ValueRW = true;
            return;
        }

        if (effectBlob.behaviours.Length > 0)
        {
            for (int b = 0; b < effectBlob.behaviours.Length; b++)
            {
                MotivationType motivation = effectBlob.behaviours[b];
                if (motivation == MotivationType.None) continue;
                changeRequests.Add(new MotivationChangeRequest
                {
                    motivationType = motivation,
                    changeType     = MotivationChangeType.Add,
                    value          = effectBlob.value,
                });
            }
            return;
        }

        // TODO: status effect dispatch (Poisoned, OnFire, etc.) — awaiting UnitStateExpirySystem.
    }

    // Mirrors PlayerPickupJob: link the item to the unit and let ItemEquipSystem finalise the slot.
    private void EquipWeapon(Entity self, Entity item)
    {
        Entity socket = unitEquiptLookup.TryGetComponent(self, out UnitEquipt unitEquipt)
            ? unitEquipt.socketEntity
            : Entity.Null;

        if (equiptByLookup.HasComponent(item))
            equiptByLookup[item] = new EquiptBy { owner = self };
        if (attachedToLookup.HasComponent(item))
            attachedToLookup[item] = new AttachedTo { socket = socket };
        if (equipActionLookup.HasComponent(item))
            equipActionLookup.SetComponentEnabled(item, true);
        if (socket != Entity.Null && attachRequestLookup.HasComponent(item))
            attachRequestLookup.SetComponentEnabled(item, true);
        if (playerInteractableLookup.HasComponent(item))
            playerInteractableLookup.SetComponentEnabled(item, false);
    }

    private void ClaimItem(Entity item, Entity owner)
    {
        if (equiptByLookup.HasComponent(item))
            equiptByLookup[item] = new EquiptBy { owner = owner };
    }

    private bool IsLoose(Entity item)
    {
        return !equiptByLookup.TryGetComponent(item, out EquiptBy equiptBy)
            || equiptBy.owner == Entity.Null;
    }

    private void Terminate(
        ref PathRequest                pathRequest,
        EnabledRefRW<PickupItemAction> pickupItemActionEnabled,
        EnabledRefRW<ActionRequest>    actionRequestEnabled,
        EnabledRefRW<ActionTimer>      actionTimerEnabled,
        EnabledRefRW<PathRequest>      pathRequestEnabled)
    {
        AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
        actionTimerEnabled.ValueRW   = false;
        pickupItemActionEnabled.ValueRW = false;
        actionRequestEnabled.ValueRW = true;
    }
}
