using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using Data;

public class CameraManager : PersistentSingleton<CameraManager>
{
    [Header("Cams (assign in inspector)")]
    [SerializeField] CinemachineCamera playerCam;
    [SerializeField] CinemachineCamera playerZoomCam;
    [SerializeField] CinemachineCamera vehicleCam;
    //[SerializeField] CinemachineCamera hordeCam;
    //[SerializeField] CinemachineCamera mapUICam;

    [Header("Priorities")]
    [Tooltip("Priority for the active camera")]
    [SerializeField] int activePriority = 20;

    [Tooltip("Priority for all inactive cameras")]
    [SerializeField] int inactivePriority = 10;

    Dictionary<CameraTypeEnum, CinemachineCamera> cams;

    void OnEnable()
    {
        // build lookup
        cams = new Dictionary<CameraTypeEnum, CinemachineCamera>()
        {
            { CameraTypeEnum.Player, playerCam },
            { CameraTypeEnum.PlayerZoom, playerZoomCam },
            { CameraTypeEnum.Vehicle, vehicleCam },
            // { CameraTypeEnum.Horde,   hordeCam   },
            // { CameraTypeEnum.MapUI,   mapUICam   },
        };

        // sanity check
        foreach (var kv in cams)
            if (kv.Value == null)
                Debug.LogWarning($"CameraManager: `{kv.Key}` cam is not assigned.");

        // start in player mode
        SwitchCamera(CameraTypeEnum.Player);
    }

    /// <summary>
    /// Activates the given cam by raising its priority, and demotes all others.
    /// </summary>
    public void SwitchCamera(CameraTypeEnum type)
    {
        foreach (var kv in cams)
        {
            kv.Value.Priority = (kv.Key == type) ? activePriority : inactivePriority;
        }
    }

    /// <summary>
    /// Call this when you enter a vehicle; swaps the vehicle cam's Follow/LookAt.
    /// </summary>
    public void SetVehicleTarget(Transform followTarget, Transform lookAtTarget = null)
    {
        if (vehicleCam == null) return;
        vehicleCam.Follow = followTarget;
        // if you want the camera to look at something specific:
        vehicleCam.LookAt = lookAtTarget ?? followTarget;
    }
}
