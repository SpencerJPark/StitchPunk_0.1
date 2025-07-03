using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Dependencies")]
    [Tooltip("Will be set dynamically when someone enters")]
    protected InputProviderBase input;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform driverSeatAnchor;


    [Header("References")]
    [Tooltip("Drag in your door zones")]
    [SerializeField] private List<GameObject> doors = new List<GameObject>();


    [Header("Driver")]
    public GameObject driverObject = null;
    private CharacterControllerBase driverCharacterController;
    private FacingDirectionBase driverFacingController;
    [SerializeField] private FacingOffsetProfile driverOffsetProfile;


    [Header("Horse")]
    [SerializeField] private RiveAnimator animator;
    [SerializeField] private FacingDirectionBase horseFacingController;
    [SerializeField] private Transform horseQuad;
    [SerializeField] private FacingOffsetProfile horseOffsetProfile;


    [Header("Vehicle Profile")]
    [SerializeField] private VehicleProfiles vehicleProfiles;


    [Header("Wheels")]
    [SerializeField] private WheelData[] wheels;
    public WheelData[] Wheels => wheels;
    [Serializable]
    public struct WheelData
    {
        public Transform mesh;
        public bool isLeftSide;
        public float radius;
        [HideInInspector]
        public float targetRPM;
    }


    // Runtime state
    private bool active = false;
    private float currentSpeed;
    private Vector3 currentDirection = Vector3.forward;
    private float turnVelocity;
    private bool _exitTriggeredThisPress = false;



    void OnEnable()
    {
        // 1) Sets vehicle weights to make sure it won't tip over
        SetUpVehicle();

        // 2) Registers for updates:
        FixedUpdateManager.RegisterObserver(this);

        // 3) Checks if driver game object is set, if so set ups hooks
        if (driverObject)
        {
            SetUpDriver(driverObject);
        }
    }
    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);


    public void ObservedFixedUpdate()
    {
        if (!active)
            return;

        // --- EXIT HANDLING ---
        if (input.ExitVehicleFired)
        {
            if (!_exitTriggeredThisPress)
            {
                _exitTriggeredThisPress = true;
                DisableVehicle();
            }
            // skip all driving logic this frame
            return;
        }
        else
        {
            // reset once button is released
            _exitTriggeredThisPress = false;
        }

        // drive physics
        HandleMovement();
        ComputeWheelSpinTargets();

        // 2D visuals for horse + driver
        Handle2D();
    }

    // Public controls

    /// <summary>
    /// Called to make someone start driving.
    /// Pass in their transform and their IInputProvider.
    /// </summary>
    public void EnableVehicle(GameObject driver, InputProviderBase driverInput)
    {
        // 1) Vehicle becomes kinematic/dynamic as desired
        rb.isKinematic = false;
        active = true;

        // 2) Capture which input to read and makes sures first update round can pass
        input = driverInput;
        _exitTriggeredThisPress = input.ExitVehicleFired;

        // 3) Hook up driver Refrences
        SetUpDriver(driver);

        // 4) Set input Handler Map
        PlayerInputHandler.Instance.SwitchActionMap(ActionMaps.Vehicle.ToString());

        // 5) Set Camera Up
        CameraManager.Instance.SwitchCamera(CameraType.Vehicle);

        // 6) Disable Entry Points
        GameObjectUtils.SetActiveForAll(false, doors);
    }

    /// <summary>
    /// Called to drop the driver back out.
    /// </summary>
    public void DisableVehicle()
    {
        // 1) Kill drive state immediately
        active = false;
        currentSpeed = 0f;
        turnVelocity = 0f;

        // 2) Stop all wheel spins
        for (int i = 0; i < wheels.Length; i++)
            wheels[i].targetRPM = 0f;

        // 3) Reset horse animator to idle
        if (animator != null && vehicleProfiles != null)
            animator.SetEnum("Actions", Actions.Idle.ToString());

        // 4) Stop reading input
        input = null;

        // 5) Resets Driver variables
        UnsetDriver();

        // 6) Make vehicle immovable by NPC bumps
        rb.isKinematic = true;

        // 7) Switch back to Player action map
        PlayerInputHandler.Instance.SwitchActionMap(ActionMaps.Player.ToString());

        // 8) Switch camera back
        CameraManager.Instance.SwitchCamera(CameraType.Player);

        // 9) Sets back up interactable zones around the vehicle
        GameObjectUtils.SetActiveForAll(true, doors);
    }


    // Vehicle Movement
    protected virtual void HandleMovement()
    {
        Vector3 desiredVelocity = ComputeDesiredVelocity();
        float targetSpeed = desiredVelocity.magnitude;
        Vector3 targetDirection = GetTargetDirection(desiredVelocity);

        UpdateSpeed(targetSpeed);
        UpdateDirection(targetDirection);
        ApplyMovement();
    }

    private Vector3 ComputeDesiredVelocity()
    {
        return new Vector3(input.SteerInput.x, 0f, input.SteerInput.y) * vehicleProfiles.moveSpeed;
    }

    private Vector3 GetTargetDirection(Vector3 desiredVelocity)
    {
        return desiredVelocity.sqrMagnitude > 0.0001f
             ? desiredVelocity.normalized
             : currentDirection;
    }

    private void UpdateSpeed(float targetSpeed)
    {
        float accel = (targetSpeed > currentSpeed)
            ? vehicleProfiles.forwardAcceleration
            : vehicleProfiles.forwardDeceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            accel * Time.fixedDeltaTime
        );
    }

    private void UpdateDirection(Vector3 targetDirection)
    {
        float currentAngle = Mathf.Atan2(currentDirection.x, currentDirection.z) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;

        float smoothAngle = Mathf.SmoothDampAngle(
            currentAngle,
            targetAngle,
            ref turnVelocity,
            vehicleProfiles.turnSmoothTime,
            vehicleProfiles.turnSpeed,
            Time.fixedDeltaTime
        );

        currentDirection = (Quaternion.Euler(0f, smoothAngle, 0f) * Vector3.forward).normalized;
    }

    private void ApplyMovement()
    {
        Vector3 velocity = currentDirection * currentSpeed;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);

        if (velocity.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(currentDirection, Vector3.up);
            rb.MoveRotation(
                Quaternion.RotateTowards(
                    rb.rotation,
                    look,
                    vehicleProfiles.turnSpeed * Time.fixedDeltaTime
                )
            );
        }
    }

    private void ComputeWheelSpinTargets()
    {
        float speed = currentSpeed;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            float rpm = speed > 0.01f
                ? (speed / (2f * Mathf.PI * w.radius)) * 60f
                : 0f;
            wheels[i].targetRPM = rpm;
        }
    }

    private void SetUpVehicle()
    {
        // 1) Lower the COM so the pivot is closer to the ground:
        rb.centerOfMass = new Vector3(0, -1.0f, 0);  // tweak Y until it feels stable

        // 2) Increase angular drag so flips damp out instantly:
        rb.angularDamping = 10f;                        // higher = more resistance to spinning

        // 3) Manually boost inertia on X/Z so it “weighs” more against tipping:
        rb.inertiaTensor = new Vector3(1000f, 2f, 1000f);
        rb.inertiaTensorRotation = Quaternion.identity;
    }


    // 2D Visuals
    protected virtual void Handle2D()
    {
        // horse visuals...
        horseFacingController?.UpdateFacing(currentDirection);
        UpdateHorsePosition();

        // driver visuals, if we have one
        if (driverObject)
        {
            driverFacingController.UpdateFacing(currentDirection);
            UpdateDriverPosition();
        }

        // optionally horse animation
        UpdateHorseAnimation();
    }

    protected virtual void UpdateHorsePosition()
    {
        var dir = horseFacingController.CurrentDirection;
        horseQuad.localPosition = horseOffsetProfile.GetOffset(dir);
    }

    protected virtual void UpdateDriverPosition()
    {
        var dir = driverFacingController.CurrentDirection;
        driverSeatAnchor.transform.localPosition = driverOffsetProfile.GetOffset(dir);
    }

    protected virtual void UpdateHorseAnimation()
    {
        if (animator == null || vehicleProfiles == null)
            return;

        // normalize your speed into [0,1]
        float t = currentSpeed / vehicleProfiles.moveSpeed;
        Actions action;

        if (t <= vehicleProfiles.walkThreshold)
            action = Actions.Idle;
        else if (t <= vehicleProfiles.runThreshold)
            action = Actions.Walk;
        else
            action = Actions.Run;

        animator.SetEnum("Actions", action.ToString());
    }

    private void SetUpDriver(GameObject driver)
    {
        // Check if driver is null, if so set driver as driverObject
        driverObject = driver;
        driverCharacterController = driverObject.GetComponent<CharacterControllerBase>();
        driverFacingController = driverObject.GetComponent<FacingDirectionBase>();

        driverCharacterController.OnMount();

        ParentToAnchor();
    }

    private void UnsetDriver()
    {
        driverCharacterController.OnDismount();
        driverObject = null;
        driverCharacterController = null;
        driverFacingController = null;

    }

    private void ParentToAnchor()
    {
        // 1) Snap the world-space transform to exactly where the anchor is:
        driverObject.transform.position = driverSeatAnchor.position;
        driverObject.transform.rotation = driverSeatAnchor.rotation;

        // 2) Now parent but keep that world-space pose:
        driverObject.transform.SetParent(driverSeatAnchor, worldPositionStays: true);

        // 3) Zero your local so you’re *exactly* at the anchor point:
        driverObject.transform.localPosition = Vector3.zero;
        driverObject.transform.localRotation = Quaternion.identity;

    }
}
