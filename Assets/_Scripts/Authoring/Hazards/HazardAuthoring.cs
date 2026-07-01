using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Authoring for a proximity damage zone (the v2 environmental damage example — e.g. a spike trap).
// Plain authored fields (no blob); bakes a HazardZone read by HazardZoneSystem. Place one on a
// GameObject in DOTSTestScene to prove the non-attack damage path.
public class HazardAuthoring : MonoBehaviour
{
    [Tooltip("Damage dealt to each unit in range per fire.")]
    public int damageAmount = 10;

    [Tooltip("XZ radius units must be within to be hit.")]
    public float radius = 1.5f;

    [Tooltip("Seconds between fires. The whole zone hits everyone in range at most once per interval.")]
    public float retriggerInterval = 1f;

    [Header("Ragdoll feel (on kill)")]
    [Tooltip("Scales ragdoll violence on a lethal hit. 1 = baseline.")]
    public float ragdollForce = 1f;

    [Tooltip("Upward launch on a lethal hit. 0 = just tips over.")]
    public float launchForceY = 0f;

    [Tooltip("Sideways launch on a lethal hit, away from the hazard. 0 = none.")]
    public float launchForceX = 0f;

    public class Baker : Baker<HazardAuthoring>
    {
        public override void Bake(HazardAuthoring authoring)
        {
            // Dynamic so the baked entity has a LocalTransform — HazardZoneSystem reads the zone's
            // world position as the proximity centre and the ragdoll direction source.
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new HazardZone
            {
                damageAmount      = authoring.damageAmount,
                damageSource      = DamageSource.Hazard,
                radius            = authoring.radius,
                retriggerInterval = authoring.retriggerInterval,
                lastTriggerTime   = float.NegativeInfinity,
                ragdollForce      = authoring.ragdollForce,
                launchForceY      = authoring.launchForceY,
                launchForceX      = authoring.launchForceX,
            });
        }
    }
}
