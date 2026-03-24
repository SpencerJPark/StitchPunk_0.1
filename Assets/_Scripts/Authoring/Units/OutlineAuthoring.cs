using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class OutlineAuthoring : MonoBehaviour
{
    public bool outlined = false;
    
    [SerializeField] 
    private Color outlineColor = Color.white;
    
    [SerializeField, Range(0f, 10f)] 
    private float outlineWidth = 2f;

    public class Baker : Baker<OutlineAuthoring>
    {
        public override void Bake(OutlineAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new Outline
            {
                outlineColor = new float4(
                    authoring.outlineColor.r, 
                    authoring.outlineColor.g, 
                    authoring.outlineColor.b, 
                    authoring.outlineColor.a),
                outlineWidth = authoring.outlineWidth,
            });
            SetComponentEnabled<Outline>(entity, authoring.outlined);
        }
    }
}


