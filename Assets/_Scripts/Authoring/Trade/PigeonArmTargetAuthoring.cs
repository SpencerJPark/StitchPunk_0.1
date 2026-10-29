using Unity.Entities;
using UnityEngine;

// Put on a child of the player (or of a bone the arm-out animation moves). The pigeon lands on this point.
public class PigeonArmTargetAuthoring : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.12f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.3f);
    }

    public class Baker : Baker<PigeonArmTargetAuthoring>
    {
        public override void Bake(PigeonArmTargetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PigeonArmTarget>(entity);
        }
    }
}
