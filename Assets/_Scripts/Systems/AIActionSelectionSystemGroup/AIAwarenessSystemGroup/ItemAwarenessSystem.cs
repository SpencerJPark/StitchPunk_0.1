using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Aware of loose items nearby and what they offer (weapon vs consumable).
// Appends ActionOption entries; never touches action tags or requests.
//   - Threatened + unarmed -> seek nearest Weapon item (SelfDefence, priority 2).
//   - Hurt (health < 100%) -> seek nearest Consumable with EffectType.Healing (SelfPreservation, priority 0).
//   - Nothing urgent       -> seek nearest Consumable with EffectType.Feed/Hydrate, gated by need (priority 0).
// Consumable effects are resolved via EffectLibraryBlob — EffectType decides the slot, behaviours[0] the motivation.
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
        state.RequireForUpdate<EffectLibrary>();

        _looseItemQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Item, LocalTransform, EquiptBy>()
            .Build(ref state);

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
        BlobAssetReference<EffectLibraryBlob> effectLibrary =
            SystemAPI.GetSingleton<EffectLibrary>().library;

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
            effectLibrary    = effectLibrary,
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
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>   itemLibrary;
    [ReadOnly] public BlobAssetReference<EffectLibraryBlob> effectLibrary;
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

            if (blob.category == ItemCategory.Weapon)
            {
                if (wantWeapon && distSq < nearestWeaponSq)
                {
                    nearestWeaponSq = distSq;
                    nearestWeapon   = itemEntities[i];
                }
                continue;
            }

            // Consumable — resolve EffectType to decide which awareness slot fires.
            int effectIndex = (int)blob.consumeEffect;
            if (effectIndex < 0 || effectIndex >= effectLibrary.Value.effects.Length)
                continue;

            ref EffectBlob effectBlob = ref effectLibrary.Value.effects[effectIndex];

            switch (effectBlob.effectType)
            {
                case EffectType.Healing:
                    if (wantHealing && distSq < nearestHealSq)
                    {
                        nearestHealSq = distSq;
                        nearestHeal   = itemEntities[i];
                    }
                    break;
                case EffectType.Feed:
                    if (wantConsume && distSq < nearestFoodSq)
                    {
                        nearestFoodSq  = distSq;
                        nearestFood    = itemEntities[i];
                        foodMotivation = effectBlob.behaviours.Length > 0 && effectBlob.behaviours[0] != MotivationType.None
                            ? effectBlob.behaviours[0] : MotivationType.Hunger;
                        foodDelta      = effectBlob.value;
                    }
                    break;
                case EffectType.Hydrate:
                    if (wantConsume && distSq < nearestDrinkSq)
                    {
                        nearestDrinkSq  = distSq;
                        nearestDrink    = itemEntities[i];
                        drinkMotivation = effectBlob.behaviours.Length > 0 && effectBlob.behaviours[0] != MotivationType.None
                            ? effectBlob.behaviours[0] : MotivationType.Hunger;
                        drinkDelta      = effectBlob.value;
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
