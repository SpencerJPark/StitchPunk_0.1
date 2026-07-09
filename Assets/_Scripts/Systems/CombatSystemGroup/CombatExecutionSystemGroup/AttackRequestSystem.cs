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

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<DamageBus>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        deadLookup      = state.GetComponentLookup<Dead>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        // Recycled DamageBus queue (v2) — each attacker Enqueues its own DamageEvent value; no entity
        // create/destroy. NativeQueue.ParallelWriter is safe from ScheduleParallel.
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
            deltaTime       = SystemAPI.Time.DeltaTime,
            damageWriter    = damageWriter,
            logEcb          = logEcb,
            loggingEnabled  = loggingEnabled,
            timestamp       = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);

        // Manual dependency wiring — a NativeQueue through a singleton bypasses ECS auto-tracking,
        // so hand this producer's write handle to the bus owner (the EntityCommandBufferSystem
        // pattern). Fetch the managed owner here rather than storing a GC reference in the struct.
        state.World.GetExistingSystemManaged<DamageBusSystem>()
            .AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct AttackRequestJob : IJobEntity
{
    // Lunge tolerance: a swing is only ever fired against a target the action system
    // already confirmed in range, so allow this much drift before the hit lands rather
    // than whiffing on minor repath jitter. A target that genuinely fled is still missed.
    private const float HIT_RANGE_MULT = 1.5f;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead>           deadLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
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

        // Hit timing is self-contained and delta-time driven: elapsed is reset to 0 at the
        // start of each swing (FireAction / PlayerAttackSystem) and advances here. This is
        // frame-rate-correct and independent of system ordering. Using >= fires correctly
        // even when a large deltaTime skips past hitTime.
        attackRequest.elapsed += deltaTime;
        if (attackRequest.elapsed < attackBlob.hitTime)
            return;

        Entity victim = attackRequest.targetEntity;

        // Present-and-not-dead: Dead is the sole life-state, so a valid victim has the
        // Dead component present but disabled.
        bool victimAlive = deadLookup.HasComponent(victim) &&
                           !deadLookup.IsComponentEnabled(victim);

        if (victimAlive &&
            transformLookup.TryGetComponent(victim, out LocalTransform victimTransform))
        {
            float distanceSq = math.distancesq(attackerTransform.Position, victimTransform.Position);
            float hitRange   = attackBlob.range * HIT_RANGE_MULT;

            if (distanceSq <= hitRange * hitRange)
            {
                // Confirmed hit — Enqueue a DamageEvent value into the bus (range/alive validation
                // stays producer-side, so whiffs never enqueue). AOE-tagged attacks are expanded to
                // per-target events by DamageResolutionSystem; SingleTarget flows straight through.
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

        // Mark fired regardless — prevents re-attempts if target stepped out of range
        attackRequest.hitFired       = true;
        attackRequestEnabled.ValueRW = false;
    }
}
