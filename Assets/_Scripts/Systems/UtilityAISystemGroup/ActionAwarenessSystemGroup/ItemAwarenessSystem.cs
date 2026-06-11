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
// Loose items are those with EquipBy.owner == Entity.Null; registered in SpatialHashRegistry.itemCells each frame.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct ItemAwarenessSystem : ISystem
{
    private ComponentLookup<UnitEquip>     _unitEquipLookup;
    private ComponentLookup<Item>          _itemLookup;
    private ComponentLookup<EquipBy>       _equipByLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<EffectLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        state.RequireForUpdate<SpatialHashRegistry>();

        _unitEquipLookup  = state.GetComponentLookup<UnitEquip>(true);
        _itemLookup       = state.GetComponentLookup<Item>(true);
        _equipByLookup    = state.GetComponentLookup<EquipBy>(true);
        _transformLookup  = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _unitEquipLookup.Update(ref state);
        _itemLookup.Update(ref state);
        _equipByLookup.Update(ref state);
        _transformLookup.Update(ref state);

        BlobAssetReference<ItemLibraryBlob> itemLibrary =
            SystemAPI.GetSingleton<ItemLibrary>().library;
        BlobAssetReference<EffectLibraryBlob> effectLibrary =
            SystemAPI.GetSingleton<EffectLibrary>().library;
        BlobAssetReference<BrainLibraryBlob> aiConfig =
            SystemAPI.GetSingleton<BrainLibrary>().blob;

        SpatialHashRegistry registry = SystemAPI.GetSingleton<SpatialHashRegistry>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Items) != 0;

        EntityCommandBuffer.ParallelWriter ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
                .AsParallelWriter()
            : default;

        state.Dependency = new ItemAwarenessJob
        {
            itemCells       = registry.itemCells,
            itemLookup      = _itemLookup,
            equipByLookup   = _equipByLookup,
            transformLookup = _transformLookup,
            itemLibrary     = itemLibrary,
            effectLibrary   = effectLibrary,
            aiConfig        = aiConfig,
            unitEquipLookup = _unitEquipLookup,
            ecb             = ecb,
            loggingEnabled  = loggingEnabled,
            timestamp       = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
public partial struct ItemAwarenessJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity>      itemCells;
    [ReadOnly] public ComponentLookup<Item>                         itemLookup;
    [ReadOnly] public ComponentLookup<EquipBy>                      equipByLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>               transformLookup;
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>           itemLibrary;
    [ReadOnly] public BlobAssetReference<EffectLibraryBlob>         effectLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>          aiConfig;
    [ReadOnly] public ComponentLookup<UnitEquip>                    unitEquipLookup;
    public EntityCommandBuffer.ParallelWriter ecb;
    public bool                              loggingEnabled;
    public double                            timestamp;

    public void Execute(
        Entity                          self,
        [EntityIndexInQuery] int        entityIndex,
        in LocalTransform               transform,
        in Awareness                    awareness,
        in Health                       health,
        in UtilityBrain                 brain,
        in CurrentAction                currentAction,
        in DynamicBuffer<ThreatEntry>   threats,
        ref DynamicBuffer<Motivation>   motivations,
        ref DynamicBuffer<UtilityActions> options)
    {
        bool  hasThreat   = threats.Length > 0;
        bool  inCombat    = currentAction.action.IsCombatAction();
        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;

        bool canEquip  = unitEquipLookup.TryGetComponent(self, out UnitEquip unitEquip);
        bool hasWeapon = canEquip && unitEquip.equipItemEntity != Entity.Null;

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
        NeedType foodMotivation  = NeedType.Hunger; float foodDelta  = 0f;
        NeedType drinkMotivation = NeedType.Hunger; float drinkDelta = 0f;

        int2 centerCell = InteractionSpatialHashSystem.GetCell(myPos);
        int  cellRange  = (int)math.ceil(awareness.range / InteractionSpatialHashSystem.CELL_SIZE);

        for (int cx = centerCell.x - cellRange; cx <= centerCell.x + cellRange; cx++)
        {
            for (int cz = centerCell.y - cellRange; cz <= centerCell.y + cellRange; cz++)
            {
                int2 cell = new int2(cx, cz);
                bool hit = itemCells.TryGetFirstValue(cell, out Entity itemEntity,
                    out NativeParallelMultiHashMapIterator<int2> it);

                if (!hit) continue;

                do
                {
                    if (!equipByLookup.TryGetComponent(itemEntity, out EquipBy equip)) continue;
                    if (equip.owner != Entity.Null) continue;

                    if (!itemLookup.TryGetComponent(itemEntity, out Item item)) continue;

                    int typeIndex = (int)item.itemType;
                    if (typeIndex < 0 || typeIndex >= itemLibrary.Value.items.Length) continue;

                    ref ItemBlob blob = ref itemLibrary.Value.items[typeIndex];
                    if (blob.category == ItemCategory.None) continue;

                    if (!transformLookup.TryGetComponent(itemEntity, out LocalTransform xf)) continue;

                    float distSq = math.distancesq(myPos, xf.Position);
                    if (distSq > rangeSq) continue;

                    if (blob.category == ItemCategory.Weapon)
                    {
                        if (wantWeapon && distSq < nearestWeaponSq)
                        {
                            nearestWeaponSq = distSq;
                            nearestWeapon   = itemEntity;
                        }
                        continue;
                    }

                    // Consumable — resolve EffectType to decide which awareness slot fires.
                    int effectIndex = (int)blob.consumeEffect;
                    if (effectIndex < 0 || effectIndex >= effectLibrary.Value.effects.Length) continue;

                    ref EffectBlob effectBlob = ref effectLibrary.Value.effects[effectIndex];

                    switch (effectBlob.effectType)
                    {
                        case EffectType.Healing:
                            if (wantHealing && distSq < nearestHealSq)
                            {
                                nearestHealSq = distSq;
                                nearestHeal   = itemEntity;
                            }
                            break;
                        case EffectType.Feed:
                            if (wantConsume && distSq < nearestFoodSq)
                            {
                                nearestFoodSq  = distSq;
                                nearestFood    = itemEntity;
                                foodMotivation = effectBlob.behaviours.Length > 0 && effectBlob.behaviours[0] != NeedType.None
                                    ? effectBlob.behaviours[0] : NeedType.Hunger;
                                foodDelta      = effectBlob.value;
                            }
                            break;
                        case EffectType.Hydrate:
                            if (wantConsume && distSq < nearestDrinkSq)
                            {
                                nearestDrinkSq  = distSq;
                                nearestDrink    = itemEntity;
                                drinkMotivation = effectBlob.behaviours.Length > 0 && effectBlob.behaviours[0] != NeedType.None
                                    ? effectBlob.behaviours[0] : NeedType.Hunger;
                                drinkDelta      = effectBlob.value;
                            }
                            break;
                    }
                }
                while (itemCells.TryGetNextValue(out itemEntity, ref it));
            }
        }

        // Weapon — urgent: arm up to defend. SelfDefence motivation drives the blob scoring curve.
        if (nearestWeapon != Entity.Null)
        {
            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.EquipWeapon);
            if (defIndex >= 0)
            {
                AIUtils.SetMotivationValue(ref motivations, NeedType.SelfDefence, 100f);
                options.Add(new UtilityActions
                {
                    actionType      = ActionType.EquipWeapon,
                    actionDefIndex  = defIndex,
                    needsValidation = false,
                    targetEntity    = nearestWeapon,
                });
                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex, $"[ItemAwareness] Entity {self.Index} found weapon {nearestWeapon.Index}. Action: EquipWeapon", LogLevel.Info, timestamp, category: LogCategory.Items);
            }
        }

        // Healing — SelfPreservation motivation drives the blob scoring curve.
        if (nearestHeal != Entity.Null)
        {
            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.UseHealingItem);
            if (defIndex >= 0)
            {
                float urgency = (1f - healthRatio) * 100f;
                SetMotivationAtLeast(ref motivations, NeedType.SelfPreservation, urgency);
                options.Add(new UtilityActions
                {
                    actionType      = ActionType.UseHealingItem,
                    actionDefIndex  = defIndex,
                    needsValidation = false,
                    targetEntity    = nearestHeal,
                });
                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex, $"[ItemAwareness] Entity {self.Index} found heal item {nearestHeal.Index}. Action: UseHealingItem", LogLevel.Info, timestamp, category: LogCategory.Items);
            }
        }

        // Food — Hunger motivation drives blob scoring curve.
        if (nearestFood != Entity.Null)
        {
            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Eat);
            if (defIndex >= 0)
            {
                options.Add(new UtilityActions
                {
                    actionType      = ActionType.Eat,
                    actionDefIndex  = defIndex,
                    needsValidation = false,
                    targetEntity    = nearestFood,
                });
                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex, $"[ItemAwareness] Entity {self.Index} found food {nearestFood.Index}. Action: Eat", LogLevel.Info, timestamp, category: LogCategory.Items);
            }
        }

        if (nearestDrink != Entity.Null)
        {
            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Drink);
            if (defIndex >= 0)
            {
                options.Add(new UtilityActions
                {
                    actionType      = ActionType.Drink,
                    actionDefIndex  = defIndex,
                    needsValidation = false,
                    targetEntity    = nearestDrink,
                });
                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex, $"[ItemAwareness] Entity {self.Index} found drink {nearestDrink.Index}. Action: Drink", LogLevel.Info, timestamp, category: LogCategory.Items);
            }
        }
    }

    // Raises a motivation toward value without lowering it (avoids stomping FleeAwareness' SelfPreservation = 100).
    private static void SetMotivationAtLeast(ref DynamicBuffer<Motivation> motivations, NeedType type, float value)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            Motivation motivation = motivations[i];
            if (motivation.needType != type)
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
