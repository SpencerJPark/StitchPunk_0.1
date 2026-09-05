// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// A baked, scene-resident cutscene (amendment A61): the blob and its scene bindings, ready for
    /// a host to hand to <c>CutscenePlaybackApi.CreatePlayRequestFromStage</c>. One entity per
    /// <c>CutsceneAsset</c> staged via <c>CutsceneStageAuthoring</c>.
    /// </summary>
    public struct CutsceneStage : IComponentData
    {
        /// <summary>The baked cutscene, owned by the bake-time <c>BlobAssetStore</c> — never disposed by a reader.</summary>
        public BlobAssetReference<CutsceneBlob> blob;

        /// <summary>The source <c>CutsceneAsset.StableId</c> — how <see cref="CutscenePlaybackApi.TryFindStage"/> finds this stage.</summary>
        public ulong cutsceneKey;
    }

    /// <summary>
    /// One slot's scene binding, baked from the cutscene editor's cast panel (amendment A61). Parallel
    /// in spirit to <see cref="CutsceneActorBinding"/>, but authored rather than host-filled — a host
    /// still may add or overwrite <see cref="CutsceneActorBinding"/> entries after
    /// <c>CreatePlayRequestFromStage</c> copies these in, for actors spawned rather than staged.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct CutsceneStageBinding : IBufferElementData
    {
        /// <summary>The <c>CutsceneSlot.SlotId</c> this entry binds.</summary>
        public uint slotId;

        /// <summary>The actor root (Actor slot) or transform-only entity (Prop slot) baked for this slot, or <c>Entity.Null</c> when the bound object lived outside this stage's subscene.</summary>
        public Entity target;
    }

    /// <summary>
    /// The identity half of a running cutscene request (Phase G §6): which cutscene, and which
    /// playback layer its clip blocks target on every bound actor. Immutable once created — the
    /// mutable half is <see cref="CutsceneControl"/>.
    /// </summary>
    public struct CutscenePlay : IComponentData
    {
        /// <summary>The baked cutscene. Ownership stays with whoever built or cached it — the player never disposes it.</summary>
        public BlobAssetReference<CutsceneBlob> blob;

        /// <summary>Which <c>PlaybackLayer</c> index clip blocks are played on, for every Actor slot.</summary>
        public byte layerIndex;
    }

    /// <summary>
    /// The live control surface a host writes to steer a running cutscene (Phase G §4, §6): pause,
    /// speed, and skip. Created at <see cref="CutscenePlaybackApi.CreatePlayRequest"/> and free for
    /// the host to keep rewriting afterward — there is no second copy of this state anywhere the
    /// player owns.
    /// </summary>
    public struct CutsceneControl : IComponentData
    {
        /// <summary>
        /// Freezes the clock and every bound actor's clip layer (amendment A62 decision A62-D4: the
        /// host is saying "freeze everything"). Distinct from a hold, which freezes only the clock —
        /// looping clips keep cycling under a hold by owner call (Phase G §2).
        /// </summary>
        public bool paused;

        /// <summary>Playback speed multiplier. 1 = normal. Time only ever moves forward (elastic length assumes it); a non-positive value is clamped to 0 by the player, which behaves like <see cref="paused"/>.</summary>
        public float speed;

        /// <summary>Set by the host to request an immediate jump to the cutscene's end (spec §4). The player clears it once processed.</summary>
        public bool skipRequested;
    }

    /// <summary>The player's own advance state for one cutscene request — never written by a host.</summary>
    public struct CutscenePlaybackState : IComponentData
    {
        /// <summary>Index into <see cref="CutsceneBlob.segments"/> of the segment currently playing.</summary>
        public int segmentIndex;

        /// <summary>Seconds elapsed within the current segment (spec §5's <c>(segmentIndex, timeInSegment)</c> clock).</summary>
        public float timeInSegment;

        /// <summary>True while the clock is paused at the current segment's hold, waiting on a matching <see cref="CutsceneHoldRelease"/>.</summary>
        public bool isPausedOnHold;

        /// <summary>True once the cutscene has reached its end or been skipped. The player takes no further action on this request.</summary>
        public bool isComplete;

        /// <summary>Cursor into the current segment's event array — the next not-yet-fired event, never re-read from the start (mirrors this package's existing "advance a cursor, never re-scan" playback convention).</summary>
        public int nextEventIndex;

        /// <summary>
        /// The layer speed last issued to every bound Actor slot via <c>SetSpeed</c> (amendment A62
        /// defect 4). <c>-1</c> means "never applied" — a value no real speed can equal, since the
        /// player clamps a negative <see cref="CutsceneControl.speed"/> to 0 — so the very first
        /// frame always issues at least one <c>SetSpeed</c> even when the host leaves speed at its
        /// default of 1.
        /// </summary>
        public float appliedLayerSpeed;
    }

    /// <summary>
    /// One slot's binding, host-filled (Phase G §6): which live entity plays <c>CutsceneSlot</c>
    /// <paramref name="slotId"/>. Explicit casting, no discovery magic — the toolkit ships no
    /// component that marks "this entity is Bertha" (decision G-D6).
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct CutsceneActorBinding : IBufferElementData
    {
        /// <summary>The <c>CutsceneSlot.SlotId</c> this entry binds.</summary>
        public uint slotId;

        /// <summary>The actor root (Actor slot) or transform-only entity (Prop slot) that plays this slot.</summary>
        public Entity actorEntity;
    }

    /// <summary>
    /// Player-owned per-slot bookkeeping, parallel to <see cref="CutsceneBlob.slots"/> by index (not
    /// by <see cref="CutsceneActorBinding"/> order, which a host controls and this must not depend
    /// on). Internal: a host never reads or writes this directly.
    /// </summary>
    [InternalBufferCapacity(4)]
    internal struct CutsceneSlotRuntimeState : IBufferElementData
    {
        /// <summary>Cursor into the current segment's clip block array for this slot — the next not-yet-issued block.</summary>
        public int nextClipBlockIndex;
    }

    /// <summary>
    /// Releases the hold the cutscene is currently paused on (Phase G §4, §6). The host sets
    /// <see cref="holdId"/> and enables the component; the player consumes and disables it the
    /// frame it matches the current segment's hold — a mismatched id is left enabled and ignored
    /// rather than erroring, so a host that fires a release slightly early or for the wrong hold
    /// simply waits.
    /// </summary>
    public struct CutsceneHoldRelease : IComponentData, IEnableableComponent
    {
        /// <summary>Which hold to release. Must match the current segment's hold id exactly.</summary>
        public FixedString64Bytes holdId;
    }

    /// <summary>
    /// The cutscene camera's current pose (Phase G §6), a world-scoped singleton — only one camera
    /// exists, so only one cutscene's shot ever drives it (multiple concurrent cutscenes are out of
    /// scope, spec §8). The host reads this every frame it wants the camera driven and applies it
    /// however it applies a camera (Cinemachine, in Stitch Punk's case) — the toolkit never touches
    /// <c>Camera.main</c> itself.
    /// </summary>
    public struct CutsceneCameraPose : IComponentData
    {
        public float3 position;
        public quaternion rotation;
        public float fieldOfView;

        /// <summary>True on the exact frame a camera cut marker fires (decision G-D7) — the host's cue to snap rather than let its own camera rig ease toward this pose.</summary>
        public bool isCut;
    }
}
