using UnityEngine;
using Unity.Entities;
using Rive.Components;

// Reads each player group horde's member count and drives a Rive artboard that
// displays up to 4 group slots. Only unlocked slots are shown.
//
// Required Rive artboard properties (numbers unless noted):
//   GroupCount        — how many slots to display (0–4)
//   ActiveGroup       — which slot is highlighted as the assignment target (0 = none)
//   Group1Count … Group4Count — member count per slot
//   Group1Color … Group4Color (colors) — slot accent color
//
// Health display is intentionally omitted until a MaxHealth component is added
// to the Health struct so a meaningful ratio can be computed.
public class MinionGroupHUD : MonoBehaviour, IUpdateObserver
{
    [SerializeField] private RiveWidget riveWidget;

    private RivePropertyCache hudRive;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private Entity playerEntity;
    private bool hasPlayer;

    // Track previous values to avoid redundant Rive calls.
    private int lastGroupCount  = -1;
    private int lastActiveGroup = -1;
    private int[] lastMemberCounts = new int[4] { -1, -1, -1, -1 };

    private static readonly Color[] GroupColors = new Color[5]
    {
        Color.white,                     // 0 — unused
        new Color(1f,    0.25f, 0.25f),  // 1 — red
        new Color(0.25f, 0.5f,  1f),     // 2 — blue
        new Color(0.25f, 1f,    0.45f),  // 3 — green
        new Color(1f,    0.9f,  0.15f),  // 4 — yellow
    };

    private void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerMinionGroupsData>());

        RiveUtils.OnLoaded(riveWidget, OnRiveLoaded);
    }

    private void OnRiveLoaded()
    {
        hudRive = RiveUtils.GetPropertyCache(riveWidget);
        if (hudRive == null)
        {
            Debug.LogError("MinionGroupHUD: RiveWidget has no ViewModelInstance. " +
                           "Set the widget's Binding Mode to 'Auto Bind Default' in the inspector.");
            return;
        }

        // Push group colors once — they never change at runtime.
        for (int i = 1; i <= 4; i++)
            hudRive.SetColor("Group" + i + "Color", GroupColors[i]);

        hudRive.SetNumber("GroupCount",  0f);
        hudRive.SetNumber("ActiveGroup", 0f);
        for (int i = 1; i <= 4; i++)
            hudRive.SetNumber("Group" + i + "Count", 0f);
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (hudRive == null) return;
        if (!EnsurePlayerEntity()) return;

        PlayerMinionGroupsData groupData = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);

        int unlocked    = groupData.unlockedGroupCount;
        int activeGroup = groupData.assignmentGroupIndex;

        if (unlocked != lastGroupCount)
        {
            hudRive.SetNumber("GroupCount", unlocked);
            lastGroupCount = unlocked;
        }

        if (activeGroup != lastActiveGroup)
        {
            hudRive.SetNumber("ActiveGroup", activeGroup);
            lastActiveGroup = activeGroup;
        }

        // Read member counts from each horde's buffer.
        DynamicBuffer<PlayerHordeSlot> slots = entityManager.GetBuffer<PlayerHordeSlot>(playerEntity, isReadOnly: true);
        for (int i = 0; i < slots.Length && i < 4; i++)
        {
            Entity hordeEntity = slots[i].hordeEntity;
            int count = 0;
            if (entityManager.Exists(hordeEntity) && entityManager.HasBuffer<HordeMemberBuffer>(hordeEntity))
                count = entityManager.GetBuffer<HordeMemberBuffer>(hordeEntity, isReadOnly: true).Length;

            if (count != lastMemberCounts[i])
            {
                hudRive.SetNumber("Group" + (i + 1) + "Count", count);
                lastMemberCounts[i] = count;
            }
        }
    }

    private bool EnsurePlayerEntity()
    {
        if (hasPlayer)
        {
            if (entityManager.Exists(playerEntity)) return true;
            hasPlayer = false;
        }
        if (playerQuery.IsEmpty) return false;
        playerEntity = playerQuery.GetSingletonEntity();
        hasPlayer    = true;
        return true;
    }
}
