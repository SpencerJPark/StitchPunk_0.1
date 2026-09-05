using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Throwaway debug trigger for playing a cutscene without a NarrativeEventSO wire-up — press
/// <see cref="key"/> to spawn the same <c>CutsceneRequest</c> signal <c>PlayCutsceneAction</c>
/// would. Used by the G1 checkpoint in DOTSTestScene. Delete once a real trigger covers this
/// cutscene. Project's active input handler is the new Input System only (activeInputHandler: 1
/// in ProjectSettings) — polls Keyboard.current directly rather than KeyCode/legacy Input.
/// </summary>
public class CutsceneDebugTrigger : MonoBehaviour
{
    [SerializeField] private CutsceneAsset cutscene;
    [SerializeField] private Key key = Key.F9;
    [SerializeField] private AnimationToolkitLayer layer = AnimationToolkitLayer.Override;
    [SerializeField] private float speed = 1f;

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current[key].wasPressedThisFrame)
            return;

        if (cutscene == null)
        {
            Debug.LogWarning("CutsceneDebugTrigger: no cutscene assigned.");
            return;
        }

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            Debug.LogError("CutsceneDebugTrigger: No default DOTS world found.");
            return;
        }

        EntityManager entityManager = world.EntityManager;
        Entity signalEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(signalEntity, new CutsceneRequest
        {
            cutsceneKey = cutscene.StableId,
            layerIndex  = (byte)layer,
            speed       = speed,
        });
    }
}
