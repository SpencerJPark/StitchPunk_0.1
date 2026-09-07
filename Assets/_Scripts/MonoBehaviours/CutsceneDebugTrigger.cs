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
    [SerializeField] private Key key = Key.F9;
    [SerializeField] private Key skipKey = Key.F10;

    [Tooltip("Debug convenience only: the instant the toolkit issues the player a mark, teleport them onto it instead of requiring a manual walk. The toolkit itself never auto-paths the Player (G2 §4) — this is purely for solo testing.")]
    [SerializeField] private bool autoWalkPlayerToMark = true;

    private Entity playerEntity = Entity.Null;
    private bool hasAutoWalkedCurrentMark;

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager entityManager = world.EntityManager;

        if (Keyboard.current[key].wasPressedThisFrame)
            FireNarrativeEvent(entityManager);

        if (Keyboard.current[skipKey].wasPressedThisFrame)
            RequestSkipOfActiveCutscene(entityManager);

        if (autoWalkPlayerToMark)
            TryAutoWalkPlayerToMark(entityManager);
    }

    // Teleports rather than paths: CutsceneMoveToMarkSystem's own arrival check is a pure XZ-distance
    // test each frame (A64), so it reads a teleport as "arrived" exactly like a real walk — no need
    // to replicate PathRequest plumbing for a debug convenience. Fires once per issued mark, tracked
    // by the component's own enabled-bit transition, so a player who then walks away isn't fought.
    private void TryAutoWalkPlayerToMark(EntityManager entityManager)
    {
        if (playerEntity == Entity.Null || !entityManager.Exists(playerEntity))
        {
            EntityQuery playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
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
        EntityQuery narrativeQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NarrativeEventTag>());
        if (narrativeQuery.IsEmpty)
        {
            Debug.LogWarning("CutsceneDebugTrigger: no NarrativeEventTag entity yet — scene still streaming in?");
            return;
        }

        Entity narrativeEntity = narrativeQuery.GetSingletonEntity();
        entityManager.SetComponentData(narrativeEntity, new OnNarrativeEvent { eventId = narrativeEventId });
        entityManager.SetComponentEnabled<OnNarrativeEvent>(narrativeEntity, true);
    }

    // One cutscene plays at a time in this checkpoint; ToEntityArray rather than GetSingletonEntity
    // so an unexpected second CutscenePlay skips the first found instead of throwing.
    private void RequestSkipOfActiveCutscene(EntityManager entityManager)
    {
        EntityQuery playQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutscenePlay>());
        Unity.Collections.NativeArray<Entity> playEntities = playQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        if (playEntities.Length > 0)
            CutsceneApi.RequestSkip(entityManager, playEntities[0]);
        playEntities.Dispose();
    }
}
