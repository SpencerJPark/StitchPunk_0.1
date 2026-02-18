using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;

/// <summary>
/// System that updates the RenderFilterSettings layer when OutlinedTag is enabled/disabled
/// This ensures entities render to the correct camera (outline camera vs main camera)
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
partial struct OutlineLayerUpdateSystem : ISystem
{
    private ComponentTypeHandle<OutlinedTag> outlinedTagHandle;
    private EntityQuery enabledQuery;
    private EntityQuery disabledQuery;
    
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<OutlinedTag>();
        
        // Query for entities where OutlinedTag was just enabled
        enabledQuery = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(OutlinedTag) },
            Options = EntityQueryOptions.IgnoreComponentEnabledState
        });
        
        disabledQuery = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(OutlinedTag) },
            Options = EntityQueryOptions.IgnoreComponentEnabledState
        });
    }
    
    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        
        // Process all entities with OutlinedTag
        NativeArray<Entity> entities = enabledQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        foreach (Entity entity in entities)
        {
            if (!entityManager.Exists(entity))
                continue;
                
            bool isEnabled = entityManager.IsComponentEnabled<OutlinedTag>(entity);
            
            // Get current RenderFilterSettings (it's a shared component)
            if (entityManager.HasComponent<RenderFilterSettings>(entity))
            {
                RenderFilterSettings renderFilterSettings = entityManager.GetSharedComponentManaged<RenderFilterSettings>(entity);
                
                byte targetLayer = isEnabled ? (byte)GlobalGameData.OUTLINE_LAYER : (byte)GlobalGameData.DEFAULT_LAYER;
                
                // Only update if layer changed
                if (renderFilterSettings.Layer != targetLayer)
                {
                    renderFilterSettings.Layer = targetLayer;
                    entityManager.SetSharedComponentManaged(entity, renderFilterSettings);
                }
            }
        }
        
        entities.Dispose();
    }
}