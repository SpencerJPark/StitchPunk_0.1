using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class QuicksaveInput : MonoBehaviour
{
    private const string QuicksaveId = "quicksave";

    private InputAction quicksaveAction;
    private InputAction quickloadAction;
    private EntityQuery gameDataQuery;
    private bool queryCreated;

    public InputAction QuicksaveAction => quicksaveAction;
    public InputAction QuickloadAction => quickloadAction;

    // InputRebinding only handles one InputActionAsset, so these two are not rebindable until it can take extra actions.
    private void OnEnable()
    {
        if (quicksaveAction == null)
        {
            quicksaveAction = new InputAction("Quicksave", InputActionType.Button, "<Keyboard>/f5");
            quickloadAction = new InputAction("Quickload", InputActionType.Button, "<Keyboard>/f9");
        }

        quicksaveAction.performed += OnQuicksavePerformed;
        quickloadAction.performed += OnQuickloadPerformed;
        quicksaveAction.Enable();
        quickloadAction.Enable();
    }

    private void OnDisable()
    {
        if (quicksaveAction == null)
            return;

        quicksaveAction.performed -= OnQuicksavePerformed;
        quickloadAction.performed -= OnQuickloadPerformed;
        quicksaveAction.Disable();
        quickloadAction.Disable();
    }

    private void OnDestroy()
    {
        quicksaveAction?.Dispose();
        quickloadAction?.Dispose();
        quicksaveAction = null;
        quickloadAction = null;
    }

    private void OnQuicksavePerformed(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f || !TryGetGameDataEntity(out Entity gameDataEntity))
            return;

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        entityManager.SetComponentData(gameDataEntity, new SaveRequest { kind = (int)SaveKind.Quicksave });
        entityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, true);
    }

    private void OnQuickloadPerformed(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f || !TryGetGameDataEntity(out Entity gameDataEntity))
            return;

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        entityManager.SetComponentData(gameDataEntity, new LoadRequest { saveId = new FixedString64Bytes(QuicksaveId) });
        entityManager.SetComponentEnabled<LoadRequest>(gameDataEntity, true);
    }

    private bool TryGetGameDataEntity(out Entity gameDataEntity)
    {
        gameDataEntity = Entity.Null;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return false;

        if (!queryCreated)
        {
            gameDataQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GameDataTag>());
            queryCreated = true;
        }

        if (gameDataQuery.IsEmptyIgnoreFilter)
            return false;

        // The title holds the world: F5 would save the frozen authored scene, F9 would load past BootFlow.
        using (EntityQuery holdQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationHold>()))
        {
            if (!holdQuery.IsEmptyIgnoreFilter)
                return false;
        }

        gameDataEntity = gameDataQuery.GetSingletonEntity();
        return true;
    }
}
