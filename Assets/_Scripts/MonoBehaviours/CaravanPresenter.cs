using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// The caravan's art (Caravan.prefab, a scene object) following the baked caravan entity (Caravan_System.md CV-D7).
// ECS owns every number; this only turns them into transforms and particles: body pose, rolling wheels, the
// steering front axle, the doors, the shake and the chimney/burst smoke. Pivots are measured once in Awake.
public class CaravanPresenter : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Transform[] wheels;
    [Tooltip("Parent of the front axle and front wheels, placed on the axle centre; yawed by steering.")]
    [SerializeField] private Transform frontAxlePivot;
    [Tooltip("Everything above the wheels; jittered while the caravan shakes.")]
    [SerializeField] private Transform shakeRoot;

    [Header("Doors")]
    [Tooltip("Each art hinge mirrors the DoorAuthoring with the same Door Name on the caravan's simulation object.")]
    [SerializeField] private ArtDoorHinge[] doors;

    [Header("Smoke")]
    [SerializeField] private ParticleSystem chimneySmoke;
    [SerializeField] private ParticleSystem smokeBurst;
    [Tooltip("Loops while the caravan is broken (0 HP).")]
    [SerializeField] private ParticleSystem brokenSmoke;

    [Header("Feel")]
    [SerializeField] private float maxFrontAxleYawDegrees = 25f;
    [SerializeField] private float shakeFrequency = 22f;
    [Tooltip("How far a broken caravan slumps onto one side.")]
    [SerializeField] private float brokenSagDegrees = 4f;

    [Header("Puller")]
    [Tooltip("The horse (or other puller). Placed at the VehicleProfileSO's offset for the current facing; empty for a car.")]
    [SerializeField] private Transform puller;

    private Quaternion[] wheelRestRotations;
    private Vector3[] wheelSpinAxes;
    private Quaternion frontAxleRestRotation;
    private Vector3 frontAxleYawAxis;
    private Quaternion[] doorRestRotations;
    private Vector3[] doorHingeAxes;
    private FixedString32Bytes[] doorNames;
    private Vector3 shakeRestPosition;
    private Quaternion shakeRestRotation;

    private EntityQuery caravanQuery;
    private EntityQuery vehicleLibraryQuery;
    private EntityQuery doorQuery;
    private World queryWorld;
    private int lastSeenSmokeBurstCount;
    private bool hasSeenSmokeBurstCount;

    private void Awake()
    {
        int wheelCount = wheels != null ? wheels.Length : 0;
        wheelRestRotations = new Quaternion[wheelCount];
        wheelSpinAxes = new Vector3[wheelCount];
        for (int wheelIndex = 0; wheelIndex < wheelCount; wheelIndex++)
        {
            if (wheels[wheelIndex] == null) continue;
            wheelRestRotations[wheelIndex] = wheels[wheelIndex].localRotation;
            wheelSpinAxes[wheelIndex] = AxisInLocalSpace(wheels[wheelIndex], transform.right);
        }

        if (frontAxlePivot != null)
        {
            frontAxleRestRotation = frontAxlePivot.localRotation;
            frontAxleYawAxis = AxisInLocalSpace(frontAxlePivot, transform.up);
        }
        int doorCount = doors != null ? doors.Length : 0;
        doorRestRotations = new Quaternion[doorCount];
        doorHingeAxes = new Vector3[doorCount];
        doorNames = new FixedString32Bytes[doorCount];
        for (int doorIndex = 0; doorIndex < doorCount; doorIndex++)
        {
            doorNames[doorIndex] = new FixedString32Bytes(doors[doorIndex].doorName ?? string.Empty);
            if (doors[doorIndex].hinge == null) continue;
            doorRestRotations[doorIndex] = doors[doorIndex].hinge.localRotation;
            doorHingeAxes[doorIndex] = AxisInLocalSpace(doors[doorIndex].hinge, transform.up);
        }
        if (shakeRoot != null)
        {
            shakeRestPosition = shakeRoot.localPosition;
            shakeRestRotation = shakeRoot.localRotation;
        }
    }

    private void LateUpdate()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        if (queryWorld != world)
        {
            queryWorld = world;
            vehicleLibraryQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<VehicleLibrary>());
            doorQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Door>(), ComponentType.ReadOnly<DoorName>(), ComponentType.ReadOnly<DoorOwner>());
            caravanQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Caravan>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<CaravanDriveState>(),
                ComponentType.ReadOnly<CaravanShake>(),
                ComponentType.ReadOnly<CaravanPresentationEvents>());
        }

        // Until the subscene streams in, the art keeps its scene pose.
        if (caravanQuery.IsEmptyIgnoreFilter) return;

        EntityManager entityManager = world.EntityManager;
        Entity caravanEntity = caravanQuery.GetSingletonEntity();

        LocalTransform caravanPose = entityManager.GetComponentData<LocalTransform>(caravanEntity);
        transform.SetPositionAndRotation(
            new Vector3(caravanPose.Position.x, caravanPose.Position.y, caravanPose.Position.z),
            new Quaternion(caravanPose.Rotation.value.x, caravanPose.Rotation.value.y, caravanPose.Rotation.value.z, caravanPose.Rotation.value.w));

        CaravanDriveState driveState = entityManager.GetComponentData<CaravanDriveState>(caravanEntity);
        ApplyWheels(driveState);
        ApplyDoors(caravanEntity);
        bool broken = entityManager.HasComponent<CaravanBroken>(caravanEntity)
            && entityManager.IsComponentEnabled<CaravanBroken>(caravanEntity);
        Quaternion rideTilt = Quaternion.identity;
        if (TryGetProfile(entityManager, caravanEntity, out BlobAssetReference<VehicleLibraryBlob> vehicleLibrary, out int vehicleIndex))
        {
            ref VehicleProfileBlob profile = ref vehicleLibrary.Value.vehicles[vehicleIndex];
            rideTilt = RideTilt(driveState, ref profile);
            ApplyPuller(driveState, ref profile);
        }
        ApplyShake(entityManager.GetComponentData<CaravanShake>(caravanEntity), broken, rideTilt);
        ApplyLoopingSmoke(brokenSmoke, broken);
        ApplyChimney(entityManager.HasComponent<CaravanChimneySmoking>(caravanEntity)
            && entityManager.IsComponentEnabled<CaravanChimneySmoking>(caravanEntity));
        ApplySmokeBurst(entityManager.GetComponentData<CaravanPresentationEvents>(caravanEntity).smokeBurstCount);
    }

    private void ApplyWheels(CaravanDriveState driveState)
    {
        float spinDegrees = math.degrees(driveState.wheelSpinRadians);
        for (int wheelIndex = 0; wheelIndex < wheelRestRotations.Length; wheelIndex++)
        {
            if (wheels[wheelIndex] == null) continue;
            wheels[wheelIndex].localRotation = wheelRestRotations[wheelIndex]
                * Quaternion.AngleAxis(spinDegrees, wheelSpinAxes[wheelIndex]);
        }

        if (frontAxlePivot != null)
            frontAxlePivot.localRotation = frontAxleRestRotation
                * Quaternion.AngleAxis(driveState.steerInput * maxFrontAxleYawDegrees, frontAxleYawAxis);
    }

    // The swing direction is the sign of the DoorAuthoring's Open Degrees, so the art only needs the hinge.
    private void ApplyDoors(Entity caravanEntity)
    {
        if (doorNames.Length == 0 || doorQuery.IsEmptyIgnoreFilter) return;
        NativeArray<Door> doorStates = doorQuery.ToComponentDataArray<Door>(Allocator.Temp);
        NativeArray<DoorName> doorStateNames = doorQuery.ToComponentDataArray<DoorName>(Allocator.Temp);
        NativeArray<DoorOwner> doorOwners = doorQuery.ToComponentDataArray<DoorOwner>(Allocator.Temp);

        for (int stateIndex = 0; stateIndex < doorStates.Length; stateIndex++)
        {
            if (doorOwners[stateIndex].owner != caravanEntity) continue;
            for (int doorIndex = 0; doorIndex < doorNames.Length; doorIndex++)
            {
                Transform hinge = doors[doorIndex].hinge;
                if (hinge == null || doorNames[doorIndex] != doorStateNames[stateIndex].value) continue;
                hinge.localRotation = doorRestRotations[doorIndex]
                    * Quaternion.AngleAxis(doorStates[stateIndex].angleDegrees, doorHingeAxes[doorIndex]);
            }
        }

        doorStates.Dispose();
        doorStateNames.Dispose();
        doorOwners.Dispose();
    }

    // A bounce that grows with speed plus a lean into turns, clamped to the original pitch/roll ranges.
    private bool TryGetProfile(EntityManager entityManager, Entity caravanEntity,
        out BlobAssetReference<VehicleLibraryBlob> vehicleLibrary, out int vehicleIndex)
    {
        vehicleLibrary = default;
        vehicleIndex = 0;
        if (vehicleLibraryQuery.IsEmptyIgnoreFilter || !entityManager.HasComponent<Vehicle>(caravanEntity)) return false;
        vehicleLibrary = vehicleLibraryQuery.GetSingleton<VehicleLibrary>().library;
        vehicleIndex = (int)entityManager.GetComponentData<Vehicle>(caravanEntity).vehicleType;
        return vehicleLibrary.IsCreated && vehicleIndex > 0 && vehicleIndex < vehicleLibrary.Value.vehicles.Length;
    }

    private void ApplyPuller(CaravanDriveState driveState, ref VehicleProfileBlob profile)
    {
        if (puller == null || !profile.HasPuller) return;
        float3 offset = profile.pullerOffsets[(int)driveState.facing];
        puller.localPosition = new Vector3(offset.x, offset.y, offset.z);
    }

    private Quaternion RideTilt(CaravanDriveState driveState, ref VehicleProfileBlob profile)
    {
        float speedFraction = Mathf.Clamp01(driveState.forwardSpeed / Mathf.Max(profile.rideMaxSpeed, 0.0001f));
        float pitchNoise = Mathf.PerlinNoise(Time.time * profile.rideNoiseFrequency, 0f) - 0.5f;
        float rollNoise = Mathf.PerlinNoise(1f, Time.time * profile.rideNoiseFrequency) - 0.5f;
        float yawFraction = Mathf.Clamp(driveState.yawRateDegrees * Mathf.Deg2Rad / Mathf.Max(profile.maxYawRate, 0.0001f), -1f, 1f);

        float pitchDegrees = Mathf.Clamp(pitchNoise * 2f * profile.speedTiltDegrees * speedFraction, -profile.pitchRangeDegrees, profile.pitchRangeDegrees);
        float rollDegrees = Mathf.Clamp(rollNoise * 2f * profile.speedTiltDegrees * speedFraction - yawFraction * profile.turnTiltDegrees,
            -profile.rollRangeDegrees, profile.rollRangeDegrees);
        return Quaternion.Euler(pitchDegrees, 0f, 0f) * Quaternion.Euler(0f, 0f, rollDegrees);
    }

    private void ApplyShake(CaravanShake shake, bool broken, Quaternion rideTilt)
    {
        if (shakeRoot == null) return;
        Quaternion sag = (broken ? Quaternion.Euler(0f, 0f, brokenSagDegrees) : Quaternion.identity) * rideTilt;
        if (shake.remainingSeconds <= 0f)
        {
            shakeRoot.localPosition = shakeRestPosition;
            shakeRoot.localRotation = shakeRestRotation * sag;
            return;
        }

        float noiseTime = Time.time * shakeFrequency;
        Vector3 jitter = new Vector3(
            Mathf.PerlinNoise(noiseTime, 0.1f) - 0.5f,
            Mathf.PerlinNoise(0.7f, noiseTime) - 0.5f,
            Mathf.PerlinNoise(noiseTime, 3.3f) - 0.5f) * (2f * shake.amplitude);
        shakeRoot.localPosition = shakeRestPosition + jitter;
        shakeRoot.localRotation = shakeRestRotation * sag * Quaternion.Euler(0f, 0f, jitter.x * 40f);
    }

    private void ApplyChimney(bool smoking)
    {
        ApplyLoopingSmoke(chimneySmoke, smoking);
    }

    private static void ApplyLoopingSmoke(ParticleSystem smoke, bool emitting)
    {
        if (smoke == null) return;
        if (emitting && !smoke.isEmitting)
            smoke.Play(true);
        else if (!emitting && smoke.isEmitting)
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void ApplySmokeBurst(int smokeBurstCount)
    {
        // The first read only learns the counter, so loading in never replays an old burst.
        if (!hasSeenSmokeBurstCount)
        {
            lastSeenSmokeBurstCount = smokeBurstCount;
            hasSeenSmokeBurstCount = true;
            return;
        }
        if (smokeBurstCount == lastSeenSmokeBurstCount) return;
        lastSeenSmokeBurstCount = smokeBurstCount;
        if (smokeBurst != null)
            smokeBurst.Play(true);
    }

    private static Vector3 AxisInLocalSpace(Transform pivot, Vector3 worldAxis)
    {
        return Quaternion.Inverse(pivot.rotation) * worldAxis;
    }

    [System.Serializable]
    private struct ArtDoorHinge
    {
        public string doorName;
        public Transform hinge;
    }
}
