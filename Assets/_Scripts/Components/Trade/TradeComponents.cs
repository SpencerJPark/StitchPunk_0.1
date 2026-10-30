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

// On the TradeBoard (baked from TradeBoardAuthoring).
public struct TradePigeonSettings : IComponentData
{
    public Entity prefab;
    public float3 fallbackArmLocalOffset;   // in the player's local space, used when the scene has no PigeonArmTarget
    public float flyInStartDistance;
    public float flyInStartHeight;
    public float flyInSeconds;
    public float flyOffSeconds;
    public float arcHeight;
    public float letterOpenDelaySeconds;    // landed -> letter opens; the window the player's reach animation will fill
}

// Tag on a child entity of the player (PigeonArmTargetAuthoring). Its world position is where the pigeon lands, so it can be
// moved in the editor and later driven by the arm-out animation.
public struct PigeonArmTarget : IComponentData { }

public struct CarrierPigeon : IComponentData
{
    public int weekNumber;
}

public enum PigeonFlightPhase : byte { FlyingIn, Perched, FlyingOff }

public struct PigeonFlight : IComponentData
{
    public PigeonFlightPhase phase;
    public float3 startPosition;
    public float3 flyOffVelocity;
    public float elapsedSeconds;     // time in the current phase
    public bool letterDelivered;     // the letter has been opened; closing it sends the pigeon off
}

// Enabled on a drop-off when the player presses E at it (DropOffInteractSystem); TradeDeliverySystem answers and disables it.
public struct TradeHandOverRequest : IComponentData, IEnableableComponent { }

// On a unit the player handed over. It is no longer a Minion: it walks to its stand point, idles, and is despawned once the
// player is far away and cannot see it (DeliveredUnitDespawnSystem). It stays a normal unit meanwhile (TR-D16).
public struct DeliveredToClient : IComponentData
{
    public int dropOffId;
    public int orderId;
    public float3 standPoint;
    public float secondsSinceHandOver;
    public bool walkOrdered;
}

public enum TradeToastKind : byte { None, HandOverRefused, HandOverDone, OrderFailed }

// Last notable trade result, shown for a few seconds by TradeOrdersHudUI. A new sequence number means a new message.
public struct TradeToast : IComponentData
{
    public int sequence;
    public TradeToastKind kind;
    public int orderId;
    public int amount;   // refused: units still missing; done: Currency paid
}

// Save mirrors, on the Player (its save identity), added at runtime by TradeSaveSyncSystem. The generic save copies IPersist
// components byte for byte, so they are flat unmanaged structs (no Entity), flags are bytes, and the board buffers ride in FixedLists
// (Trade_System.md TR-A10). hasData is 1 while a snapshot is waiting to be applied by TradeLoadSyncSystem.
public struct WorldClockSnapshot : IComponentData, IPersist
{
    public byte  hasData;
    public float timeOfDay01;
    public int   dayOfWeek;
    public int   weekNumber;
}

public struct TradeBoardSnapshot : IComponentData, IPersist
{
    public byte hasData;
    public uint worldSeed;
    public Unity.Collections.FixedList4096Bytes<TradeOrder> orders;               // about 90 orders; a week holds about 12
    public Unity.Collections.FixedList512Bytes<ClientRelationship> relationships; // about 30 clients
}
