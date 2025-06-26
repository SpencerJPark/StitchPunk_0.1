using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance { get; private set; }

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

    Dictionary<CameraType, CinemachineCamera> cams;

    void OnEnable()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // build lookup
        cams = new Dictionary<CameraType, CinemachineCamera>()
        {
            { CameraType.Player, playerCam },
            { CameraType.PlayerZoom, playerZoomCam },
            { CameraType.Vehicle, vehicleCam },
            // { CameraType.Horde,   hordeCam   },
            // { CameraType.MapUI,   mapUICam   },
        };

        // sanity check
        foreach (var kv in cams)
            if (kv.Value == null)
                Debug.LogWarning($"CameraManager: `{kv.Key}` cam is not assigned.");

        // start in player mode
        SwitchCamera(CameraType.Player);
    }

    /// <summary>
    /// Activates the given cam by raising its priority, and demotes all others.
    /// </summary>
    public void SwitchCamera(CameraType type)
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
