using UnityEngine;
using Unity.Entities;
using Rive.Components;

public class UnitSelectionBoxUI : MonoBehaviour, IUpdateObserver
{
    [SerializeField] private RiveWidget riveWidget;

    private RivePropertyCache selectionRive;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private Entity playerEntity;
    private bool hasPlayer;
    private bool wasEnabled = true; // start true so the first update always forces a sync

    private const float LINE_WIDTH       = 100f;
    private const float MUSIC_NOTE_WIDTH = 90f;
    private const float MUSIC_NOTE_EXTRA = 22f;

    private int lastGroupIndex = -1; // -1 forces a sync on the first frame

    private void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery   = new Unity.Entities.EntityQueryBuilder(Unity.Collections.Allocator.Temp)
            .WithAll<Player>()
            .WithPresent<SelectionBoxData>()
            .Build(entityManager);

        RiveUtils.OnLoaded(riveWidget, OnRiveLoaded);
    }

    private void OnRiveLoaded()
    {
        selectionRive = RiveUtils.GetPropertyCache(riveWidget);
        if (selectionRive == null)
        {
            Debug.LogError("UnitSelectionBoxUI: RiveWidget has no ViewModelInstance. " +
                           "Set the widget's Binding Mode to 'Auto Bind Default' in the inspector.");
            return;
        }
        selectionRive.SetNumber("Visable", 0f);
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (selectionRive == null) return;

        bool isEnabled = EnsurePlayerEntity() &&
                         entityManager.IsComponentEnabled<SelectionBoxData>(playerEntity);

        if (isEnabled != wasEnabled)
        {
            selectionRive.SetNumber("Visable", isEnabled ? 1f : 0f);
            wasEnabled = isEnabled;
        }

        // Keep box color in sync with the active assignment group regardless of visibility.
        if (hasPlayer && entityManager.HasComponent<PlayerMinionGroupsData>(playerEntity))
        {
            int groupIndex = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity).assignmentGroupIndex;
            if (groupIndex != lastGroupIndex)
            {
                selectionRive.SetColor("SelectionColor", GroupIndexToColor(groupIndex));
                lastGroupIndex = groupIndex;
            }
        }

        if (!isEnabled) return;

        SelectionBoxData data = entityManager.GetComponentData<SelectionBoxData>(playerEntity);
        UpdateVisual(data);
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

    private void UpdateVisual(SelectionBoxData data)
    {
        selectionRive.SetNumber("PositionX",           data.PositionX);
        selectionRive.SetNumber("PositionY",           data.PositionY);
        selectionRive.SetNumber("SelectionAreaWidth",  data.Width);
        selectionRive.SetNumber("SelectionAreaHeight", data.Height);
        selectionRive.SetNumber("LineAmount",          Mathf.Floor(data.Height / LINE_WIDTH));
        selectionRive.SetNumber("MusicNoteAmount",     CalculateMusicNotes(data.Width, data.Height));
    }

    private static float CalculateMusicNotes(float width, float height)
    {
        float minSpace = MUSIC_NOTE_WIDTH + MUSIC_NOTE_EXTRA;
        if (width < minSpace) return 0f;
        float lines = Mathf.Floor(height / LINE_WIDTH);
        return Mathf.Floor((width - MUSIC_NOTE_EXTRA) / MUSIC_NOTE_WIDTH) * lines;
    }

    // Maps a group assignment index to its display color.
    // NOTE: The Rive artboard must expose a color property named "SelectionColor".
    private static Color GroupIndexToColor(int groupIndex)
    {
        return groupIndex switch
        {
            1 => new Color(1f,    0.25f, 0.25f), // red
            2 => new Color(0.25f, 0.5f,  1f),    // blue
            3 => new Color(0.25f, 1f,    0.45f), // green
            4 => new Color(1f,    0.9f,  0.15f), // yellow
            _ => Color.white,                    // 0 = no group
        };
    }
}
