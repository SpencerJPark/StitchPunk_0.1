using Unity.Entities;
using UnityEngine;

// On a unit root (BaseUnit carries it, so every unit inherits it). Names the billboarded Visual that topples and
// the limbs that flail; see FakeRagdollComponents.cs.
public class FakeRagdollAuthoring : MonoBehaviour
{
    [Tooltip("The billboarded child that tips over. Its pivot must be at the feet.")]
    public Transform visual;
    public LimbEntry[] limbs;

    [System.Serializable]
    public struct LimbEntry
    {
        public Transform limb;
        [Tooltip("Peak swing either side of the rest pose while airborne.")]
        public float flailDegrees;
    }

    public class Baker : Baker<FakeRagdollAuthoring>
    {
        public override void Bake(FakeRagdollAuthoring authoring)
        {
            if (authoring.visual == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FakeRagdollRig { visual = GetEntity(authoring.visual, TransformUsageFlags.Dynamic) });
            AddComponent(entity, new FakeRagdoll());
            SetComponentEnabled<FakeRagdoll>(entity, false);

            DynamicBuffer<FakeRagdollLimb> limbBuffer = AddBuffer<FakeRagdollLimb>(entity);
            if (authoring.limbs == null) return;
            for (int limbIndex = 0; limbIndex < authoring.limbs.Length; limbIndex++)
            {
                if (authoring.limbs[limbIndex].limb == null) continue;
                limbBuffer.Add(new FakeRagdollLimb
                {
                    limb = GetEntity(authoring.limbs[limbIndex].limb, TransformUsageFlags.Dynamic),
                    flailDegrees = authoring.limbs[limbIndex].flailDegrees,
                    // Spread the phases so the limbs don't swing in lockstep.
                    flailPhase = limbIndex * 1.7f,
                });
            }
        }
    }
}
