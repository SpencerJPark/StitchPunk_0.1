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
    private BufferLookup<Hurt>             hurtBufferLookup;
    private ComponentLookup<Alive>         aliveLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();

        transformLookup  = state.GetComponentLookup<LocalTransform>(true);
        hurtBufferLookup = state.GetBufferLookup<Hurt>(false);
        aliveLookup      = state.GetComponentLookup<Alive>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        hurtBufferLookup.Update(ref state);
        aliveLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Combat) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        // Single-threaded: multiple attackers may write to the same target's Hurt buffer
        state.Dependency = new AttackRequestJob
        {
            transformLookup  = transformLookup,
            hurtBufferLookup = hurtBufferLookup,
            aliveLookup      = aliveLookup,
            attackLibrary    = attackLibrary,
            deltaTime        = SystemAPI.Time.DeltaTime,
            ecb              = ecb,
            loggingEnabled   = loggingEnabled,
            timestamp        = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
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
    public            BufferLookup<Hurt>              hurtBufferLookup;
    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
    public float              deltaTime;
    public EntityCommandBuffer ecb;
    public bool               loggingEnabled;
    public double             timestamp;

    public void Execute(
        Entity                      attackerEntity,
        in LocalTransform           attackerTransform,
        ref AttackRequest           attackRequest,
        EnabledRefRW<AttackRequest> attackRequestEnabled)
    {
        if (attackRequest.hitFired)
            return;

        int attackIndex = (int)attackRequest.attackType;
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

        bool victimAlive = aliveLookup.HasComponent(victim) &&
                           aliveLookup.IsComponentEnabled(victim);

        if (victimAlive &&
            transformLookup.TryGetComponent(victim, out LocalTransform victimTransform) &&
            hurtBufferLookup.HasBuffer(victim))
        {
            float distanceSq = math.distancesq(attackerTransform.Position, victimTransform.Position);
            float hitRange   = attackBlob.range * HIT_RANGE_MULT;

            if (distanceSq <= hitRange * hitRange)
            {
                hurtBufferLookup[victim].Add(new Hurt
                {
                    attackerEntity = attackerEntity,
                    attackType     = attackBlob.attackType,
                    distance       = math.sqrt(distanceSq),
                    damageAmount   = attackBlob.damageAmount,
                    hitSourceX     = attackerTransform.Position.x,
                    ragdollForce   = attackBlob.ragdollForce,
                    launchForceY   = attackBlob.launchForceY,
                    launchForceX   = attackBlob.launchForceX,
                });
                if (loggingEnabled)
                    LogUtil.Log(ref ecb,
                        $"[Attack] Hit {victim.Index} for {attackBlob.damageAmount} dmg (dist {math.round(math.sqrt(distanceSq) * 100f) / 100f})",
                        LogLevel.Info, timestamp, category: LogCategory.Combat);
            }
            else if (loggingEnabled)
            {
                LogUtil.Log(ref ecb,
                    $"[Attack] Whiffed on {victim.Index} — out of range (dist {math.round(math.sqrt(distanceSq) * 100f) / 100f} > {hitRange})",
                    LogLevel.Info, timestamp, category: LogCategory.Combat);
            }
        }
        else if (loggingEnabled)
        {
            LogUtil.Log(ref ecb,
                $"[Attack] Skipped — target {victim.Index} not alive or invalid",
                LogLevel.Info, timestamp, category: LogCategory.Combat);
        }

        // Mark fired regardless — prevents re-attempts if target stepped out of range
        attackRequest.hitFired       = true;
        attackRequestEnabled.ValueRW = false;
    }
}