using Unity.Entities;
using Unity.Mathematics;

public enum TradeOrderState : byte { Offered, Accepted, Delivered, Failed, Expired, Declined }

// Singleton. Orders and relationships are buffers on it, not entities (TR-A3): the save system cannot remap Entity fields.
public struct TradeBoard : IComponentData
{
    public uint worldSeed;   // 0 = pick a random seed on the first roll
}

[InternalBufferCapacity(0)]
public struct TradeOrder : IBufferElementData
{
    public int             orderId;           // weekNumber * 100 + index: deterministic for a given seed
    public int             clientId;          // < 0 = a household one-off (-orderId)
    public int             templateIndex;     // into the client's pool, or the household table
    public int             dropOffId;         // -1 = no drop-off of that kind exists in the scene
    public UnitType        unitType;
    public byte            quantity;
    public float           minHealthFraction;
    public byte            daysAllowed;       // rolled at generation; the due day is set on acceptance
    public byte            dueDayOfWeek;      // absolute 0..6, meaningful once Accepted
    public int             weekIssued;
    public float           difficulty;        // frozen at roll time so the letter and the payout agree
    public int             currencyReward;
    public TradeOrderState state;
}

[InternalBufferCapacity(0)]
public struct ClientRelationship : IBufferElementData
{
    public int clientId;
    public int reputation;   // 0..100
    public int ordersCompleted;
    public int ordersFailed;
}

// One entry per weekly roll; PigeonSpawnSystem (Phase 2) drains it.
[InternalBufferCapacity(0)]
public struct PendingPigeon : IBufferElementData
{
    public int weekNumber;
}

// Throws the current week away and rolls it again with this seed (debug menu; proves same seed -> same week).
public struct TradeRerollRequest : IComponentData, IEnableableComponent
{
    public uint seed;
}

// On each drop-off entity (baked from TradeDropOffAuthoring).
public struct TradeDropOff : IComponentData
{
    public int        dropOffId;
    public DropOffTag tag;
    public float      handOverRadius;
    public float3     standPoint;
    public byte       dangerRating;   // 0..3, feeds difficulty
}

// Written by TradeLetterUI; drained by TradeOrderResponseSystem.
[InternalBufferCapacity(0)]
public struct TradeOrderResponseRequest : IBufferElementData
{
    public int  orderId;
    public bool accept;
}

// On the TradeBoard. Enabled while the letter is on screen; the UI toggles it, PigeonReadSystem sends perched pigeons off when it rises.
public struct TradeLetterOpen : IComponentData, IEnableableComponent { }

// On the TradeBoard (baked from TradeBoardAuthoring). The perch is an offset in the caravan's local space, so no prefab edit is needed.
public struct TradePigeonSettings : IComponentData
{
    public Entity prefab;
    public float3 perchLocalOffset;
    public float flyInStartDistance;
    public float flyInStartHeight;
    public float flyInSeconds;
    public float flyOffSeconds;
    public float arcHeight;
}

public struct CarrierPigeon : IComponentData
{
    public int weekNumber;
}

public enum PigeonFlightPhase : byte { FlyingIn, Perched, FlyingOff }

public struct PigeonFlight : IComponentData
{
    public PigeonFlightPhase phase;
    public float3 startPosition;
    public float3 fallbackPerchPosition;   // used when no caravan exists
    public float3 flyOffVelocity;
    public float elapsedSeconds;
}

// Optional, read by PlayerTargetingSystem: inside priorityRadius (planar, from the origin) this beats a nearer interactable,
// so a pigeon perched on the caravan can still be picked over the caravan's own reach capsule.
public struct PlayerInteractablePriority : IComponentData
{
    public float priorityRadius;
}
