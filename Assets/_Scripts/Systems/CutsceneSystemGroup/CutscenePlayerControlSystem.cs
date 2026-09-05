using Unity.Entities;

/// <summary>
/// Drives <see cref="CutsceneActiveTag"/> from <see cref="ActiveCutscene"/>. NarrativeEventManager
/// independently enables the same tag for a <c>blockPlayerInput</c> narrative event, so the two
/// writers must OR together: this system only ENABLES while a cutscene is active, and only
/// DISABLES when no narrative event is active either — never stomps a narrative-event-driven
/// enable out from under it. (G2 adds the rendezvous exception, where the player keeps control
/// while a hold waits on their move-to mark.)
/// </summary>
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneEndSystem))]
public partial struct CutscenePlayerControlSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity))
            return;

        if (SystemAPI.IsComponentEnabled<ActiveCutscene>(narrativeEntity))
        {
            SystemAPI.SetComponentEnabled<CutsceneActiveTag>(narrativeEntity, true);
            return;
        }

        if (!SystemAPI.IsComponentEnabled<ActiveNarrativeEvent>(narrativeEntity))
            SystemAPI.SetComponentEnabled<CutsceneActiveTag>(narrativeEntity, false);
    }
}
