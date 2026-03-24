using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;


[BurstCompile]
[UpdateInGroup(typeof(CombatResolutionSystemGroup))]
public partial struct AttackResolutionSystem : ISystem
{
    private ComponentLookup<UnitEquipt> unitEquiptLookup;
    private ComponentLookup<AttackData> attackDataLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private BufferLookup<Hurt> hurtBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();

        unitEquiptLookup = state.GetComponentLookup<UnitEquipt>(true);
        attackDataLookup = state.GetComponentLookup<AttackData>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        hurtBufferLookup = state.GetBufferLookup<Hurt>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitEquiptLookup.Update(ref state);
        attackDataLookup.Update(ref state);
        transformLookup.Update(ref state);
        hurtBufferLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;
        BlobAssetReference<AttackLibraryBlob> attackLibrary = SystemAPI.GetSingleton<AttackLibrary>().library;

        // Tick all cooldowns in parallel first
        state.Dependency = new TickAttackCooldownJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        // Resolve attacks single-threaded — multiple attackers can write to the same target's Hurt buffer
        state.Dependency = new AttackResolutionJob
        {
            unitEquiptLookup = unitEquiptLookup,
            attackDataLookup = attackDataLookup,
            transformLookup = transformLookup,
            hurtBufferLookup = hurtBufferLookup,
            attackLibrary = attackLibrary
        }.Schedule(state.Dependency);
    }
}

// Ticks cooldown down on all units that have AttackCooldown, regardless of Attack enabled state
[BurstCompile]
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
public partial struct TickAttackCooldownJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref AttackCooldown cooldown)
    {
        if (cooldown.timer > 0f)
            cooldown.timer = math.max(0f, cooldown.timer - deltaTime);
    }
}

// Processes units with Attack enabled — resolves which AttackData to use, range checks, writes Hurt buffer
[BurstCompile]
[WithAll(typeof(Attack))]
public partial struct AttackResolutionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<UnitEquipt> unitEquiptLookup;
    [ReadOnly] public ComponentLookup<AttackData> attackDataLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    public BufferLookup<Hurt> hurtBufferLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;

    public void Execute(
        Entity attackerEntity,
        in Target target,
        in LocalTransform attackerTransform,
        ref AttackCooldown cooldown,
        EnabledRefRW<Attack> attackEnabled)
    {
        if (cooldown.timer > 0f)
            return;

        if (target.entity == Entity.Null)
            return;

        // Item AttackData takes priority over unit's base AttackData
        AttackData attackData = attackDataLookup[attackerEntity];
        if (unitEquiptLookup.TryGetComponent(attackerEntity, out UnitEquipt unitEquipt))
        {
            if (unitEquipt.equiptItemEntity != Entity.Null &&
                attackDataLookup.HasComponent(unitEquipt.equiptItemEntity))
            {
                attackData = attackDataLookup[unitEquipt.equiptItemEntity];
            }
        }

        // Fetch full attack stats from baked blob
        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[(int)attackData.attackType];

        // Range check
        if (!transformLookup.TryGetComponent(target.entity, out LocalTransform targetTransform))
            return;

        float distanceSq = math.distancesq(attackerTransform.Position, targetTransform.Position);
        if (distanceSq > attackBlob.range * attackBlob.range)
            return;

        // Write hit to target's Hurt buffer
        if (!hurtBufferLookup.HasBuffer(target.entity))
            return;

        hurtBufferLookup[target.entity].Add(new Hurt
        {
            attackerEntity = attackerEntity,
            distance = math.sqrt(distanceSq),
            damageAmount = attackBlob.damageAmount
        });

        // Reset cooldown and clear intent flag
        cooldown.timer = attackBlob.cooldown;
        attackEnabled.ValueRW = false;
    }
}
