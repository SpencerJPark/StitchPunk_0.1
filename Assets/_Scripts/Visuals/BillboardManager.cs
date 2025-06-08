using UnityEngine;
using System;

public class BillboardManager : MonoBehaviour, ILateUpdateObserver
{
    public static event Action<Vector3> OnCameraRotationChanged;
    public static BillboardManager Instance;

    public Camera targetCamera;
    private Vector3 lastEulerAngles;

    void OnEnable()
    {
        LateUpdateManager.RegisterObserver(this);

        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void OnDisable()
    {
        LateUpdateManager.UnregisterObserver(this);
    }

    public void ObservedLateUpdate()
    {
        Vector3 currentEuler = targetCamera.transform.eulerAngles;

        if (!Mathf.Approximately(currentEuler.x, lastEulerAngles.x) ||
            !Mathf.Approximately(currentEuler.y, lastEulerAngles.y))
        {
            lastEulerAngles = currentEuler;
            OnCameraRotationChanged?.Invoke(currentEuler);
        }
    }
}
