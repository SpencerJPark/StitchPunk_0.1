using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder until the Rive artboard exists (CampLoop_System.md CL-P4).
public class ResourceHudUI : MonoBehaviour
{
    private const int LabelCount = 6;
    private const float ShortfallTintSeconds = 1f;

    private static readonly ResourceType[] LabelResourceTypes = new ResourceType[LabelCount]
    {
        ResourceType.Corpse, ResourceType.Wood, ResourceType.ScrapMetal,
        ResourceType.Electricity, ResourceType.RevivalRound, ResourceType.Currency,
    };

    private static readonly string[] LabelNames = new string[LabelCount]
    {
        "bodies-label", "wood-label", "scrap-label", "electricity-label", "rounds-label", "currency-label",
    };

    private static readonly string[] LabelPrefixes = new string[LabelCount]
    {
        "Bodies ", "Wood ", "Scrap ", "Electricity ", "Rounds ", "Currency ",
    };

    [SerializeField] private UIDocument hudDocument;

    private readonly Label[] labels = new Label[LabelCount];
    private readonly int[] lastValues = new int[LabelCount] { -1, -1, -1, -1, -1, -1 };
    private static readonly Color CreamColor = new Color(0.94f, 0.9f, 0.78f);

    private EntityQuery playerQuery;
    private World queryWorld;
    private double lastShownShortfallTime = -1.0;
    private int tintedLabelIndex = -1;
    private float tintEndUnscaledTime;

    private void OnEnable()
    {
        if (hudDocument == null)
            return;

        VisualElement root = hudDocument.rootVisualElement;
        for (int index = 0; index < LabelCount; index++)
        {
            labels[index] = root.Q<Label>(LabelNames[index]);
        }
    }

    private void LateUpdate()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        }

        EntityManager entityManager = world.EntityManager;
        if (playerQuery.IsEmptyIgnoreFilter)
            return;

        Entity playerEntity = playerQuery.GetSingletonEntity();
        if (!entityManager.HasBuffer<ResourceStack>(playerEntity))
            return;

        DynamicBuffer<ResourceStack> stacks = entityManager.GetBuffer<ResourceStack>(playerEntity, true);
        for (int index = 0; index < LabelCount; index++)
        {
            if (labels[index] == null)
                continue;
            int amount = ResourceLedger.Get(in stacks, LabelResourceTypes[index]);
            if (amount == lastValues[index])
                continue;
            lastValues[index] = amount;
            labels[index].text = LabelPrefixes[index] + amount;
        }

        UpdateShortfallTint(entityManager, playerEntity);
    }

    private void UpdateShortfallTint(EntityManager entityManager, Entity playerEntity)
    {
        if (tintedLabelIndex >= 0 && Time.unscaledTime >= tintEndUnscaledTime)
        {
            if (labels[tintedLabelIndex] != null)
                labels[tintedLabelIndex].style.color = CreamColor;
            tintedLabelIndex = -1;
        }

        if (!entityManager.HasComponent<ResourceShortfall>(playerEntity)
            || !entityManager.IsComponentEnabled<ResourceShortfall>(playerEntity))
            return;

        ResourceShortfall shortfall = entityManager.GetComponentData<ResourceShortfall>(playerEntity);
        if (shortfall.stampedTime == lastShownShortfallTime)
            return;
        lastShownShortfallTime = shortfall.stampedTime;

        int shortfallIndex = System.Array.IndexOf(LabelResourceTypes, shortfall.resourceType);
        if (shortfallIndex < 0 || labels[shortfallIndex] == null)
            return;

        if (tintedLabelIndex >= 0 && tintedLabelIndex != shortfallIndex && labels[tintedLabelIndex] != null)
            labels[tintedLabelIndex].style.color = CreamColor;

        tintedLabelIndex = shortfallIndex;
        tintEndUnscaledTime = Time.unscaledTime + ShortfallTintSeconds;
        labels[shortfallIndex].style.color = Color.red;
    }
}
