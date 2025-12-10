using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class OutlineAuthoring : MonoBehaviour
{
    [SerializeField] 
    private OutlineMode outlineMode = OutlineMode.OutlineAll;
    
    [SerializeField] 
    private Color outlineColor = Color.white;
    
    [SerializeField, Range(0f, 10f)] 
    private float outlineWidth = 2f;

    public class Baker : Baker<OutlineAuthoring>
    {
        public override void Bake(OutlineAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new OutlineComponent
            {
                outlineMode = authoring.outlineMode,
                outlineColor = new float4(
                    authoring.outlineColor.r, 
                    authoring.outlineColor.g, 
                    authoring.outlineColor.b, 
                    authoring.outlineColor.a),
                outlineWidth = authoring.outlineWidth,
                needsUpdate = true
            });
        }
    }
}

public enum OutlineMode : byte
{
    OutlineAll,
    OutlineVisible,
    OutlineHidden,
    OutlineAndSilhouette,
    SilhouetteOnly
}

/// <summary>
/// Component that enables outline rendering on an entity.
/// Add this component to any entity that should have an outline.
/// </summary>
public struct OutlineComponent : IComponentData
{
    public OutlineMode outlineMode;
    public float4 outlineColor;
    public float outlineWidth;
    public bool needsUpdate;
}

/// <summary>
/// Tag component to mark entities that have had their outline initialized.
/// </summary>
public struct OutlineInitialized : IComponentData
{
}

/// <summary>
/// Stores the original material count before outline materials were added.
/// Used for cleanup when outline is removed.
/// </summary>
public struct OutlineOriginalMaterialCount : IComponentData
{
    public int count;
}

/// <summary>
/// Marks entities that need their material properties applied on the main thread.
/// </summary>
public struct OutlineNeedsMaterialUpdate : IComponentData, IEnableableComponent
{
}

/// <summary>
/// Processed outline data ready to be applied to materials.
/// This is computed in parallel, then consumed on main thread.
/// </summary>
public struct OutlineProcessedData : IComponentData
{
    public float4 color;
    public float outlineWidth;
    public float maskZTest;
    public float fillZTest;
}