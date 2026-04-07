using Unity.Entities;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraZoomController : MonoBehaviour, IUpdateObserver
{
    [FormerlySerializedAs("controlUnitsCam")]
    [Header("References")]
    [SerializeField] private CinemachineCameraOffset cameraOffset;

    [Header("Zoom Settings")]
    [SerializeField] private float defaultOffset = -16.5f;
    [SerializeField] private float closestOffset = 1f;
    [SerializeField] private float farthestOffset = -50f;
    [SerializeField] private float zoomStep = 6f;
    [SerializeField] private float smoothTime = 0.15f;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private Entity playerEntity;
    private bool hasPlayer;

    private float currentOffset;
    private float targetOffset;
    private float offsetVelocity;
    private float lastZoomInput;
    private ActionMaps lastActionMap;

    private void Awake()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerActionMap>());

        currentOffset = defaultOffset;
        targetOffset = defaultOffset;

        if (cameraOffset == null)
            Debug.LogError("CameraZoomController: CameraOffset not assigned!");
    }

    private void OnEnable()
    {
        UpdateManager.RegisterObserver(this);
        SetOffsetInstant(defaultOffset);
    }

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
    }

    public void ObservedUpdate()
    {
        if (!EnsurePlayerEntity()) return;
        if (cameraOffset == null) return;

        PlayerActionMap actionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity);

        if (actionMap.activeActionMap != lastActionMap)
        {
            lastActionMap = actionMap.activeActionMap;
            SetOffsetInstant(defaultOffset);
        }

        if (actionMap.activeActionMap != ActionMaps.ControlUnits) return;

        float rawZoom = 0f;
        if (entityManager.IsComponentEnabled<ZoomPlayerInput>(playerEntity))
            rawZoom = entityManager.GetComponentData<ZoomPlayerInput>(playerEntity).zoomInput;

        CalculateOffset(rawZoom);

        currentOffset = Mathf.SmoothDamp(currentOffset, targetOffset, ref offsetVelocity, smoothTime);
        ApplyOffsetToCamera(currentOffset);
    }

    private void CalculateOffset(float rawZoom)
    {
        if (Mathf.Abs(rawZoom) > 0.01f && Mathf.Abs(lastZoomInput) < 0.01f)
        {
            float direction = -Mathf.Sign(rawZoom);
            targetOffset -= direction * zoomStep;
            targetOffset = Mathf.Clamp(targetOffset, farthestOffset, closestOffset);
        }
        lastZoomInput = rawZoom;
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
        hasPlayer = true;
        return true;
    }

    private void ApplyOffsetToCamera(float offset)
    {
        cameraOffset.Offset = new Vector3(0, 0, offset);
    }

    public void ResetOffset()
    {
        targetOffset = defaultOffset;
        lastZoomInput = 0f;
    }

    public void SetOffsetInstant(float offset)
    {
        offset = Mathf.Clamp(offset, farthestOffset, closestOffset);
        currentOffset = offset;
        targetOffset = offset;
        offsetVelocity = 0f;
        lastZoomInput = 0f;
        ApplyOffsetToCamera(currentOffset);
    }
}
