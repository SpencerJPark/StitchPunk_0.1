using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// The seam between the MonoBehaviour/UI layer and the ECS save request model. Flips SaveRequest /
/// LoadRequest on the GameData singleton; the PersistentSave/Load systems do the actual work.
/// </summary>
public class SaveLoadBridge : MonoBehaviour
{
    private EntityManager entityManager;
    private EntityQuery gameDataQuery;
    private EntityQuery cutsceneQuery;
    private bool worldReady;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        gameDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameDataTag>());
        cutsceneQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NarrativeEventTag>(), ComponentType.ReadOnly<ActiveCutscene>());
        worldReady    = true;
    }

    /// <summary>Enables SaveRequest on the GameData singleton. overwriteSaveId only applies to Manual; empty = new save.</summary>
    public void RequestSave(SaveKind kind, string overwriteSaveId = null)
    {
        if (!TryGetGameDataEntity(out Entity gameDataEntity))
            return;

        FixedString64Bytes overwriteId = string.IsNullOrEmpty(overwriteSaveId) ? default : new FixedString64Bytes(overwriteSaveId);
        entityManager.SetComponentData(gameDataEntity, new SaveRequest { kind = (int)kind, overwriteSaveId = overwriteId });
        entityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, true);
    }

    /// <summary>Enables LoadRequest on the GameData singleton. Null or empty saveId loads the newest save.</summary>
    public void RequestLoad(string saveId = null)
    {
        if (!TryGetGameDataEntity(out Entity gameDataEntity))
            return;

        FixedString64Bytes loadId = string.IsNullOrEmpty(saveId) ? default : new FixedString64Bytes(saveId);
        entityManager.SetComponentData(gameDataEntity, new LoadRequest { saveId = loadId });
        entityManager.SetComponentEnabled<LoadRequest>(gameDataEntity, true);
    }

    [System.Obsolete("Slots are gone; use RequestSave(SaveKind, string).")]
    public void RequestSave(int saveSlot)
    {
        RequestSave(SaveKind.Manual);
    }

    [System.Obsolete("Slots are gone; use RequestLoad(string).")]
    public void RequestLoad(int loadSlot)
    {
        RequestLoad();
    }

    public bool IsCutsceneActive()
    {
        if (!worldReady || World.DefaultGameObjectInjectionWorld == null)
            return false;

        // IsEmpty (not IsEmptyIgnoreFilter) respects enableable bits, so this is true only while ActiveCutscene is enabled.
        return !cutsceneQuery.IsEmpty;
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
