using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(CombatExecutionSystemGroup))]
public partial struct AttackRequestSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Dead>          deadLookup;
    private BufferLookup<AnimEventOutput>      animEventOutputLookup;
    private ComponentLookup<AnimEventsPending> animEventsPendingLookup;
    private ComponentLookup<UnitData>          unitDataLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<DamageBus>();
        state.RequireForUpdate<UnitDataLibrary>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        deadLookup      = state.GetComponentLookup<Dead>(true);
        animEventOutputLookup   = state.GetBufferLookup<AnimEventOutput>(true);
        animEventsPendingLookup = state.GetComponentLookup<AnimEventsPending>(true);
        unitDataLookup          = state.GetComponentLookup<UnitData>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        animEventOutputLookup.Update(ref state);
        animEventsPendingLookup.Update(ref state);
        unitDataLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        NativeQueue<DamageEvent>.ParallelWriter damageWriter =
            SystemAPI.GetSingleton<DamageBus>().raw.AsParallelWriter();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Combat) != 0;

        EntityCommandBuffer.ParallelWriter logEcb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
                .AsParallelWriter()
            : default;

        state.Dependency = new AttackRequestJob
        {
            transformLookup = transformLookup,
            deadLookup      = deadLookup,
            attackLibrary   = attackLibrary,
            unitLibrary     = unitLibrary,
            deltaTime       = SystemAPI.Time.DeltaTime,
            damageWriter    = damageWriter,
            logEcb          = logEcb,
            loggingEnabled  = loggingEnabled,
            timestamp       = SystemAPI.Time.ElapsedTime,
            animEventOutputLookup   = animEventOutputLookup,
            animEventsPendingLookup = animEventsPendingLookup,
            unitDataLookup          = unitDataLookup,
        }.ScheduleParallel(state.Dependency);

        // A NativeQueue behind a singleton bypasses ECS dependency tracking, so hand the handle to the bus owner.
        state.World.GetExistingSystemManaged<DamageBusSystem>()
            .AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct AttackRequestJob : IJobEntity
{
    // Lunge tolerance: absorbs repath jitter after the action system confirmed range; a fled target still whiffs.
    private const float HIT_RANGE_MULT = 1.5f;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead>           deadLookup;
    [ReadOnly] public BufferLookup<AnimEventOutput>      animEventOutputLookup;
    [ReadOnly] public ComponentLookup<AnimEventsPending> animEventsPendingLookup;
    [ReadOnly] public ComponentLookup<UnitData>          unitDataLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>   unitLibrary;
    public float                                    deltaTime;
    public NativeQueue<DamageEvent>.ParallelWriter  damageWriter;
    public EntityCommandBuffer.ParallelWriter       logEcb;
    public bool                                     loggingEnabled;
    public double                                   timestamp;

    public void Execute(
        [ChunkIndexInQuery] int     sortKey,
        Entity                      attackerEntity,
        in LocalTransform           attackerTransform,
        ref AttackRequest           attackRequest,
        EnabledRefRW<AttackRequest> attackRequestEnabled)
    {
        if (attackRequest.hitFired)
            return;

        int attackIndex = (int)attackRequest.damageSource;
        if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
            return;
        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

        // The clip's AnimEvents.Attack event decides hit timing; hitTime is only the timeout fallback,
        // so keep it above the authored event time. elapsed is reset at swing start (FireAction / PlayerAttackSystem).
        attackRequest.elapsed += deltaTime;

        // Match by playing animation key, not a fixed layer, so an attack on any layer still lands.
        uint attackAnimationKey = 0;
        if (unitDataLookup.TryGetComponent(attackerEntity, out UnitData attackerUnitData))
        {
            int unitIndex = unitLibrary.Value.FindByUnitType(attackerUnitData.unitType);
            if (unitIndex >= 0)
            {
                ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
                ActionType actionType = AIUtils.GetActionByAttack(ref unitBlob, attackRequest.damageSource);
                attackAnimationKey = AIUtils.GetAnimationKeyByAction(ref unitBlob, actionType);
            }
        }

        bool attackEventFired = false;
        if (attackAnimationKey != 0
            && animEventsPendingLookup.HasComponent(attackerEntity)
            && animEventsPendingLookup.IsComponentEnabled(attackerEntity)
            && animEventOutputLookup.HasBuffer(attackerEntity))
        {
            DynamicBuffer<AnimEventOutput> attackerEvents = animEventOutputLookup[attackerEntity];
            for (int eventIndex = 0; eventIndex < attackerEvents.Length; eventIndex++)
            {
                if (attackerEvents[eventIndex].eventKey == AnimEvents.Attack
                    && attackerEvents[eventIndex].animationKey == attackAnimationKey)
                {
                    attackEventFired = true;
                    break;
                }
            }
        }

        bool timedOut = attackRequest.elapsed >= attackBlob.hitTime;
        if (!attackEventFired && !timedOut)
            return;

        if (timedOut && !attackEventFired && loggingEnabled)
        {
            LogUtil.Log(ref logEcb, sortKey,
                $"[Attack] Attack event never arrived for attacker {attackerEntity.Index} — falling back to hitTime",
                LogLevel.Warning, timestamp, category: LogCategory.Combat);
        }

        Entity victim = attackRequest.targetEntity;

        // Alive means Dead is present but disabled.
        bool victimAlive = deadLookup.HasComponent(victim) &&
                           !deadLookup.IsComponentEnabled(victim);

        if (victimAlive &&
            transformLookup.TryGetComponent(victim, out LocalTransform victimTransform))
        {
            float distanceSq = math.distancesq(attackerTransform.Position, victimTransform.Position);
            float hitRange   = attackBlob.range * HIT_RANGE_MULT;

            if (distanceSq <= hitRange * hitRange)
            {
                // Validation is producer-side so whiffs never enqueue; DamageResolutionSystem expands AOE.
                damageWriter.Enqueue(new DamageEvent
                {
                    targetEntity    = victim,
                    sourceEntity    = attackerEntity,
                    damageSource    = attackBlob.damageSource,
                    damageAmount    = attackBlob.damageAmount,
                    distance        = math.sqrt(distanceSq),
                    ragdollForce    = attackBlob.ragdollForce,
                    launchForceY    = attackBlob.launchForceY,
                    launchForceX    = attackBlob.launchForceX,
                    flailIntensity  = attackBlob.flailIntensity,
                    spin            = attackBlob.spin,
                    restitution     = attackBlob.restitution,
                    damageBehaviour = attackBlob.damageBehaviour,
                    sourcePosition  = attackerTransform.Position,
                    range           = attackBlob.range,
                });

                if (loggingEnabled)
                    LogUtil.Log(ref logEcb, sortKey,
                        $"[Attack] Hit {victim.Index} for {attackBlob.damageAmount} dmg (dist {math.round(math.sqrt(distanceSq) * 100f) / 100f})",
                        LogLevel.Info, timestamp, category: LogCategory.Combat);
            }
            else if (loggingEnabled)
            {
                LogUtil.Log(ref logEcb, sortKey,
                    $"[Attack] Whiffed on {victim.Index} — out of range (dist {math.round(math.sqrt(distanceSq) * 100f) / 100f} > {hitRange})",
                    LogLevel.Info, timestamp, category: LogCategory.Combat);
            }
        }
        else if (loggingEnabled)
        {
            LogUtil.Log(ref logEcb, sortKey,
                $"[Attack] Skipped — target {victim.Index} not alive or invalid",
                LogLevel.Info, timestamp, category: LogCategory.Combat);
        }

        // Mark fired even on a whiff so a target stepping out of range is not retried.
        attackRequest.hitFired       = true;
        attackRequestEnabled.ValueRW = false;
    }
}
