using Unity.Entities;
using Unity.Mathematics;

public struct Item : IComponentData { }

public struct Socket : IComponentData
{
    public float3 LocalPosition;
    public quaternion LocalRotation;
    public Entity AttachedItem;
}

public struct AttachedToSocket : IComponentData
{
    public Entity SocketOwner;
    public int SocketIndex;
}