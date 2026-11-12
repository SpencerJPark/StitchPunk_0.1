using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Debug keys for cutscene testing: fire the narrative event, skip the playing cutscene, dump actor gate state.
// Polls Keyboard.current because the project uses the new Input System only.
public class CutsceneDebugTrigger : MonoBehaviour
{
    [Tooltip("NarrativeIds.Events constant to fire — the enclosing NarrativeEventSO's PlayCutsceneAction owns the actual cutscene.")]
    [SerializeField] private int narrativeEventId = NarrativeIds.Events.RendezvousTest;
    // Deliberately no function keys: F9 is an Editor shortcut that never reaches the game, and F11 arrives as
    // Key.Home on laptops sharing the F-row. Letter and punctuation keys have no such collision.
    [SerializeField] private Key key = Key.H;
    [SerializeField] private Key skipKey = Key.Backslash;
    [SerializeField] private Key dumpActorStateKey = Key.Semicolon;

    // The toolkit never auto-paths the Player, so this teleports them onto an issued mark for solo testing.
    [Tooltip("Debug convenience only: the instant the toolkit issues the player a mark, teleport them onto it instead of requiring a manual walk. The toolkit itself never auto-paths the Player (G2 §4) — this is purely for solo testing.")]
    [SerializeField] private bool autoWalkPlayerToMark = true;

    private World cachedWorld;
    private EntityQuery narrativeQuery;
    private EntityQuery cutscenePlayQuery;
    private EntityQuery playerQuery;
    private EntityQuery cutsceneActorQuery;

    private Entity playerEntity = Entity.Null;
    private bool hasAutoWalkedCurrentMark;

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EnsureQueries(world);
        EntityManager entityManager = world.EntityManager;

        if (Keyboard.current[key].wasPressedThisFrame)
            FireNarrativeEvent(entityManager);

        if (Keyboard.current[skipKey].wasPressedThisFrame)
            RequestSkipOfActiveCutscene(entityManager);

        if (Keyboard.current[dumpActorStateKey].wasPressedThisFrame)
            DumpCutsceneActorState(entityManager);

        if (autoWalkPlayerToMark)
            TryAutoWalkPlayerToMark(entityManager);
    }

    // Queries are created once and reused: an undisposed CreateEntityQuery leaks unmanaged state.
    // Rebuilt only when the World changes (entering Play mode swaps it under a surviving instance).
    private void EnsureQueries(World world)
    {
        if (cachedWorld == world) return;

        DisposeQueries();
        cachedWorld = world;
        EntityManager entityManager = world.EntityManager;
        narrativeQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NarrativeEventTag>());
        cutscenePlayQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutscenePlay>());
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());

        // IgnoreComponentEnabledState so the dump also sees actors whose CutsceneActor is stuck on or off.
        cutsceneActorQuery = new EntityQueryBuilder(Unity.Collections.Allocator.Temp)
            .WithAll<CutsceneActor>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build(entityManager);
    }

    private void DisposeQueries()
    {
        if (cachedWorld == null || !cachedWorld.IsCreated) return;
        if (narrativeQuery != default) narrativeQuery.Dispose();
        if (cutscenePlayQuery != default) cutscenePlayQuery.Dispose();
        if (playerQuery != default) playerQuery.Dispose();
        if (cutsceneActorQuery != default) cutsceneActorQuery.Dispose();
    }

    // One line per actor listing every gate that can stop it walking. Movement off or CutsceneActor ON means
    // no movement; behavior=None phase=Execute forever means frozen AI; mode=Stop with agent inactive means pathing gave up.
    private void DumpCutsceneActorState(EntityManager entityManager)
    {
        Unity.Collections.NativeArray<Entity> actorEntities =
            cutsceneActorQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        System.Text.StringBuilder report = new System.Text.StringBuilder();
        report.Append("=== CutsceneActor state dump (").Append(actorEntities.Length).Append(" actors) ===");
        report.Append('\n').Append(DescribeCutsceneState(entityManager));

        for (int actorIndex = 0; actorIndex < actorEntities.Length; actorIndex++)
        {
            Entity actorEntity = actorEntities[actorIndex];
            report.Append('\n').Append(DescribeActor(entityManager, actorEntity));
        }

        actorEntities.Dispose();
        Debug.Log(report.ToString());
    }

    // CutsceneEndSystem releases actors only once playback is complete, so activeCutscene=ON means nobody was
    // released. pausedOnHold=True names the rendezvous hold, which frees the player while bound NPCs stay frozen.
    private string DescribeCutsceneState(EntityManager entityManager)
    {
        if (narrativeQuery.IsEmpty)
            return "narrative: <no NarrativeEventTag entity>";

        Entity narrativeEntity = narrativeQuery.GetSingletonEntity();
        System.Text.StringBuilder line = new System.Text.StringBuilder();
        line.Append("narrative ").Append(narrativeEntity.ToString());

        AppendFlag(line, entityManager, narrativeEntity, "activeCutscene");
        AppendFlag(line, entityManager, narrativeEntity, "cutsceneActiveTag");
        AppendFlag(line, entityManager, narrativeEntity, "activeNarrativeEvent");
        AppendFlag(line, entityManager, narrativeEntity, "onNarrativeEvent");

        Entity playRequestEntity = entityManager.GetComponentData<ActiveCutscene>(narrativeEntity).playRequest;
        if (!entityManager.Exists(playRequestEntity))
        {
            line.Append(" playRequest=<destroyed/none>");
            return line.ToString();
        }

        line.Append(" playRequest=").Append(playRequestEntity.ToString());
        if (entityManager.HasComponent<CutscenePlaybackState>(playRequestEntity))
        {
            CutscenePlaybackState playback = entityManager.GetComponentData<CutscenePlaybackState>(playRequestEntity);
            line.Append(" isComplete=").Append(playback.isComplete)
                .Append(" pausedOnHold=").Append(playback.isPausedOnHold)
                .Append(" segment=").Append(playback.segmentIndex)
                .Append(" t=").Append(playback.timeInSegment.ToString("F2"));
        }
        else
        {
            line.Append(" <no CutscenePlaybackState>");
        }

        if (entityManager.HasBuffer<CutsceneActorBinding>(playRequestEntity))
        {
            DynamicBuffer<CutsceneActorBinding> bindings =
                entityManager.GetBuffer<CutsceneActorBinding>(playRequestEntity);
            line.Append(" bindings=").Append(bindings.Length);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                line.Append(" [slot").Append(bindings[bindingIndex].slotId).Append('=')
                    .Append(bindings[bindingIndex].actorEntity.ToString()).Append(']');
            }
        }

        return line.ToString();
    }

    private string DescribeActor(EntityManager entityManager, Entity actorEntity)
    {
        System.Text.StringBuilder line = new System.Text.StringBuilder();
        line.Append(entityManager.HasComponent<Player>(actorEntity) ? "PLAYER " : "unit   ");
        line.Append(actorEntity.ToString());

        AppendFlag(line, entityManager, actorEntity, "cutsceneActor");
        AppendFlag(line, entityManager, actorEntity, "movement");
        AppendFlag(line, entityManager, actorEntity, "dead");
        AppendFlag(line, entityManager, actorEntity, "utilityBrain");
        AppendFlag(line, entityManager, actorEntity, "actionRequest");
        AppendFlag(line, entityManager, actorEntity, "actionInterrupt");
        AppendFlag(line, entityManager, actorEntity, "pathRequest");
        AppendFlag(line, entityManager, actorEntity, "dstarFollower");
        AppendFlag(line, entityManager, actorEntity, "movementStuck");
        AppendFlag(line, entityManager, actorEntity, "moveToMark");
        AppendFlag(line, entityManager, actorEntity, "markIssued");
        AppendFlag(line, entityManager, actorEntity, "cutsceneFacing");

        // Health.kill* is stamped on the lethal event, so a corpse names its killer (cutscene vs ordinary combat).
        if (entityManager.HasComponent<Health>(actorEntity))
        {
            Health health = entityManager.GetComponentData<Health>(actorEntity);
            line.Append(" hp=").Append(health.healthAmount).Append('/').Append(health.healthAmountMax);
            if (entityManager.HasComponent<Dead>(actorEntity) && entityManager.IsComponentEnabled<Dead>(actorEntity))
                line.Append(" killedBy=").Append(health.killDamageSource);
        }

        if (entityManager.HasComponent<Faction>(actorEntity))
            line.Append(" faction=").Append(entityManager.GetComponentData<Faction>(actorEntity).factionType);

        if (entityManager.HasComponent<Parent>(actorEntity))
            line.Append(" PARENTED=").Append(entityManager.GetComponentData<Parent>(actorEntity).Value.ToString());

        if (entityManager.HasComponent<LocalTransform>(actorEntity))
            line.Append(" pos=").Append(entityManager.GetComponentData<LocalTransform>(actorEntity).Position.ToString());

        if (entityManager.HasComponent<Movement>(actorEntity))
        {
            Movement movement = entityManager.GetComponentData<Movement>(actorEntity);
            line.Append(" moveTarget=").Append(movement.targetPosition.ToString())
                .Append(" isMoving=").Append(movement.isMoving)
                .Append(" speed=").Append(movement.moveSpeed);
        }

        if (entityManager.HasComponent<PathRequest>(actorEntity))
            line.Append(" pathMode=").Append(entityManager.GetComponentData<PathRequest>(actorEntity).requestedMode);

        if (entityManager.HasComponent<PathfindingAgent>(actorEntity))
        {
            PathfindingAgent agent = entityManager.GetComponentData<PathfindingAgent>(actorEntity);
            line.Append(" agentActive=").Append(agent.isActive).Append(" agentMode=").Append(agent.currentMode);
        }

        if (entityManager.HasComponent<StateMachine>(actorEntity))
        {
            StateMachine stateMachine = entityManager.GetComponentData<StateMachine>(actorEntity);
            line.Append(" action=").Append(stateMachine.action)
                .Append(" behavior=").Append(stateMachine.activeBehavior)
                .Append(" phase=").Append(stateMachine.currentPhase)
                .Append(" cmd=").Append(stateMachine.CurrentCommandIndex)
                .Append(" pending=").Append(stateMachine.pendingBehavior);
        }

        if (entityManager.HasBuffer<UtilityActions>(actorEntity))
            line.Append(" options=").Append(entityManager.GetBuffer<UtilityActions>(actorEntity).Length);

        return line.ToString();
    }

    // "-" absent, "off" present but disabled, "ON" enabled.
    private void AppendFlag(System.Text.StringBuilder line, EntityManager entityManager, Entity entity, string label)
    {
        bool present;
        bool enabled;
        switch (label)
        {
            case "cutsceneActor":   present = entityManager.HasComponent<CutsceneActor>(entity);          enabled = present && entityManager.IsComponentEnabled<CutsceneActor>(entity); break;
            case "movement":        present = entityManager.HasComponent<Movement>(entity);               enabled = present && entityManager.IsComponentEnabled<Movement>(entity); break;
            case "dead":            present = entityManager.HasComponent<Dead>(entity);                   enabled = present && entityManager.IsComponentEnabled<Dead>(entity); break;
            case "utilityBrain":    present = entityManager.HasComponent<UtilityBrain>(entity);           enabled = present && entityManager.IsComponentEnabled<UtilityBrain>(entity); break;
            case "actionRequest":   present = entityManager.HasComponent<ActionRequest>(entity);          enabled = present && entityManager.IsComponentEnabled<ActionRequest>(entity); break;
            case "actionInterrupt": present = entityManager.HasComponent<ActionInterruptRequest>(entity); enabled = present && entityManager.IsComponentEnabled<ActionInterruptRequest>(entity); break;
            case "pathRequest":     present = entityManager.HasComponent<PathRequest>(entity);            enabled = present && entityManager.IsComponentEnabled<PathRequest>(entity); break;
            case "dstarFollower":   present = entityManager.HasComponent<DStarLiteFollower>(entity);      enabled = present && entityManager.IsComponentEnabled<DStarLiteFollower>(entity); break;
            case "movementStuck":   present = entityManager.HasComponent<MovementStuck>(entity);          enabled = present && entityManager.IsComponentEnabled<MovementStuck>(entity); break;
            case "moveToMark":      present = entityManager.HasComponent<CutsceneMoveToMark>(entity);     enabled = present && entityManager.IsComponentEnabled<CutsceneMoveToMark>(entity); break;
            case "markIssued":      present = entityManager.HasComponent<CutsceneMarkIssued>(entity);     enabled = present && entityManager.IsComponentEnabled<CutsceneMarkIssued>(entity); break;
            case "cutsceneFacing":  present = entityManager.HasComponent<CutsceneFacing>(entity);         enabled = present && entityManager.IsComponentEnabled<CutsceneFacing>(entity); break;
            case "activeCutscene":  present = entityManager.HasComponent<ActiveCutscene>(entity);         enabled = present && entityManager.IsComponentEnabled<ActiveCutscene>(entity); break;
            case "cutsceneActiveTag": present = entityManager.HasComponent<CutsceneActiveTag>(entity);   enabled = present && entityManager.IsComponentEnabled<CutsceneActiveTag>(entity); break;
            case "activeNarrativeEvent": present = entityManager.HasComponent<ActiveNarrativeEvent>(entity); enabled = present && entityManager.IsComponentEnabled<ActiveNarrativeEvent>(entity); break;
            case "onNarrativeEvent": present = entityManager.HasComponent<OnNarrativeEvent>(entity);      enabled = present && entityManager.IsComponentEnabled<OnNarrativeEvent>(entity); break;
            default: return;
        }

        line.Append(' ').Append(label).Append('=').Append(!present ? "-" : enabled ? "ON" : "off");
    }

    private void OnDestroy()
    {
        DisposeQueries();
    }

    // Teleports rather than paths: the arrival check is a pure XZ-distance test, so it reads a teleport as arrived.
    // Fires once per issued mark, so a player who then walks away is not fought.
    private void TryAutoWalkPlayerToMark(EntityManager entityManager)
    {
        if (playerEntity == Entity.Null || !entityManager.Exists(playerEntity))
        {
            if (playerQuery.IsEmpty) return;
            playerEntity = playerQuery.GetSingletonEntity();
        }

        if (!entityManager.HasComponent<CutsceneMoveToMark>(playerEntity))
        {
            hasAutoWalkedCurrentMark = false;
            return;
        }

        if (!entityManager.IsComponentEnabled<CutsceneMoveToMark>(playerEntity))
        {
            hasAutoWalkedCurrentMark = false;
            return;
        }

        if (hasAutoWalkedCurrentMark) return;

        CutsceneMoveToMark mark = entityManager.GetComponentData<CutsceneMoveToMark>(playerEntity);
        LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(playerEntity);
        localTransform.Position = mark.position;
        entityManager.SetComponentData(playerEntity, localTransform);
        hasAutoWalkedCurrentMark = true;
    }

    private void FireNarrativeEvent(EntityManager entityManager)
    {
        if (narrativeQuery.IsEmpty)
        {
            Debug.LogWarning("CutsceneDebugTrigger: no NarrativeEventTag entity yet — scene still streaming in?");
            return;
        }

        Entity narrativeEntity = narrativeQuery.GetSingletonEntity();

        // A re-fire while running stacks a second ExecuteEventAsync on the shared CutsceneActiveTag,
        // which re-locks player input after the first releases it and leaves input dead. Drop the press.
        if (entityManager.IsComponentEnabled<ActiveNarrativeEvent>(narrativeEntity)
            || entityManager.IsComponentEnabled<OnNarrativeEvent>(narrativeEntity))
        {
            Debug.Log("CutsceneDebugTrigger: narrative event already running — press ignored.");
            return;
        }

        entityManager.SetComponentData(narrativeEntity, new OnNarrativeEvent { eventId = narrativeEventId });
        entityManager.SetComponentEnabled<OnNarrativeEvent>(narrativeEntity, true);
        Debug.Log("CutsceneDebugTrigger: fired narrative event " + narrativeEventId + " on " + narrativeEntity);
    }

    // ToEntityArray rather than GetSingletonEntity so a second CutscenePlay does not throw.
    private void RequestSkipOfActiveCutscene(EntityManager entityManager)
    {
        Unity.Collections.NativeArray<Entity> playEntities =
            cutscenePlayQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        if (playEntities.Length > 0)
            CutsceneApi.RequestSkip(entityManager, playEntities[0]);
        playEntities.Dispose();
    }
}
