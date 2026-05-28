using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Aware of loose items nearby and what they offer (weapon, heal, food/drink).
// Appends ActionOption entries; never touches action tags or requests.
//   - Threatened + unarmed -> seek nearest weapon (SelfDefence, priority 2).
//   - Hurt (health < 100%)  -> seek nearest healing item, scaled by damage (SelfPreservation, priority 0).
//   - Nothing urgent        -> consume nearest food/drink, gated by need via the scoring curve (priority 0).
// Loose items are those with EquiptBy.owner == Entity.Null.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct ItemAwarenessSystem : ISystem
{
    private EntityQuery               _looseItemQuery;
    private ComponentLookup<UnitEquipt> _unitEquiptLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();

        _looseItemQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<Item>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<EquiptBy>());

        _unitEquiptLookup = state.GetComponentLookup<UnitEquipt>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int itemCount = _looseItemQuery.CalculateEntityCount();
        if (itemCount == 0)
            return;

        _unitEquiptLookup.Update(ref state);

        BlobAssetReference<ItemLibraryBlob> itemLibrary =
            SystemAPI.GetSingleton<ItemLibrary>().library;

        NativeArray<Entity>         itemEntities   = _looseItemQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<Item>           itemData       = _looseItemQuery.ToComponentDataArray<Item>(Allocator.TempJob);
        NativeArray<LocalTransform> itemTransforms = _looseItemQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        NativeArray<EquiptBy>       itemOwners     = _looseItemQuery.ToComponentDataArray<EquiptBy>(Allocator.TempJob);

        state.Dependency = new ItemAwarenessJob
        {
            itemEntities     = itemEntities,
            itemData         = itemData,
            itemTransforms   = itemTransforms,
            itemOwners       = itemOwners,
            itemLibrary      = itemLibrary,
            unitEquiptLookup = _unitEquiptLookup,
        }.ScheduleParallel(state.Dependency);

        itemEntities.Dispose(state.Dependency);
        itemData.Dispose(state.Dependency);
        itemTransforms.Dispose(state.Dependency);
        itemOwners.Dispose(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
public partial struct ItemAwarenessJob : IJobEntity
{
    [ReadOnly] public NativeArray<Entity>         itemEntities;
    [ReadOnly] public NativeArray<Item>           itemData;
    [ReadOnly] public NativeArray<LocalTransform> itemTransforms;
    [ReadOnly] public NativeArray<EquiptBy>       itemOwners;
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob> itemLibrary;
    [ReadOnly] public ComponentLookup<UnitEquipt> unitEquiptLookup;

    public void Execute(
        Entity                          self,
        in LocalTransform               transform,
        in Awareness                    awareness,
        in Health                       health,
        in CurrentAction                currentAction,
        in DynamicBuffer<ThreatEntry>   threats,
        ref DynamicBuffer<Motivation>   motivations,
        ref DynamicBuffer<ActionOption> options)
    {
        bool  hasThreat   = threats.Length > 0;
        bool  inCombat    = currentAction.actionType.IsCombatAction();
        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;

        bool canEquip  = unitEquiptLookup.TryGetComponent(self, out UnitEquipt unitEquipt);
        bool hasWeapon = canEquip && unitEquipt.equiptItemEntity != Entity.Null;

        bool wantWeapon  = hasThreat && canEquip && !hasWeapon;
        bool wantHealing = healthRatio < 1f;
        bool wantConsume = !hasThreat && !inCombat;

        if (!wantWeapon && !wantHealing && !wantConsume)
            return;

        float3 myPos   = transform.Position;
        float  rangeSq = awareness.range * awareness.range;

        Entity nearestWeapon = Entity.Null; float nearestWeaponSq = float.MaxValue;
        Entity nearestHeal   = Entity.Null; float nearestHealSq   = float.MaxValue;
        Entity nearestFood   = Entity.Null; float nearestFoodSq   = float.MaxValue;
        Entity nearestDrink  = Entity.Null; float nearestDrinkSq  = float.MaxValue;
        MotivationType foodMotivation  = MotivationType.Hunger; float foodDelta  = 0f;
        MotivationType drinkMotivation = MotivationType.Hunger; float drinkDelta = 0f;

        for (int i = 0; i < itemEntities.Length; i++)
        {
            if (itemOwners[i].owner != Entity.Null)
                continue; // not loose

            int typeIndex = (int)itemData[i].itemType;
            if (typeIndex < 0 || typeIndex >= itemLibrary.Value.items.Length)
                continue;

            ref ItemBlob blob = ref itemLibrary.Value.items[typeIndex];
            if (blob.category == ItemCategory.None)
                continue;

            float distSq = math.distancesq(myPos, itemTransforms[i].Position);
            if (distSq > rangeSq)
                continue;

            switch (blob.category)
            {
                case ItemCategory.Weapon:
                    if (wantWeapon && distSq < nearestWeaponSq)
                    {
                        nearestWeaponSq = distSq;
                        nearestWeapon   = itemEntities[i];
                    }
                    break;
                case ItemCategory.Healing:
                    if (wantHealing && distSq < nearestHealSq)
                    {
                        nearestHealSq = distSq;
                        nearestHeal   = itemEntities[i];
                    }
                    break;
                case ItemCategory.Food:
                    if (wantConsume && distSq < nearestFoodSq)
                    {
                        nearestFoodSq  = distSq;
                        nearestFood    = itemEntities[i];
                        foodMotivation = blob.satisfiedMotivation != MotivationType.None
                            ? blob.satisfiedMotivation : MotivationType.Hunger;
                        foodDelta      = blob.restorationAmount;
                    }
                    break;
                case ItemCategory.Drink:
                    if (wantConsume && distSq < nearestDrinkSq)
                    {
                        nearestDrinkSq  = distSq;
                        nearestDrink    = itemEntities[i];
                        drinkMotivation = blob.satisfiedMotivation != MotivationType.None
                            ? blob.satisfiedMotivation : MotivationType.Hunger;
                        drinkDelta      = blob.restorationAmount;
                    }
                    break;
            }
        }

        // Weapon — urgent: arm up to defend (competes with combat/flee at priority 2).
        if (nearestWeapon != Entity.Null)
        {
            AIUtils.SetMotivationValue(ref motivations, MotivationType.SelfDefence, 100f);
            options.Add(new ActionOption
            {
                actionType     = ActionType.EquipWeapon,
                motivationType = MotivationType.SelfDefence,
                priority       = 2,
                utilityScore   = 1f - math.saturate(nearestWeaponSq / rangeSq),
                needsValidation = false,
                targetEntity   = nearestWeapon,
            });
        }

        // Healing — ambient recovery scaled by damage taken (priority 0, scored by SelfPreservation curve).
        if (nearestHeal != Entity.Null)
        {
            float urgency = (1f - healthRatio) * 100f;
            SetMotivationAtLeast(ref motivations, MotivationType.SelfPreservation, urgency);
            options.Add(new ActionOption
            {
                actionType     = ActionType.UseHealingItem,
                motivationType = MotivationType.SelfPreservation,
                priority       = 0,
                utilityScore   = (1f - healthRatio) * (1f - math.saturate(nearestHealSq / rangeSq)),
                needsValidation = false,
                targetEntity   = nearestHeal,
            });
        }

        // Food — ambient need; scoring curve + advertisedDelta gate on actual hunger (priority 0).
        if (nearestFood != Entity.Null)
        {
            options.Add(new ActionOption
            {
                actionType      = ActionType.Eat,
                motivationType  = foodMotivation,
                priority        = 0,
                utilityScore    = 1f - math.saturate(nearestFoodSq / rangeSq),
                advertisedDelta = foodDelta,
                needsValidation = false,
                targetEntity    = nearestFood,
            });
        }

        if (nearestDrink != Entity.Null)
        {
            options.Add(new ActionOption
            {
                actionType      = ActionType.Drink,
                motivationType  = drinkMotivation,
                priority        = 0,
                utilityScore    = 1f - math.saturate(nearestDrinkSq / rangeSq),
                advertisedDelta = drinkDelta,
                needsValidation = false,
                targetEntity    = nearestDrink,
            });
        }
    }

    // Raises a motivation toward value without lowering it (avoids stomping FleeAwareness' SelfPreservation = 100).
    private static void SetMotivationAtLeast(ref DynamicBuffer<Motivation> motivations, MotivationType type, float value)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            Motivation motivation = motivations[i];
            if (motivation.motivationType != type)
                continue;
            if (value > motivation.value)
            {
                motivation.value = value;
                motivations[i]   = motivation;
            }
            return;
        }
    }
}
