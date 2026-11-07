using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class StrategicViewToggle : MonoBehaviour
{
    private const string OnFootActionPath = "Player/ToggleStrategicView";
    private const string VehicleActionPath = "Vehicle/ToggleStrategicView";

    [SerializeField] private StrategicZoomProfile zoomProfile;

    private InputActionAsset resolvedFromAsset;
    private InputAction onFootAction;
    private InputAction vehicleAction;
    private float lastNotifiedEasedMix = -1f;

    public event Action<float> MixChanged;

    public float EasedMix01 { get; private set; }

    private void OnDisable()
    {
        UnsubscribeActions();
        resolvedFromAsset = null;
        StrategicZoomState.ForceToClose();
    }

    private void Update()
    {
        if (zoomProfile == null)
            zoomProfile = StrategicZoomProfile.CreateDefault();

        ResolveActionsFromRegisteredAsset();

        if (PlayerInputManager.Instance != null && PlayerInputManager.Instance.IsModalUiOpen)
            StrategicZoomState.ForceToClose();

        float targetMix = StrategicZoomState.IsStrategicTarget ? 1f : 0f;
        // Unscaled: the strategic view may speed the game up, which must not speed the camera blend.
        StrategicZoomState.Mix01 = StrategicZoomState.StepMix(
            StrategicZoomState.Mix01, targetMix, Time.unscaledDeltaTime, zoomProfile.transitionSeconds);

        EasedMix01 = StrategicZoomState.ApplyEase(StrategicZoomState.Mix01, zoomProfile.transitionEase);
        if (!Mathf.Approximately(EasedMix01, lastNotifiedEasedMix))
        {
            lastNotifiedEasedMix = EasedMix01;
            MixChanged?.Invoke(EasedMix01);
        }
    }

    // PlayerInputManager registers the scene's asset in Awake, after this component may already exist.
    private void ResolveActionsFromRegisteredAsset()
    {
        InputActionAsset registeredAsset = InputRebinding.Asset;
        if (registeredAsset == resolvedFromAsset)
            return;

        UnsubscribeActions();
        resolvedFromAsset = registeredAsset;
        if (registeredAsset == null)
            return;

        onFootAction = registeredAsset.FindAction(OnFootActionPath);
        vehicleAction = registeredAsset.FindAction(VehicleActionPath);
        if (onFootAction != null)
            onFootAction.performed += OnTogglePerformed;
        if (vehicleAction != null)
            vehicleAction.performed += OnTogglePerformed;
    }

    private void UnsubscribeActions()
    {
        if (onFootAction != null)
            onFootAction.performed -= OnTogglePerformed;
        if (vehicleAction != null)
            vehicleAction.performed -= OnTogglePerformed;
        onFootAction = null;
        vehicleAction = null;
    }

    // The registered asset has every map; only the active map's copy of the action may flip the view.
    private void OnTogglePerformed(InputAction.CallbackContext context)
    {
        if (!context.action.actionMap.enabled)
            return;
        if (PlayerInputManager.Instance != null && PlayerInputManager.Instance.IsModalUiOpen)
            return;
        StrategicZoomState.SetStrategicTarget(!StrategicZoomState.IsStrategicTarget);
    }
}
