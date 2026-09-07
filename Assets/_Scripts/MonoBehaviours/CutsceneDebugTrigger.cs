using DotsAnimationToolkit;
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

    [Tooltip("Debug convenience only: the instant the toolkit issues the player a mark, teleport them onto it instead of requiring a manual walk. The toolkit itself never auto-paths the Player (G2 §4) — this is purely for solo testing.")]
    [SerializeField] private bool autoWalkPlayerToMark = true;

    private World cachedWorld;
    private EntityQuery narrativeQuery;
    private EntityQuery cutscenePlayQuery;
    private EntityQuery playerQuery;

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
    }

    private void DisposeQueries()
    {
        if (cachedWorld == null || !cachedWorld.IsCreated) return;
        if (narrativeQuery != default) narrativeQuery.Dispose();
        if (cutscenePlayQuery != default) cutscenePlayQuery.Dispose();
        if (playerQuery != default) playerQuery.Dispose();
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
