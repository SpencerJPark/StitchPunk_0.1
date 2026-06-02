using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Fans out action entity pool slots to match current AwarenessTarget entries each frame.
// Runs first in UtilityDecisionSystemGroup so downstream scoring only sees valid, targeted candidates.
//
// Self-targeting actions: enable slot[0], clear target, disable remaining slots.
// SingleTarget actions:   enable one slot per AwarenessTarget entry (up to pool size),
//                         set targetEntity on each; disable unused slots.
[BurstCompile]
[UpdateInGroup(typeof(UtilityDecisionSystemGroup), OrderFirst = true)]
public partial struct ActionInstancingSystem : ISystem
{
    private ComponentLookup<UtilityAction> _utilityActionLookup;
    private ComponentLookup<ActionActive>  _actionActiveLookup;
    private BufferLookup<AwarenessTarget>  _awarenessLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _utilityActionLookup = state.GetComponentLookup<UtilityAction>(false);
        _actionActiveLookup  = state.GetComponentLookup<ActionActive>(false);
        _awarenessLookup     = state.GetBufferLookup<AwarenessTarget>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _utilityActionLookup.Update(ref state);
        _actionActiveLookup.Update(ref state);
        _awarenessLookup.Update(ref state);

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new ActionInstancingJob
        {
            aiConfig            = brainLibrary.blob,
            utilityActionLookup = _utilityActionLookup,
            actionActiveLookup  = _actionActiveLookup,
            awarenessLookup     = _awarenessLookup,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrainV2))]
public partial struct ActionInstancingJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;

    // NativeDisableParallelForRestriction is safe here: each unit owns unique action entities,
    // so no two threads ever write to the same action entity.
    [NativeDisableParallelForRestriction] public ComponentLookup<UtilityAction> utilityActionLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<ActionActive>  actionActiveLookup;

    [ReadOnly] public BufferLookup<AwarenessTarget> awarenessLookup;

    public void Execute(Entity unit, in DynamicBuffer<OwnedAction> ownedActions)
    {
        bool hasTargets = awarenessLookup.TryGetBuffer(unit, out DynamicBuffer<AwarenessTarget> targets);

        int ownedCount = ownedActions.Length;
        int defIndex   = -1;
        int slotStart  = 0;

        // Walk owned actions grouped by actionDefIndex (spawn system adds them in def order).
        for (int i = 0; i <= ownedCount; i++)
        {
            int currentDef = (i < ownedCount) ? ownedActions[i].actionDefIndex : -1;

            // When def changes (or we've reached the end), process the completed group.
            if (currentDef != defIndex && defIndex >= 0)
            {
                ref ActionDefBlob actionDef = ref aiConfig.Value.actionDefs[defIndex];
                int slotEnd = i;

                if (actionDef.targeting == TargetingMode.Self)
                {
                    SetSlot(ownedActions[slotStart].actionEntity, Entity.Null, true);
                    for (int slot = slotStart + 1; slot < slotEnd; slot++)
                        SetSlot(ownedActions[slot].actionEntity, Entity.Null, false);
                }
                else
                {
                    int targetCount  = hasTargets ? targets.Length : 0;
                    int poolSize     = slotEnd - slotStart;
                    int enabledCount = targetCount < poolSize ? targetCount : poolSize;

                    for (int slot = 0; slot < enabledCount; slot++)
                        SetSlot(ownedActions[slotStart + slot].actionEntity,
                                targets[slot].targetEntity, true);

                    for (int slot = enabledCount; slot < poolSize; slot++)
                        SetSlot(ownedActions[slotStart + slot].actionEntity, Entity.Null, false);
                }

                slotStart = i;
            }

            defIndex = currentDef;
        }
    }

    private void SetSlot(Entity actionEntity, Entity target, bool enabled)
    {
        actionActiveLookup.SetComponentEnabled(actionEntity, enabled);
        if (!enabled) return;
        utilityActionLookup.GetRefRW(actionEntity).ValueRW.targetEntity = target;
    }
}
