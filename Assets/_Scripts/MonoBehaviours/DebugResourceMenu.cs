using Unity.Entities;
using UnityEngine;

// Throwaway OnGUI resource cheat menu — delete once the real HUD and producers exist.
public class DebugResourceMenu : MonoBehaviour
{
    private const int CapValue = 20;
    private const int DeltaAmount = 10;

    private static readonly ResourceType[] DisplayedResourceTypes =
    {
        ResourceType.Wood,
        ResourceType.ScrapMetal,
        ResourceType.Corpse,
        ResourceType.Electricity,
        ResourceType.Currency,
        ResourceType.RevivalRound,
    };

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private bool worldReady;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<ResourceStack>());
        worldReady = true;
    }

    private void OnGUI()
    {
        if (!worldReady || World.DefaultGameObjectInjectionWorld == null)
            return;

        GUILayout.BeginArea(new Rect(390f, 10f, 380f, 280f), GUI.skin.box);
        GUILayout.Label("Resources (debug)");

        if (!playerQuery.TryGetSingletonEntity<Player>(out Entity playerEntity))
        {
            GUILayout.Label("No player entity with a ResourceStack.");
            GUILayout.EndArea();
            return;
        }
        // The caravan holds the ledger once it links.
        Entity ledgerHolder = ResourceLedger.HolderOf(entityManager, playerEntity);

        foreach (ResourceType resourceType in DisplayedResourceTypes)
        {
            DynamicBuffer<ResourceStack> resourceStacks = entityManager.GetBuffer<ResourceStack>(ledgerHolder, true);
            int currentAmount = ResourceLedger.Get(in resourceStacks, resourceType);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{resourceType}: {currentAmount}", GUILayout.Width(130f));
            if (GUILayout.Button("+10")) QueueChange(ledgerHolder, resourceType, DeltaAmount);
            if (GUILayout.Button("-10")) QueueChange(ledgerHolder, resourceType, -DeltaAmount);
            if (GUILayout.Button($"Set cap {CapValue}")) SetCap(ledgerHolder, resourceType);
            GUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Clear caps"))
            entityManager.GetBuffer<StorageCapacity>(ledgerHolder).Clear();

        GUILayout.EndArea();
    }

    private void QueueChange(Entity ledgerHolder, ResourceType resourceType, int amount)
    {
        entityManager.GetBuffer<ResourceChangeRequest>(ledgerHolder).Add(new ResourceChangeRequest
        {
            resourceType = resourceType,
            changeType   = ResourceChangeType.Add,
            value        = amount,
        });
    }

    private void SetCap(Entity ledgerHolder, ResourceType resourceType)
    {
        DynamicBuffer<StorageCapacity> capacities = entityManager.GetBuffer<StorageCapacity>(ledgerHolder);
        for (int rowIndex = 0; rowIndex < capacities.Length; rowIndex++)
        {
            if (capacities[rowIndex].resourceType != resourceType)
                continue;
            capacities[rowIndex] = new StorageCapacity { resourceType = resourceType, cap = CapValue };
            return;
        }
        capacities.Add(new StorageCapacity { resourceType = resourceType, cap = CapValue });
    }
}
