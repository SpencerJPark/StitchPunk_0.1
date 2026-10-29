using Unity.Entities;
using UnityEngine;

// A place a client takes delivery. The ids are what orders and saves reference, never the Entity.
public class TradeDropOffAuthoring : MonoBehaviour
{
    public int dropOffId;
    public DropOffTag tag;
    public float handOverRadius = 6f;
    [Range(0, 3)] public int dangerRating;
    [Tooltip("Where delivered units go and idle. Empty = this object's own position.")]
    public Transform standPoint;

    public class Baker : Baker<TradeDropOffAuthoring>
    {
        public override void Bake(TradeDropOffAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Transform standTransform = authoring.standPoint != null ? authoring.standPoint : authoring.transform;
            AddComponent(entity, new TradeDropOff
            {
                dropOffId = authoring.dropOffId,
                tag = authoring.tag,
                handOverRadius = authoring.handOverRadius,
                standPoint = standTransform.position,
                dangerRating = (byte)authoring.dangerRating,
            });
            AddComponent<TradeHandOverRequest>(entity);
            SetComponentEnabled<TradeHandOverRequest>(entity, false);
            // TradeDeliverySystem turns this on only while an accepted order waits here, so E is not swallowed by an idle marker.
            AddComponent<PlayerInteractable>(entity);
            SetComponentEnabled<PlayerInteractable>(entity, false);
        }
    }
}
