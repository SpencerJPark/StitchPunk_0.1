using Unity.Entities;
using UnityEngine;

/// <summary>
/// The seam between the MonoBehaviour/UI layer and the ECS save request model. Holds no UI of
/// its own — callers (e.g. <see cref="GameMenuManager"/>) invoke RequestSave / RequestLoad, which
/// flip SaveRequest / LoadRequest on the GameData singleton; the PersistentSave/Load systems do
/// the actual work.
/// </summary>
public class SaveLoadBridge : MonoBehaviour
{
    private EntityManager entityManager;
    private EntityQuery gameDataQuery;
    private bool worldReady;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        gameDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameDataTag>());
        worldReady    = true;
    }

    /// <summary>Enables SaveRequest on the GameData singleton for the given slot.</summary>
    public void RequestSave(int saveSlot)
    {
        if (!TryGetGameDataEntity(out Entity gameDataEntity))
            return;

        entityManager.SetComponentData(gameDataEntity, new SaveRequest { slot = saveSlot });
        entityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, true);
    }

    /// <summary>Enables LoadRequest on the GameData singleton for the given slot.</summary>
    public void RequestLoad(int loadSlot)
    {
        if (!TryGetGameDataEntity(out Entity gameDataEntity))
            return;

        entityManager.SetComponentData(gameDataEntity, new LoadRequest { slot = loadSlot });
        entityManager.SetComponentEnabled<LoadRequest>(gameDataEntity, true);
    }

    private bool TryGetGameDataEntity(out Entity gameDataEntity)
    {
        gameDataEntity = Entity.Null;

        // No-op cleanly before the game subscene has streamed in the GameData singleton.
        if (!worldReady || World.DefaultGameObjectInjectionWorld == null || gameDataQuery.IsEmptyIgnoreFilter)
            return false;

        gameDataEntity = gameDataQuery.GetSingletonEntity();
        return true;
    }
}
