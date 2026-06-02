using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;


public struct PlayerInteractable : IComponentData, IEnableableComponent { }



// Requests
public struct ReleaseRequest : IComponentData, IEnableableComponent
{
    public Entity interactionEntity;
}