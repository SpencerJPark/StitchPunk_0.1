using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// The caravan's art (Caravan.prefab, a scene object) following the baked caravan entity (Caravan_System.md CV-D7).
// ECS owns every number; this only turns them into transforms and particles: body pose, rolling wheels, the
// steering front axle, the back doors, the shake and the chimney/burst smoke. Pivots are measured once in Awake.
public class CaravanPresenter : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Transform[] wheels;
    [Tooltip("Parent of the front axle and front wheels, placed on the axle centre; yawed by steering.")]
    [SerializeField] private Transform frontAxlePivot;
    [SerializeField] private Transform leftDoorHinge;
    [SerializeField] private Transform rightDoorHinge;
    [Tooltip("Everything above the wheels; jittered while the caravan shakes.")]
    [SerializeField] private Transform shakeRoot;

    [Header("Smoke")]
    [SerializeField] private ParticleSystem chimneySmoke;
    [SerializeField] private ParticleSystem smokeBurst;

    [Header("Feel")]
    [SerializeField] private float doorOpenDegrees = 110f;
    [SerializeField] private float maxFrontAxleYawDegrees = 25f;
    [SerializeField] private float shakeFrequency = 22f;

    private Quaternion[] wheelRestRotations;
    private Vector3[] wheelSpinAxes;
    private Quaternion frontAxleRestRotation;
    private Vector3 frontAxleYawAxis;
    private Quaternion leftDoorRestRotation;
    private Vector3 leftDoorHingeAxis;
    private Quaternion rightDoorRestRotation;
    private Vector3 rightDoorHingeAxis;
    private Vector3 shakeRestPosition;
    private Quaternion shakeRestRotation;

    private EntityQuery caravanQuery;
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
        if (leftDoorHinge != null)
        {
            leftDoorRestRotation = leftDoorHinge.localRotation;
            leftDoorHingeAxis = AxisInLocalSpace(leftDoorHinge, transform.up);
        }
        if (rightDoorHinge != null)
        {
            rightDoorRestRotation = rightDoorHinge.localRotation;
            rightDoorHingeAxis = AxisInLocalSpace(rightDoorHinge, transform.up);
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
            caravanQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Caravan>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<CaravanDriveState>(),
                ComponentType.ReadOnly<CaravanDoors>(),
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
        ApplyDoors(entityManager.GetComponentData<CaravanDoors>(caravanEntity));
        ApplyShake(entityManager.GetComponentData<CaravanShake>(caravanEntity));
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

    // The doors hinge on their outer edges and swing out backwards: +angle opens the left one, -angle the right.
    private void ApplyDoors(CaravanDoors doors)
    {
        float openDegrees = CaravanDoorControl.Openness(in doors) * doorOpenDegrees;
        if (leftDoorHinge != null)
            leftDoorHinge.localRotation = leftDoorRestRotation * Quaternion.AngleAxis(openDegrees, leftDoorHingeAxis);
        if (rightDoorHinge != null)
            rightDoorHinge.localRotation = rightDoorRestRotation * Quaternion.AngleAxis(-openDegrees, rightDoorHingeAxis);
    }

    private void ApplyShake(CaravanShake shake)
    {
        if (shakeRoot == null) return;
        if (shake.remainingSeconds <= 0f)
        {
            shakeRoot.localPosition = shakeRestPosition;
            shakeRoot.localRotation = shakeRestRotation;
            return;
        }

        float noiseTime = Time.time * shakeFrequency;
        Vector3 jitter = new Vector3(
            Mathf.PerlinNoise(noiseTime, 0.1f) - 0.5f,
            Mathf.PerlinNoise(0.7f, noiseTime) - 0.5f,
            Mathf.PerlinNoise(noiseTime, 3.3f) - 0.5f) * (2f * shake.amplitude);
        shakeRoot.localPosition = shakeRestPosition + jitter;
        shakeRoot.localRotation = shakeRestRotation * Quaternion.Euler(0f, 0f, jitter.x * 40f);
    }

    private void ApplyChimney(bool smoking)
    {
        if (chimneySmoke == null) return;
        if (smoking && !chimneySmoke.isEmitting)
            chimneySmoke.Play(true);
        else if (!smoking && chimneySmoke.isEmitting)
            chimneySmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
}
