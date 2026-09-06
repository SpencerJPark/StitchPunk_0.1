// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// A baked, scene-resident cutscene: the blob and its scene bindings, ready for a host to hand
    /// to <c>CutsceneApi.CreatePlayRequestFromStage</c>. One entity per <c>CutsceneAsset</c> staged
    /// via <c>CutsceneStageAuthoring</c>.
    /// </summary>
    public struct CutsceneStage : IComponentData
    {
        public BlobAssetReference<CutsceneBlob> blob; // BlobAssetStore-owned; never disposed by a reader

        public ulong cutsceneKey; // source CutsceneAsset.StableId; how CutsceneApi.TryFindStage finds this stage
    }

    /// <summary>
    /// One slot's scene binding, baked from the cutscene editor's cast panel. A host may still add
    /// or overwrite <see cref="CutsceneActorBinding"/> entries after
    /// <c>CreatePlayRequestFromStage</c> copies these in, for actors spawned rather than staged.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct CutsceneStageBinding : IBufferElementData
    {
        public uint slotId; // CutsceneSlot.SlotId this entry binds

        public Entity target; // the baked actor/transform entity, or Entity.Null when it lived outside this stage's subscene
    }

    /// <summary>
    /// The identity half of a running cutscene request: which cutscene, and which playback layer
    /// its clip blocks target on every bound actor. Immutable once created — the mutable half is
    /// <see cref="CutsceneControl"/>.
    /// </summary>
    public struct CutscenePlay : IComponentData
    {
        public BlobAssetReference<CutsceneBlob> blob; // ownership stays with whoever built/cached it; the player never disposes it

        public byte layerIndex; // PlaybackLayer index clip blocks play on, for every Actor slot
    }

    /// <summary>
    /// The live control surface a host writes to steer a running cutscene: pause, speed, and skip.
    /// Created at <see cref="CutsceneApi.CreatePlayRequest"/> and free for the host to keep
    /// rewriting afterward.
    /// </summary>
    public struct CutsceneControl : IComponentData
    {
        public bool paused; // freezes the clock AND every bound actor's clip layer; a hold freezes only the clock

        public float speed; // 1 = normal; clamped to 0 (behaves like paused) if non-positive; time only moves forward

        public bool skipRequested; // set by the host to jump to the end; cleared by the player once processed
    }

    /// <summary>The player's own advance state for one cutscene request — never written by a host.</summary>
    public struct CutscenePlaybackState : IComponentData
    {
        public int segmentIndex; // index into CutsceneBlob.segments of the segment currently playing

        public float timeInSegment; // seconds elapsed within the current segment

        public bool isPausedOnHold; // true while waiting on a matching CutsceneHoldRelease

        public bool isComplete; // true once ended or skipped; the player takes no further action

        public int nextEventIndex; // cursor into the current segment's event array; never re-scanned from the start

        public float appliedLayerSpeed; // last SetSpeed issued to every bound Actor slot; -1 = never applied (forces one on the first frame)
    }

    /// <summary>One slot's binding, host-filled: which live entity plays <c>CutsceneSlot</c> <paramref name="slotId"/>. Explicit casting; the toolkit has no "this entity is Bertha" marker.</summary>
    [InternalBufferCapacity(4)]
    public struct CutsceneActorBinding : IBufferElementData
    {
        public uint slotId; // CutsceneSlot.SlotId this entry binds

        public Entity actorEntity; // the actor root (Actor slot) or transform-only entity (Prop slot) that plays this slot
    }

    /// <summary>
    /// Player-owned per-slot bookkeeping, parallel to <see cref="CutsceneBlob.slots"/> by index
    /// (never by <see cref="CutsceneActorBinding"/> order, which a host controls). Internal: a host
    /// never reads or writes this directly.
    /// </summary>
    [InternalBufferCapacity(4)]
    internal struct CutsceneSlotRuntimeState : IBufferElementData
    {
        public int nextClipBlockIndex; // cursor into the current segment's clip block array

        public int nextAttachMarkerIndex; // cursor into the current segment's attach marker array

        public int attachedHostSlotIndex; // index into CutsceneBlob.slots of the host being ridden; -1 = free. While set, this slot's root lane is ignored.

        public uint attachedSocketId; // socket the current attachment rides; 0 = root attach

        public bool isHiddenByAttachment;

        public int nextMarkIndex; // cursor into the current segment's mark array

        public ulong activeVariantClipId; // clip id currently playing after facing has had its say; what a re-pick compares against

        public int activeBlockSegmentIndex; // segment of the block this slot is playing, or -1 for "nothing playing"; kept rather than derived because nextClipBlockIndex rebases at every hold

        public int activeBlockIndex; // index into the active block's own segment array; meaningless while activeBlockSegmentIndex is -1

        public float activeBlockSpeed; // active block's authored speed; a later host SetSpeed multiplies it rather than replacing it; 1 while nothing plays

        public bool hasOutstandingMark; // ordered to a mark not yet reached; while set this slot's root lane is ignored. Survives a hold.
    }

    /// <summary>
    /// Enabled on a bound entity when its slot's mark time is reached: the host walks the entity
    /// there through whatever movement it has, and the toolkit judges arrival by distance — or
    /// teleports on timeout so a stuck mover cannot softlock a rendezvous hold.
    /// </summary>
    public struct CutsceneMoveToMark : IComponentData, IEnableableComponent
    {
        public float3 position;

        public float facingRadians; // applied by the toolkit only when the mark times out

        public float toleranceMeters; // XZ distance that counts as arrived; Y is ignored

        public float timeoutSeconds; // 0 waits forever; otherwise resolves by teleport after this long

        public float elapsedSeconds; // written by the player only; frozen while the cutscene is paused
    }

    /// <summary>
    /// The facing a cutscene is driving a bound Actor slot toward, written every frame the
    /// cutscene has an answer and disabled when it ends. The host maps it onto its own facing
    /// model; the toolkit never writes <c>PartFacing</c> itself.
    /// </summary>
    public struct CutsceneFacing : IComponentData, IEnableableComponent
    {
        // Degrees about world up, measured from +X toward +Z (0 = east, 90 = north) — the Direction
        // Sets/FacingResolver.FromMovement convention, NOT a LocalTransform Y euler, which measures
        // from +Z instead.
        public float angleDegrees;
    }

    /// <summary>
    /// Enabled on the detached entity for the frame a Detach marker fires. The host reads it and
    /// disables it — a sellable package cannot assume a physics stack, so the toolkit hands over an
    /// impulse and applies none of its own.
    /// </summary>
    public struct CutsceneDetachSignal : IComponentData, IEnableableComponent
    {
        public float3 worldImpulse; // authored impulse, rotated out of host space at the instant of detachment

        public Entity previousHost; // whatever the entity was riding, so a host can credit the throw to it
    }

    /// <summary>
    /// Releases the hold the cutscene is currently paused on. The host sets <see cref="holdId"/>
    /// and enables the component; the player consumes and disables it the frame it matches the
    /// current segment's hold. A mismatched id is left enabled and ignored, not an error.
    /// </summary>
    public struct CutsceneHoldRelease : IComponentData, IEnableableComponent
    {
        public FixedString64Bytes holdId; // must match the current segment's hold id exactly
    }

    /// <summary>
    /// The cutscene camera's current pose, a world-scoped singleton (only one camera, so only one
    /// cutscene's shot ever drives it). The host reads this every frame and applies it however it
    /// applies a camera; the toolkit never touches <c>Camera.main</c> itself.
    /// </summary>
    public struct CutsceneCameraPose : IComponentData
    {
        public float3 position;
        public quaternion rotation;
        public float fieldOfView;

        public bool isCut; // true on the exact frame a camera cut marker fires; the host's cue to snap instead of easing

        // True while a running, incomplete cutscene's current segment has a camera lane to write a
        // pose from. Cleared every frame before any cutscene runs, so "no camera keys" or "just
        // ended/skipped" reads as not-driven instead of leaving a stale pose. Apply position/
        // rotation/fieldOfView only while this is true.
        public bool isDriven;
    }
}
