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
    private EntityQuery inputQuery;
    private Entity inputEntity;
    private bool hasInput;

    private float currentOffset;
    private float targetOffset;
    private float offsetVelocity;
    private float lastZoomInput;
    private ActionMaps lastActionMap;

    

    private void Awake()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        inputQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerInputData>());
        
        currentOffset = defaultOffset;
        targetOffset = defaultOffset;

        if (cameraOffset == null)
        {
            Debug.LogError("CameraZoomController: ControlUnits camera not assigned!");
        }
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
        if (Conditions(out var inputData)) return;
        
        CalculateOffset(inputData);

        // Smooth zoom to target
        currentOffset = Mathf.SmoothDamp(currentOffset, targetOffset, ref offsetVelocity, smoothTime);

        ApplyOffsetToCamera(currentOffset);
    }

    private void CalculateOffset(PlayerInputData inputData)
    {
        float rawZoom = inputData.zoomInput;
        if (Mathf.Abs(rawZoom) > 0.01f && Mathf.Abs(lastZoomInput) < 0.01f)
        {
            float direction = -Mathf.Sign(rawZoom);
            targetOffset -= direction * zoomStep;
            targetOffset = Mathf.Clamp(targetOffset, farthestOffset, closestOffset);
        }
        lastZoomInput = rawZoom;
    }

    private bool Conditions(out PlayerInputData inputData)
    {
        inputData = new PlayerInputData();
    
        if (cameraOffset == null)
            return true;

        if (!EnsureInputEntity())
            return true;

        inputData = entityManager.GetComponentData<PlayerInputData>(inputEntity);

        // Track map changes FIRST
        if (inputData.activeActionMap != lastActionMap)
        {
            lastActionMap = inputData.activeActionMap;
            ResetOffset();
        }

        if (inputData.activeActionMap != ActionMaps.ControlUnits)
            return true;

        return false;
    }

    private bool EnsureInputEntity()
    {
        if (hasInput)
        {
            if (entityManager.Exists(inputEntity))
                return true;

            hasInput = false;
        }

        if (inputQuery.IsEmpty)
            return false;

        inputEntity = inputQuery.GetSingletonEntity();
        hasInput = true;
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