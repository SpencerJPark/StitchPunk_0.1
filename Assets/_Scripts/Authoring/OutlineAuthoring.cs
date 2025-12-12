using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class OutlineAuthoring : MonoBehaviour
{
    
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
                onUpdateVisual = true
            });
        }
    }
}


/// <summary>
/// Component that enables outline rendering on an entity.
/// Add this component to any entity that should have an outline.
/// </summary>
public struct Outline : IComponentData
{
    public float4 outlineColor;
    public float outlineWidth;
    public bool onUpdateVisual;
}
