using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder until the Rive squad HUD (MinionGroupHUD reads hordes and no longer matches squads — CampLoop CL-A13).
public class SquadHudUI : MonoBehaviour
{
    private const int SquadCount = 4;

    private static readonly string[] SquadNames = new string[SquadCount] { "Red", "Blue", "Green", "Yellow" };

    private static readonly Color[] SquadColors = new Color[SquadCount]
    {
        new Color(0.85f, 0.25f, 0.25f),
        new Color(0.3f, 0.5f, 0.95f),
        new Color(0.3f, 0.8f, 0.4f),
        new Color(0.95f, 0.85f, 0.3f),
    };

    private static readonly Color CreamColor = new Color(0.94f, 0.9f, 0.78f);
    private const float InactiveAlpha = 0.35f;
    private const float ActiveBorderWidth = 2f;

    [SerializeField] private UIDocument hudDocument;

    private readonly VisualElement[] chips = new VisualElement[SquadCount];
    private readonly Label[] labels = new Label[SquadCount];
    private readonly int[] lastCounts = new int[SquadCount] { -1, -1, -1, -1 };
    private readonly int[] counts = new int[SquadCount];
    private int lastUnlockedCount = -1;
    private int lastActiveSquad = -1;

    private EntityQuery playerQuery;
    private EntityQuery squadMinionQuery;
    private World queryWorld;

    private void OnEnable()
    {
        ResolveChips();
    }

    // The UIDocument may build its tree after this OnEnable, so LateUpdate retries until it exists.
    private void ResolveChips()
    {
        if (hudDocument == null || hudDocument.rootVisualElement == null)
            return;

        VisualElement root = hudDocument.rootVisualElement;
        for (int index = 0; index < SquadCount; index++)
        {
            chips[index] = root.Q<VisualElement>("squad-" + (index + 1));
            labels[index] = root.Q<Label>("squad-" + (index + 1) + "-label");
        }

        lastUnlockedCount = -1;
        lastActiveSquad = -1;
        for (int index = 0; index < SquadCount; index++)
            lastCounts[index] = -1;
    }

    private void LateUpdate()
    {
        if (chips[0] == null)
            ResolveChips();
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
            squadMinionQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Minion>(), ComponentType.ReadOnly<SquadMember>());
        }

        EntityManager entityManager = world.EntityManager;
        if (playerQuery.IsEmptyIgnoreFilter)
            return;

        Entity playerEntity = playerQuery.GetSingletonEntity();
        if (!entityManager.HasComponent<PlayerMinionGroupsData>(playerEntity))
            return;

        PlayerMinionGroupsData groups = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);

        for (int index = 0; index < SquadCount; index++)
            counts[index] = 0;

        // Minion is enableable: the query skips disabled ones, so only player minions are counted.
        NativeArray<SquadMember> members = squadMinionQuery.ToComponentDataArray<SquadMember>(Allocator.Temp);
        for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            int squadIndex = members[memberIndex].squadIndex;
            if (squadIndex >= 1 && squadIndex <= SquadCount)
                counts[squadIndex - 1]++;
        }
        members.Dispose();

        bool unlockedChanged = groups.unlockedGroupCount != lastUnlockedCount;
        bool activeChanged = groups.assignmentGroupIndex != lastActiveSquad;
        lastUnlockedCount = groups.unlockedGroupCount;
        lastActiveSquad = groups.assignmentGroupIndex;

        for (int index = 0; index < SquadCount; index++)
        {
            if (chips[index] == null || labels[index] == null)
                continue;

            if (unlockedChanged)
                chips[index].style.display = index < groups.unlockedGroupCount ? DisplayStyle.Flex : DisplayStyle.None;

            if (counts[index] != lastCounts[index])
            {
                lastCounts[index] = counts[index];
                labels[index].text = SquadNames[index] + " " + counts[index];
            }

            if (unlockedChanged || activeChanged)
                ApplyActiveStyle(chips[index], index, index + 1 == groups.assignmentGroupIndex);
        }
    }

    private static void ApplyActiveStyle(VisualElement chip, int squadArrayIndex, bool isActive)
    {
        Color squadColor = SquadColors[squadArrayIndex];
        squadColor.a = isActive ? 1f : InactiveAlpha;
        chip.style.backgroundColor = squadColor;

        float borderWidth = isActive ? ActiveBorderWidth : 0f;
        chip.style.borderTopWidth = borderWidth;
        chip.style.borderBottomWidth = borderWidth;
        chip.style.borderLeftWidth = borderWidth;
        chip.style.borderRightWidth = borderWidth;
        chip.style.borderTopColor = CreamColor;
        chip.style.borderBottomColor = CreamColor;
        chip.style.borderLeftColor = CreamColor;
        chip.style.borderRightColor = CreamColor;
    }
}
