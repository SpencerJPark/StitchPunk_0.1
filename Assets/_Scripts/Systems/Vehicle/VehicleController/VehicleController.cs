using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Controller Dependencies")]
    protected IInputProvider input;
    [SerializeField] private Rigidbody rb;
    [SerializeField] protected Camera mainCamera;

    [Header("Child Object Refs")]
    [SerializeField] private Transform driverSeatAnchor;
    [SerializeField] private Transform horseQuad;

    [Tooltip("Drag in your door zones")]
    [SerializeField] private List<GameObject> doorZones = new List<GameObject>();
    

    [Header("View Dependencies")]
    [SerializeField] private RiveAnimator riveAnimator;
    [SerializeField] private VehicleShakerAnimator vehicleShakerAnimator;
    [SerializeField] private VehicleWheelAnimator vehicleWheelAnimator;


    [Header("Model Dependencies")]
    [SerializeField] private VehicleData vehicleData;


    private VehicleModel vehicleModel;

    void Awake()
    {
        vehicleModel = new VehicleModel(vehicleData);
    } 

    void OnEnable()
    {
        SetUpVehicle();

        FixedUpdateManager.RegisterObserver(this);

        // Issue is that this initializes before characters
        // if (DriverObject != null)
        // {
        //     // 1) get the CharacterControllerBase
        //     var ctrl = DriverObject.GetComponent<CharacterControllerBase>();
        //     if (ctrl == null)
        //     {
        //         Debug.LogError($"VehicleController: DriverObject {DriverObject.name} has no CharacterControllerBase!");
        //         return;
        //     }

        //     // 2) pull its input provider
        //     var provider = ctrl.input;

        //     // 3) now enable the vehicle with that exact provider
        //     EnableVehicle(DriverObject, provider);
        // }
    }
    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);

    public void ObservedFixedUpdate()
    {
        if (!vehicleModel.Active)
            return;

        // --- EXIT HANDLING ---
        if (input.ExitVehicleFired)
        {
            if (!vehicleModel.ExitTriggeredThisPress)
            {
                vehicleModel.SetExitTriggered(true);
                DisableVehicle();
            }
            // skip all driving logic this frame
            return;
        }
        else
        {
            // reset once button is released
            vehicleModel.SetExitTriggered(false);
        }

        // drive physics
        HandleMovement();

        // 2D visuals for horse + driver
        HandleView();
    }


    // Public controls
    public void EnableVehicle(GameObject driver, IInputProvider driverInput)
    {
        // 1) Vehicle becomes kinematic/dynamic as desired
        rb.isKinematic = false;
        vehicleModel.SetActive(true);

        // 2) Capture which input to read and makes sures first update round can pass
        input = driverInput;
        vehicleModel.SetExitTriggered(input.ExitVehicleFired);

        // 3) Hook up driver Refrences
        SetUpDriver(driver);

        // 4) Set input Handler Map
        PlayerInputHandler.Instance.SwitchActionMap(ActionMaps.Vehicle);

        // 5) Set Camera Up
        CameraManager.Instance.SwitchCamera(CameraType.Vehicle);

        // 6) Disable Entry Points
        GameObjectUtils.SetActiveForAll(false, doorZones);
    }

    public void DisableVehicle()
    {
        // 1) Kill drive state immediately
        vehicleModel.SetActive(false);
        vehicleModel.SetCurrentSpeed(0f);
        vehicleModel.SetTurnVelocity(0f);

        // 2) Reset horse riveAnimator to idle
        if (riveAnimator != null && vehicleModel != null)
            riveAnimator.SetEnum("Actions", Actions.Idle.ToString());

        // 3) Stop reading input
        input = null;

        // 4) Resets Driver variables
        UnsetDriver();

        // 5) Make vehicle immovable by NPC bumps
        rb.isKinematic = true;

        // 6) Switch back to Player action map
        PlayerInputHandler.Instance.SwitchActionMap(ActionMaps.Player);

        // 7) Switch camera back
        CameraManager.Instance.SwitchCamera(CameraType.Player);

        // 8) Sets back up interactable zones around the vehicle
        GameObjectUtils.SetActiveForAll(true, doorZones);
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
        return new Vector3(input.SteerInput.x, 0f, input.SteerInput.y) * vehicleModel.MoveSpeed;
    }

    private Vector3 GetTargetDirection(Vector3 desiredVelocity)
    {
        Vector2 steerInput = input.SteerInput;

        if (steerInput.sqrMagnitude > 0.0001f)
            return new Vector3(steerInput.x, 0f, steerInput.y).normalized;

        return vehicleModel.MovementVector;
    }


    private void UpdateSpeed(float targetSpeed)
    {
        float accel = (targetSpeed > vehicleModel.CurrentSpeed)
            ? vehicleModel.ForwardAcceleration
            : vehicleModel.ForwardDeceleration;

        vehicleModel.SetCurrentSpeed (Mathf.MoveTowards(
            vehicleModel.CurrentSpeed,
            targetSpeed,
            accel * Time.fixedDeltaTime
        ));
    }

    private void UpdateDirection(Vector3 targetDirection)
    {
        float currentAngle = Mathf.Atan2(vehicleModel.MovementVector.x, vehicleModel.MovementVector.z) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;

        float turnVelocity = vehicleModel.TurnVelocity;

        // Calculate how fast we should allow turning based on current speed
        float speedFactor = Mathf.Clamp01(vehicleModel.CurrentSpeed / vehicleModel.MoveSpeed);
        float idleTurnFactor = Mathf.Lerp(vehicleModel.IdleTurnSpeedFactor, 1f, speedFactor);
        float turnSpeed = vehicleModel.TurnSpeed * idleTurnFactor;

        float smoothAngle = Mathf.SmoothDampAngle(
            currentAngle,
            targetAngle,
            ref turnVelocity,
            vehicleModel.TurnSmoothTime,
            turnSpeed,
            Time.fixedDeltaTime
        );

        vehicleModel.SetTurnVelocity(turnVelocity);
        vehicleModel.SetMovementVector((Quaternion.Euler(0f, smoothAngle, 0f) * Vector3.forward).normalized);
    }




    private void ApplyMovement()
    {
        Vector3 velocity = vehicleModel.MovementVector * vehicleModel.CurrentSpeed;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);

        if (velocity.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(vehicleModel.MovementVector, Vector3.up);
            rb.MoveRotation(
                Quaternion.RotateTowards(
                    rb.rotation,
                    look,
                    vehicleModel.TurnSpeed * Time.fixedDeltaTime
                )
            );
        }
    }




    // Animations
    protected virtual void HandleView()
    {
        // horse visuals...
        //horseFacingController?.UpdateFacing(currentDirection);
        UpdateHorsePosition();

        // driver visuals, if we have one
        if (vehicleModel.DriverObject)
        {
            //driverFacingController.UpdateFacing(currentDirection);
            UpdateDriverPosition();
        }

        // optionally horse animation
        UpdateHorseAnimation();
        vehicleShakerAnimator.UpdateShake(vehicleModel.CurrentSpeed);
        vehicleWheelAnimator.UpdateWheels(vehicleModel.CurrentSpeed);
    }

    private void UpdateHorseFacing()
    {
        // if (mainCamera == null || riveAnimator == null)
        //     return;

        // if (unitModel.IsMoving)
        // {
        //     Direction newDirection = DirectionUtil.GetCameraRelativeDirection(
        //         mainCamera,
        //         unitModel.MovementVector,
        //         unitModel.DirectionType
        //     );

        //     if (newDirection != unitModel.CurrentDirection)
        //     {
        //         unitModel.SetDirection(newDirection);
        //     }
        // }

        // riveAnimator.SetEnum("Direction", unitModel.CurrentDirection.ToString());
    }

    protected virtual void UpdateHorsePosition()
    {
        //var dir = horseFacingController.CurrentDirection;
        //horseQuad.localPosition = vehicleModel.HorseOffset.GetOffset(dir);
    }

    protected virtual void UpdateDriverPosition()
    {
        // var dir = driverFacingController.CurrentDirection;
        // driverSeatAnchor.transform.localPosition = vehicleModel.DriverOffset.GetOffset(dir);
    }

    protected virtual void UpdateHorseAnimation()
    {
        if (riveAnimator == null || vehicleModel == null)
            return;

        // normalize your speed into [0,1]
        float t = vehicleModel.CurrentSpeed / vehicleModel.MoveSpeed;
        Actions action;

        if (t <= vehicleModel.WalkThreshold)
            action = Actions.Idle;
        else if (t <= vehicleModel.RunThreshold)
            action = Actions.Walk;
        else
            action = Actions.Run;

        riveAnimator.SetEnum("Actions", action.ToString());
    }


    // Setup
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
   
    private void SetUpDriver(GameObject driver)
    {
        // Check if driver is null, if so set driver as DriverObject
        vehicleModel.SetDriver(driver);
        vehicleModel.SetDriverController(vehicleModel.DriverObject.GetComponent<CharacterControllerBase>());

        vehicleModel.DriverController.OnMount();

        ParentToAnchor();
    }

    private void UnsetDriver()
    {
        vehicleModel.DriverController.OnDismount();

        vehicleModel.SetDriver(null);
        vehicleModel.SetDriverController(null);
    }

    private void ParentToAnchor()
    {
        // 1) Snap the world-space transform to exactly where the anchor is:
        vehicleModel.DriverObject.transform.position = driverSeatAnchor.position;
        vehicleModel.DriverObject.transform.rotation = driverSeatAnchor.rotation;

        // 2) Now parent but keep that world-space pose:
        vehicleModel.DriverObject.transform.SetParent(driverSeatAnchor, worldPositionStays: true);

        // 3) Zero your local so you’re *exactly* at the anchor point:
        vehicleModel.DriverObject.transform.localPosition = Vector3.zero;
        vehicleModel.DriverObject.transform.localRotation = Quaternion.identity;
    }
}
