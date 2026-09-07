using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug trigger for the G3 acceptance cutscene — press <see cref="key"/> to fire the narrative
/// event (<see cref="narrativeEventId"/>, run through NarrativeEventManager's PlayCutsceneAction)
/// rather than the raw CutsceneRequest signal, so the narrative path is exercised too. Press
/// <see cref="skipKey"/> mid-run to request a skip on whichever cutscene is currently playing.
/// <see cref="autoWalkPlayerToMark"/> snaps the player onto their mark the instant one is issued —
/// the toolkit never auto-paths the Player by design (G2 §4), so testing solo means either walking
/// there by hand every run or opting into this. Project's active input handler is the new Input
/// System only (activeInputHandler: 1 in ProjectSettings) — polls Keyboard.current directly rather
/// than KeyCode/legacy Input.
/// </summary>
public class CutsceneDebugTrigger : MonoBehaviour
{
    [Tooltip("NarrativeIds.Events constant to fire — the enclosing NarrativeEventSO's PlayCutsceneAction owns the actual cutscene.")]
    [SerializeField] private int narrativeEventId = NarrativeIds.Events.RendezvousTest;
    // NOT any function key. F9 is Unity's own reserved Editor shortcut (Profiling/Profiler/
    // RecordToggle — confirmed against all 1121 shortcuts ShortcutManager.instance registers
    // project-wide) and never reaches the running game at all. F11, tried next, reached Unity fine
    // but as Key.Home, not Key.F11 — a laptop keyboard sharing the F-row with Home/End/PgUp/PgDn
    // (no Fn-lock), confirmed live via a diagnostic that logged every key Unity's Input System
    // actually saw. Backquote/Backslash are dedicated physical keys on every keyboard, with no
    // Editor shortcut and no secondary Fn function to collide with.
    [SerializeField] private Key key = Key.H;
    [SerializeField] private Key skipKey = Key.Backslash;
    [SerializeField] private Key dumpActorStateKey = Key.Semicolon;

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

    // Every EntityQuery this component uses is created exactly once and reused — CreateEntityQuery
    // allocates unmanaged query-matching state that is never reclaimed unless the query is disposed,
    // so building a fresh one every frame (or every keypress) leaks. Rebuilt only if the World itself
    // changed (a domain reload / re-entering Play mode swaps it out from under a surviving instance).
    private void EnsureQueries(World world)
    {
        if (cachedWorld == world) return;

        DisposeQueries();
        cachedWorld = world;
        EntityManager entityManager = world.EntityManager;
        narrativeQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NarrativeEventTag>());
        cutscenePlayQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutscenePlay>());
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());

        // IgnoreComponentEnabledState is the whole point: the question this dump answers is
        // "which gate is still stuck ON after the cutscene", and the default query would hide
        // exactly the entities whose CutsceneActor is stuck enabled behind the ones it is not.
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

    // Every gate that can stop a unit walking, in one line per actor, so "it still can't move after
    // the cutscene" becomes a specific stuck flag instead of a guess. Read it in this order: a unit
    // that cannot move at all has Movement disabled or CutsceneActor still enabled; one whose AI is
    // frozen shows behavior=None phase=Execute forever; one the pathing gave up on shows
    // mode=Stop with agent inactive; one the toolkit never let go of still has Parent/mark set.
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

    // The half that says WHY an actor is still gated: CutsceneEndSystem only releases actors once
    // ActiveCutscene is enabled AND the playback reports complete, so an activeCutscene=ON line here
    // means nobody was ever released — and pausedOnHold=True names the rendezvous hold as the reason
    // the cutscene never finished (the hold hands the player their input back while it waits, which
    // is exactly how the player can be free while every bound NPC is still frozen).
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

    // "-" absent, "off" present-but-disabled, "ON" enabled — the three states that matter, since a
    // component the unit never had and one that is switched off fail queries for different reasons.
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

    // Teleports rather than paths: CutsceneMoveToMarkSystem's own arrival check is a pure XZ-distance
    // test each frame (A64), so it reads a teleport as "arrived" exactly like a real walk — no need
    // to replicate PathRequest plumbing for a debug convenience. Fires once per issued mark, tracked
    // by the component's own enabled-bit transition, so a player who then walks away isn't fought.
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

        // A re-fire while the last one is still running stacks a second ExecuteEventAsync on the
        // first. Both share the one CutsceneActiveTag: the second re-locks player input after the
        // first's finally released it, and nothing releases it again — input is dead for good. Drop
        // the press instead. OnNarrativeEvent covers the frame between signal and pickup.
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

    // One cutscene plays at a time in this checkpoint; ToEntityArray rather than GetSingletonEntity
    // so an unexpected second CutscenePlay skips the first found instead of throwing.
    private void RequestSkipOfActiveCutscene(EntityManager entityManager)
    {
        Unity.Collections.NativeArray<Entity> playEntities =
            cutscenePlayQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        if (playEntities.Length > 0)
            CutsceneApi.RequestSkip(entityManager, playEntities[0]);
        playEntities.Dispose();
    }
}
