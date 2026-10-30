using Unity.Entities;
using UnityEngine;

// Throwaway OnGUI resource cheat menu — delete once the real HUD and producers exist.
public class DebugResourceMenu : MonoBehaviour
{
    private const int CapValue = 20;
    private const int DeltaAmount = 10;

    private static readonly Rect MenuAreaRect = new Rect(390f, 10f, 380f, 280f);

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
    private DebugMenuVirtualCursorInput virtualCursorInput;
    private bool worldReady;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<ResourceStack>());
        virtualCursorInput = new DebugMenuVirtualCursorInput(entityManager, MenuAreaRect, IsMenuShown);
        worldReady = true;
    }

    private void OnEnable()  => virtualCursorInput?.Register();
    private void OnDisable() => virtualCursorInput?.Unregister();

    private void Update() => DebugMenuVisibility.PollToggleAction();

    private bool IsMenuShown()
    {
        return worldReady && DebugMenuVisibility.AreDebugMenusVisible;
    }

    private void OnGUI()
    {
        if (!IsMenuShown() || World.DefaultGameObjectInjectionWorld == null)
            return;

        virtualCursorInput.BeginMenuGui();
        GUILayout.BeginArea(MenuAreaRect, GUI.skin.box);
        GUILayout.Label($"Resources (debug) — {DebugMenuVisibility.ToggleBindingDisplayText} hides");

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
            if (virtualCursorInput.Button("+10")) QueueChange(ledgerHolder, resourceType, DeltaAmount);
            if (virtualCursorInput.Button("-10")) QueueChange(ledgerHolder, resourceType, -DeltaAmount);
            if (virtualCursorInput.Button($"Set cap {CapValue}")) SetCap(ledgerHolder, resourceType);
            GUILayout.EndHorizontal();
        }

        if (virtualCursorInput.Button("Clear caps"))
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
