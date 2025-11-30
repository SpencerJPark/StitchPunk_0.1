using UnityEngine;
using Unity.Cinemachine;
using System.Collections.Generic;

public class CameraManager : Singleton<CameraManager>
{
    [Header("Virtual Cameras")]
    [SerializeField] private CinemachineCamera playerCam;
    [SerializeField] private CinemachineCamera playerZoomCam;
    [SerializeField] private CinemachineCamera vehicleCam;
    [SerializeField] private CinemachineCamera controllUnitsCam;
    [SerializeField] private CinemachineCamera mapCam;

    private Dictionary<CinemachineCameraType, CinemachineCamera> cameraMap;
    private CinemachineCameraType currentCamera;
    
    // Create a switch camera event that all cameras controllers hook to which triggers a recentering their focus

    private const int ACTIVE_PRIORITY = 20;
    private const int INACTIVE_PRIORITY = 1;

    private void Awake()
    {
        cameraMap = new Dictionary<CinemachineCameraType, CinemachineCamera>
        {
            { CinemachineCameraType.Player,      playerCam },
            { CinemachineCameraType.PlayerZoom,  playerZoomCam },
            { CinemachineCameraType.Vehicle,     vehicleCam },
            { CinemachineCameraType.ControlUnits, controllUnitsCam },
            { CinemachineCameraType.Map,         mapCam },
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
    }

    public CinemachineCameraType GetCurrentCamera()
    {
        return currentCamera;
    }
}