using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : Singleton<CameraManager>
{
    [Header("Virtual Cameras")]
    [SerializeField] private CinemachineCamera playerCam;
    [SerializeField] private CinemachineCamera playerZoomCam;
    [SerializeField] private CinemachineCamera vehicleCam;
    [SerializeField] private CinemachineCamera controllUnitsCam;
    [SerializeField] private CinemachineCamera mapCam;
    [SerializeField] private CinemachineCamera cinematicCam;

    [Header("Cinematic")]
    [Tooltip("Empty GameObject that the cinematic camera follows/looks at. Move this to frame each shot.")]
    [SerializeField] private Transform cinematicTarget;

    /// <summary>Fired whenever the active camera changes. Subscribe to recenter follow targets.</summary>
    public static event Action<CinemachineCameraType> OnCameraSwitch;

    private Dictionary<CinemachineCameraType, CinemachineCamera> cameraMap;
    private CinemachineCameraType currentCamera;
    private CinemachineCameraType _preCinematicCamera;

    private const int ACTIVE_PRIORITY = 20;
    private const int INACTIVE_PRIORITY = 1;

    private void Start()
    {
        cameraMap = new Dictionary<CinemachineCameraType, CinemachineCamera>
        {
            { CinemachineCameraType.Player,       playerCam },
            { CinemachineCameraType.PlayerZoom,   playerZoomCam },
            { CinemachineCameraType.Vehicle,      vehicleCam },
            { CinemachineCameraType.ControlUnits, controllUnitsCam },
            { CinemachineCameraType.Map,          mapCam },
            { CinemachineCameraType.Cinematic,    cinematicCam },
        };

        // Ensure a default active camera
        SwitchCamera(CinemachineCameraType.Player);
    }

    public void SwitchCamera(CinemachineCameraType type)
    {
        currentCamera = type;

        foreach (var pair in cameraMap)
        {
            if (pair.Value == null)
                continue;

            pair.Value.Priority = (pair.Key == type)
                ? ACTIVE_PRIORITY
                : INACTIVE_PRIORITY;
        }

        OnCameraSwitch?.Invoke(type);
    }

    /// <summary>
    /// Moves the cinematic target to worldPosition and activates the cinematic camera.
    /// Call ExitCinematic() to return to the previous camera.
    /// </summary>
    public void EnterCinematic(Vector3 worldPosition)
    {
        _preCinematicCamera = currentCamera;
        if (cinematicTarget != null)
            cinematicTarget.position = worldPosition;
        SwitchCamera(CinemachineCameraType.Cinematic);
    }

    /// <summary>Restores the camera that was active before EnterCinematic was called.</summary>
    public void ExitCinematic()
    {
        SwitchCamera(_preCinematicCamera);
    }

    public CinemachineCameraType GetCurrentCamera()
    {
        return currentCamera;
    }
}