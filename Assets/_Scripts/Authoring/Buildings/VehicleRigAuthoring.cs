using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// On a vehicle's root, beside CaravanAuthoring (Caravan_System.md CV-D21): the art is baked with it, and these
// parts are moved by code every frame (VehiclePoseSystem). ⚠ Never make any of them a toolkit rig target:
// clip-driven parts (doors, Shakable) go elsewhere in the hierarchy, or the two fight over the transform.
public class VehicleRigAuthoring : MonoBehaviour
{
    [Header("Code-driven parts")]
    public Transform[] wheels;
    [Tooltip("Parent of the front axle and front wheels, on the axle centre; yawed by steering.")]
    public Transform frontAxlePivot;
    [Tooltip("Carries the speed bounce and the lean into turns; also the shake and broken slump while those have no clips.")]
    public Transform ride;
    [Tooltip("Where the horse stands, moved to the VehicleProfileSO's offset for each facing. Empty for a car.")]
    public Transform puller;

    [Header("Smoke (ParticleSystems; they bake as companion objects)")]
    public ParticleSystem chimneySmoke;
    public ParticleSystem smokeBurst;
    [Tooltip("Loops while the vehicle is broken (0 HP).")]
    public ParticleSystem brokenSmoke;

    [Header("Feel")]
    public float maxFrontAxleYawDegrees = 25f;
    [Tooltip("Code shake only (no VehicleShakeBuildUp/VehicleShakeBurst clip).")]
    public float shakeFrequency = 22f;
    [Tooltip("Code slump only (no VehicleBroken clip).")]
    public float brokenSagDegrees = 4f;

    public class Baker : Baker<VehicleRigAuthoring>
    {
        public override void Bake(VehicleRigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Quaternion rootRotation = authoring.transform.rotation;

            VehicleRig rig = new VehicleRig
            {
                maxFrontAxleYawDegrees = authoring.maxFrontAxleYawDegrees,
                shakeFrequency = authoring.shakeFrequency,
                brokenSagDegrees = authoring.brokenSagDegrees,
                rideRestRotation = quaternion.identity,
            };
            if (authoring.frontAxlePivot != null)
            {
                rig.frontAxle = GetEntity(authoring.frontAxlePivot, TransformUsageFlags.Dynamic);
                rig.frontAxleRestRotation = authoring.frontAxlePivot.localRotation;
                rig.frontAxleYawAxis = AxisInPartSpace(authoring.frontAxlePivot, rootRotation, Vector3.up);
            }
            if (authoring.ride != null)
            {
                rig.ride = GetEntity(authoring.ride, TransformUsageFlags.Dynamic);
                rig.rideRestPosition = authoring.ride.localPosition;
                rig.rideRestRotation = authoring.ride.localRotation;
            }
            if (authoring.puller != null)
                rig.puller = GetEntity(authoring.puller, TransformUsageFlags.Dynamic);
            AddComponent(entity, rig);

            DynamicBuffer<VehicleWheel> wheels = AddBuffer<VehicleWheel>(entity);
            if (authoring.wheels != null)
            {
                foreach (Transform wheel in authoring.wheels)
                {
                    if (wheel == null) continue;
                    wheels.Add(new VehicleWheel
                    {
                        wheel = GetEntity(wheel, TransformUsageFlags.Dynamic),
                        restRotation = wheel.localRotation,
                        spinAxis = AxisInPartSpace(wheel, rootRotation, Vector3.right),
                    });
                }
            }

            AddComponent(entity, new VehicleSmoke
            {
                chimneySmoke = SmokeEntity(authoring.chimneySmoke),
                smokeBurst = SmokeEntity(authoring.smokeBurst),
                brokenSmoke = SmokeEntity(authoring.brokenSmoke),
            });

            AddComponent(entity, new VehicleAnimationKeys
            {
                shakeBuildUp = AnimationNameKeys.Resolve(AnimationNameConvention.VehicleShakeBuildUp),
                shakeBurst = AnimationNameKeys.Resolve(AnimationNameConvention.VehicleShakeBurst),
                broken = AnimationNameKeys.Resolve(AnimationNameConvention.VehicleBroken),
            });
        }

        private Entity SmokeEntity(ParticleSystem smoke)
        {
            return smoke != null ? GetEntity(smoke, TransformUsageFlags.Dynamic) : Entity.Null;
        }

        // A vehicle-space axis (right = wheel spin, up = steering) expressed in the part's own space.
        private static float3 AxisInPartSpace(Transform part, Quaternion rootRotation, Vector3 vehicleAxis)
        {
            Quaternion partRotationInVehicle = Quaternion.Inverse(rootRotation) * part.rotation;
            return Quaternion.Inverse(partRotationInVehicle) * vehicleAxis;
        }
    }
}
