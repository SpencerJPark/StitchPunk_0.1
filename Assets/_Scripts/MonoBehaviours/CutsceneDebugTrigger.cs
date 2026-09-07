using DotsAnimationToolkit;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug trigger for the G3 acceptance cutscene — press <see cref="key"/> to fire the narrative
/// event (<see cref="narrativeEventId"/>, run through NarrativeEventManager's PlayCutsceneAction)
/// rather than the raw CutsceneRequest signal, so the narrative path is exercised too. Press
/// <see cref="skipKey"/> mid-run to request a skip on whichever cutscene is currently playing.
/// Project's active input handler is the new Input System only (activeInputHandler: 1 in
/// ProjectSettings) — polls Keyboard.current directly rather than KeyCode/legacy Input.
/// </summary>
public class CutsceneDebugTrigger : MonoBehaviour
{
    [Tooltip("NarrativeIds.Events constant to fire — the enclosing NarrativeEventSO's PlayCutsceneAction owns the actual cutscene.")]
    [SerializeField] private int narrativeEventId = NarrativeIds.Events.RendezvousTest;
    [SerializeField] private Key key = Key.F9;
    [SerializeField] private Key skipKey = Key.F10;

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
