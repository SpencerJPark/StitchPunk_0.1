using Unity.Entities;
using Unity.Mathematics;

public struct OnPlayerReviverEquipt : IComponentData, IEnableableComponent
{
    public ItemType itemType;
}