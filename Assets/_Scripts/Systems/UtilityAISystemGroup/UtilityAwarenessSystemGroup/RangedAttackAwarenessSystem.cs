using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// One ranged awareness for rocks and guns (SmartHumanAI H5-D9): Throw or Shoot by the held item's rangedKind, Keep Range
// below its min range, and the line-of-fire side-step (H5-D11). All share Critical and never coexist for one unit.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
[UpdateBefore(typeof(GuardPostAwarenessSystem))]
public partial struct RangedAttackAwarenessSystem : ISystem
{
    // Above the tier-3 fight so a unit closing to melee stops and fires; GuardPost's leash (5) still wins.
    public const int RANGED_ATTACK_PRIORITY = AIPriorityTier.Critical;

    public const int    KEEP_RANGE_PRIORITY               = AIPriorityTier.Critical;
    public const float  KEEP_RANGE_PREFERRED_FRACTION     = 0.3f;
    public const double KEEP_RANGE_COOLDOWN_SECONDS       = 4.0;
    public const float  KEEP_RANGE_FAN_STEP_DEGREES       = 20f;

    public const float LINE_OF_FIRE_CLEARANCE_METERS        = 0.3f;
    public const float LINE_OF_FIRE_DISCIPLINE_THRESHOLD    = 0.35f;
    public const float INTOXICATED_LEVEL_FOR_CARELESSNESS   = 0.5f;
    public const float INTOXICATED_DISCIPLINE_PENALTY       = 0.5f;
    public const float SIDE_STEP_METERS                     = 2.5f;
    public const float SIDE_STEP_ALLY_COUNT_RADIUS_METERS   = 10f;

    private ComponentLookup<Item>            itemLookup;
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Dead>            deadLookup;
    private ComponentLookup<GuardPost>       guardPostLookup;
    private ComponentLookup<TacticsCooldown> tacticsCooldownLookup;
    private ComponentLookup<Faction>         factionLookup;
    private ComponentLookup<Intoxication>    intoxicationLookup;
    private BufferLookup<PersonalityAttributes> personalityLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        itemLookup            = state.GetComponentLookup<Item>(true);
        transformLookup       = state.GetComponentLookup<LocalTransform>(true);
        deadLookup            = state.GetComponentLookup<Dead>(true);
        guardPostLookup       = state.GetComponentLookup<GuardPost>(true);
        tacticsCooldownLookup = state.GetComponentLookup<TacticsCooldown>(false);
        factionLookup         = state.GetComponentLookup<Faction>(true);
        intoxicationLookup    = state.GetComponentLookup<Intoxication>(true);
        personalityLookup     = state.GetBufferLookup<PersonalityAttributes>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        itemLookup.Update(ref state);
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        guardPostLookup.Update(ref state);
        tacticsCooldownLookup.Update(ref state);
        factionLookup.Update(ref state);
        intoxicationLookup.Update(ref state);
        personalityLookup.Update(ref state);

        // Without a registry (e.g. a fixture) there are no allies to check the line of fire against.
        NativeParallelMultiHashMap<byte, Entity> factionEntities = SystemAPI.TryGetSingleton(out FactionRegistry registry)
            ? registry.entities
            : new NativeParallelMultiHashMap<byte, Entity>(0, state.WorldUpdateAllocator);

        state.Dependency = new RangedAttackAwarenessJob
        {
            itemLookup            = itemLookup,
            transformLookup       = transformLookup,
            deadLookup            = deadLookup,
            guardPostLookup       = guardPostLookup,
            tacticsCooldownLookup = tacticsCooldownLookup,
            factionLookup         = factionLookup,
            intoxicationLookup    = intoxicationLookup,
            personalityLookup     = personalityLookup,
            factionEntities       = factionEntities,
            elapsedTime           = SystemAPI.Time.ElapsedTime,
            itemLibrary           = SystemAPI.GetSingleton<ItemLibrary>().library,
            aiConfig              = SystemAPI.GetSingleton<BrainLibrary>().blob,
        }.ScheduleParallel(state.Dependency);
    }

    // Resourceful is the unit's fire discipline; a drunk is careless (H5-D11). Deterministic so the player can learn who shoots friends.
    public static float LineOfFireDiscipline(float resourcefulness, float intoxicationLevel)
    {
        return intoxicationLevel > INTOXICATED_LEVEL_FOR_CARELESSNESS
            ? resourcefulness - INTOXICATED_DISCIPLINE_PENALTY
            : resourcefulness;
    }

    public static bool IsPointInLineOfFire(float3 shooterPosition, float3 targetPosition, float3 point)
    {
        float2 shotStart = shooterPosition.xz;
        float2 shotDelta = targetPosition.xz - shotStart;
        float  shotLengthSquared = math.dot(shotDelta, shotDelta);
        if (shotLengthSquared < 0.0001f) return false;

        float alongShot = math.dot(point.xz - shotStart, shotDelta) / shotLengthSquared;
        if (alongShot <= 0f || alongShot >= 1f) return false;

        float2 closestPointOnShot = shotStart + shotDelta * alongShot;
        float  clearance = ProjectileHitUtils.UNIT_HIT_RADIUS + LINE_OF_FIRE_CLEARANCE_METERS;
        return math.distancesq(closestPointOnShot, point.xz) <= clearance * clearance;
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct RangedAttackAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Item>           itemLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead>           deadLookup;
    [ReadOnly] public ComponentLookup<GuardPost>      guardPostLookup;
    // Each iteration writes only its own entity's cooldown.
    [NativeDisableParallelForRestriction] public ComponentLookup<TacticsCooldown> tacticsCooldownLookup;
    [ReadOnly] public ComponentLookup<Faction>        factionLookup;
    [ReadOnly] public ComponentLookup<Intoxication>   intoxicationLookup;
    [ReadOnly] public BufferLookup<PersonalityAttributes> personalityLookup;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    public double elapsedTime;
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>  itemLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;

    public void Execute(
        Entity                            self,
        in UtilityBrain                   brain,
        in UnitEquip                      unitEquip,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (unitEquip.equipItemEntity == Entity.Null) return;
        if (!itemLookup.TryGetComponent(unitEquip.equipItemEntity, out Item item)) return;

        int itemIndex = (int)item.itemType;
        if (itemIndex < 0 || itemIndex >= itemLibrary.Value.items.Length) return;

        ref ItemBlob blob = ref itemLibrary.Value.items[itemIndex];
        if (blob.aiRangedMaxRange <= 0f) return;

        Entity target = Entity.Null;
        for (int index = 0; index < actions.Length; index++)
        {
            UtilityActions option = actions[index];
            if (IsMeleeOrChaseCombatAction(option.actionType) && option.targetEntity != Entity.Null)
            {
                target = option.targetEntity;
                break;
            }
        }

        if (target == Entity.Null && IsMeleeOrChaseCombatAction(stateMachine.action))
            target = stateMachine.targetEntity;

        if (target == Entity.Null) return;
        if (deadLookup.HasComponent(target) && deadLookup.IsComponentEnabled(target)) return;
        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform)) return;

        float3 offset   = targetTransform.Position - transform.Position;
        float  distance = math.length(new float2(offset.x, offset.z));
        if (distance > blob.aiRangedMaxRange) return;

        if (distance < blob.aiRangedMinRange)
        {
            EmitKeepRange(self, brain, transform, stateMachine, targetTransform, ref blob, ref actions);
            return;
        }

        if (tacticsCooldownLookup.TryGetComponent(self, out TacticsCooldown cooldown)
            && elapsedTime < cooldown.rangedReadyAtSeconds)
            return;

        if (IsAllyInLineOfFire(self, transform.Position, targetTransform.Position, target)
            && LineOfFireDisciplineOf(self) >= RangedAttackAwarenessSystem.LINE_OF_FIRE_DISCIPLINE_THRESHOLD)
        {
            EmitSideStep(self, brain, transform, stateMachine, targetTransform, ref actions);
            return;
        }

        ActionType rangedAction = blob.rangedKind == RangedKind.Shoot ? ActionType.Shoot : ActionType.Throw;
        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, rangedAction);
        if (defIndex < 0) return;

        actions.Add(new UtilityActions
        {
            actionType      = rangedAction,
            actionDefIndex  = defIndex,
            priority        = RangedAttackAwarenessSystem.RANGED_ATTACK_PRIORITY,
            targetEntity    = target,
        });
    }

    private static bool IsMeleeOrChaseCombatAction(ActionType actionType)
    {
        return actionType.IsCombatAction()
            && actionType != ActionType.Flee
            && actionType != ActionType.Throw
            && actionType != ActionType.Shoot;
    }

    private float LineOfFireDisciplineOf(Entity self)
    {
        float resourcefulness = personalityLookup.TryGetBuffer(self, out DynamicBuffer<PersonalityAttributes> traits)
            ? PersonalityTraitLookup.Get(traits, PersonalityTypes.Resourceful)
            : 0.5f;
        float intoxicationLevel = intoxicationLookup.HasComponent(self) && intoxicationLookup.IsComponentEnabled(self)
            ? intoxicationLookup[self].level
            : 0f;
        return RangedAttackAwarenessSystem.LineOfFireDiscipline(resourcefulness, intoxicationLevel);
    }

    // Decision-time positions only: an ally can still walk into the shot locked during the wind-up (H5-D11).
    private bool IsAllyInLineOfFire(Entity self, float3 shooterPosition, float3 targetPosition, Entity target)
    {
        if (!factionLookup.TryGetComponent(self, out Faction faction)) return false;
        if (!factionEntities.TryGetFirstValue((byte)faction.factionType, out Entity ally,
                out NativeParallelMultiHashMapIterator<byte> allyIterator))
            return false;

        do
        {
            if (ally == self || ally == target) continue;
            if (deadLookup.HasComponent(ally) && deadLookup.IsComponentEnabled(ally)) continue;
            if (!transformLookup.TryGetComponent(ally, out LocalTransform allyTransform)) continue;
            if (RangedAttackAwarenessSystem.IsPointInLineOfFire(shooterPosition, targetPosition, allyTransform.Position))
                return true;
        } while (factionEntities.TryGetNextValue(out ally, ref allyIterator));

        return false;
    }

    private void EmitSideStep(
        Entity                            self,
        in UtilityBrain                   brain,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        in LocalTransform                 targetTransform,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (!tacticsCooldownLookup.TryGetComponent(self, out TacticsCooldown cooldown)) return;
        if (elapsedTime < cooldown.keepRangeReadyAtSeconds) return;
        if (stateMachine.action == ActionType.KeepRange) return;

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.KeepRange);
        if (defIndex < 0) return;

        float2 towardTarget = math.normalizesafe(targetTransform.Position.xz - transform.Position.xz, new float2(0f, 1f));
        float2 leftSide     = new float2(-towardTarget.y, towardTarget.x);
        float  sideSign     = CountAlliesOnSide(self, transform.Position, leftSide) <= CountAlliesOnSide(self, transform.Position, -leftSide) ? 1f : -1f;

        float2 sideStepXZ  = transform.Position.xz + leftSide * sideSign * RangedAttackAwarenessSystem.SIDE_STEP_METERS;
        float3 destination = ClampToGuardLeash(self, new float3(sideStepXZ.x, transform.Position.y, sideStepXZ.y));

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.KeepRange,
            actionDefIndex    = defIndex,
            priority          = RangedAttackAwarenessSystem.KEEP_RANGE_PRIORITY,
            targetPosition    = destination,
            hasTargetPosition = true,
        });

        cooldown.keepRangeReadyAtSeconds = elapsedTime + RangedAttackAwarenessSystem.KEEP_RANGE_COOLDOWN_SECONDS;
        tacticsCooldownLookup[self]      = cooldown;
    }

    private int CountAlliesOnSide(Entity self, float3 shooterPosition, float2 sideDirection)
    {
        if (!factionLookup.TryGetComponent(self, out Faction faction)) return 0;
        if (!factionEntities.TryGetFirstValue((byte)faction.factionType, out Entity ally,
                out NativeParallelMultiHashMapIterator<byte> allyIterator))
            return 0;

        float radiusSquared = RangedAttackAwarenessSystem.SIDE_STEP_ALLY_COUNT_RADIUS_METERS * RangedAttackAwarenessSystem.SIDE_STEP_ALLY_COUNT_RADIUS_METERS;
        int   allyCount = 0;
        do
        {
            if (ally == self) continue;
            if (deadLookup.HasComponent(ally) && deadLookup.IsComponentEnabled(ally)) continue;
            if (!transformLookup.TryGetComponent(ally, out LocalTransform allyTransform)) continue;

            float2 toAlly = allyTransform.Position.xz - shooterPosition.xz;
            if (math.lengthsq(toAlly) > radiusSquared) continue;
            if (math.dot(toAlly, sideDirection) > 0f) allyCount++;
        } while (factionEntities.TryGetNextValue(out ally, ref allyIterator));

        return allyCount;
    }

    private void EmitKeepRange(
        Entity                            self,
        in UtilityBrain                   brain,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        in LocalTransform                 targetTransform,
        ref ItemBlob                      blob,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (!tacticsCooldownLookup.TryGetComponent(self, out TacticsCooldown cooldown)) return;
        if (elapsedTime < cooldown.keepRangeReadyAtSeconds) return;
        if (stateMachine.action == ActionType.KeepRange) return;

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.KeepRange);
        if (defIndex < 0) return;

        float3 away     = transform.Position - targetTransform.Position;
        away.y          = 0f;
        float  awayLengthSq = math.lengthsq(away);
        away = awayLengthSq < 0.0001f ? new float3(0f, 0f, 1f) : away / math.sqrt(awayLengthSq);

        int    fanStep     = (int)(((uint)self.Index * 2654435761u) % 5u) - 2;
        float  fanRadians  = math.radians(fanStep * RangedAttackAwarenessSystem.KEEP_RANGE_FAN_STEP_DEGREES);
        float3 rotatedAway = math.mul(quaternion.RotateY(fanRadians), away);

        float  preferredRange = math.lerp(blob.aiRangedMinRange, blob.aiRangedMaxRange, RangedAttackAwarenessSystem.KEEP_RANGE_PREFERRED_FRACTION);
        float3 destination    = targetTransform.Position + rotatedAway * preferredRange;
        destination.y         = transform.Position.y;
        destination           = ClampToGuardLeash(self, destination);

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.KeepRange,
            actionDefIndex    = defIndex,
            priority          = RangedAttackAwarenessSystem.KEEP_RANGE_PRIORITY,
            targetPosition    = destination,
            hasTargetPosition = true,
        });

        cooldown.keepRangeReadyAtSeconds = elapsedTime + RangedAttackAwarenessSystem.KEEP_RANGE_COOLDOWN_SECONDS;
        tacticsCooldownLookup[self]      = cooldown;
    }

    // Backing off or side-stepping must never walk a guard into its tier-5 leash return.
    private float3 ClampToGuardLeash(Entity self, float3 destination)
    {
        if (!guardPostLookup.TryGetComponent(self, out GuardPost guardPost)) return destination;

        float3 anchorOffset = destination - guardPost.anchorPosition;
        anchorOffset.y      = 0f;
        float  anchorDistance = math.length(anchorOffset);
        float  maxDistance    = guardPost.leashRadius * GuardPostAwarenessSystem.REENGAGE_LEASH_FRACTION;
        if (anchorDistance <= maxDistance) return destination;

        float3 clamped = guardPost.anchorPosition + anchorOffset / anchorDistance * maxDistance;
        return new float3(clamped.x, destination.y, clamped.z);
    }
}
