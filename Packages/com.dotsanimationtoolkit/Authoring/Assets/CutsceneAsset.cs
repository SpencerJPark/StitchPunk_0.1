// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// A multi-actor timeline (Phase G §1): named actor/prop slots, clip blocks, root motion, facing
    /// overrides, per-part keys, a camera lane, an event lane and hold markers, staged against a
    /// remembered scene and baked to a <c>CutsceneBlob</c> for ECS playback. Authoring happens here
    /// and in Unity's own Scene view (spec §3); this asset carries no viewport of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every lane's <c>time</c> is raw authored seconds along one flat timeline — hold points are
    /// markers on that same timeline, not a break in it. Splitting the timeline into
    /// <c>(segmentIndex, timeInSegment)</c> pairs is bake-only work (spec §5); nothing in this asset
    /// assumes a fixed end time, and nothing here does <c>time * frameRate</c> arithmetic the way a
    /// clip's normalized time does — nothing in this asset is normalized against a duration, because
    /// a cutscene's duration is elastic by design.
    /// </para>
    /// <para>
    /// <strong>Carries a stable id like <see cref="RigAsset"/>/<see cref="ClipAsset"/>/
    /// <see cref="ClipSetAsset"/>, unlike <see cref="DirectionSetAsset"/>.</strong> Those three feed a
    /// content-hashed dedup key at bake (<c>ClipRegistryBuilder.ComposeBindKey</c>); a direction set
    /// never bakes anything of its own and so never needed one. A cutscene bakes to
    /// <c>CutsceneBlob</c> (spec §5), so it needs the same identity the others do.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewCutscene",
        menuName = "DOTS Animation Toolkit/Cutscene Asset",
        order = 4)]
    public sealed class CutsceneAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong stableId;

        /// <summary>
        /// GUID of the scene this cutscene is authored against (spec §3). Authoritative; opening the
        /// cutscene resolves this to find (and offer to open) the scene.
        /// </summary>
        public string sceneGuid = string.Empty;

        /// <summary>Display-only scene path, for the inspector and the "open this scene?" prompt. May go stale if the scene is moved; <see cref="sceneGuid"/> is what resolution actually reads.</summary>
        public string scenePath = string.Empty;

        /// <summary>The abstract actor/prop slots this cutscene stages (spec §3).</summary>
        public List<CutsceneSlot> slots = new List<CutsceneSlot>();

        /// <summary>The camera's keyed pose/FOV curve and hard-cut markers (spec §2).</summary>
        public CutsceneCameraLane cameraLane = new CutsceneCameraLane();

        /// <summary>Typed markers on the shared timeline, using the same <c>AnimEventKeyRegistry</c> vocabulary clips use (spec §2).</summary>
        public List<CutsceneEventMarker> events = new List<CutsceneEventMarker>();

        /// <summary>Points where the clock pauses until the host releases it (spec §2). Looping clips and the camera hold their state; nothing here assumes a hold ever resolves within a fixed time.</summary>
        public List<CutsceneHoldMarker> holdMarkers = new List<CutsceneHoldMarker>();

        /// <summary>
        /// Editor-only slot→GameObject bindings, one entry per scene this cutscene has been opened
        /// against (spec §5). Stored as strings so <c>Authoring/</c> never references the editor
        /// assembly (Conformance_C) — only editor code ever parses a
        /// <c>GlobalObjectId</c> out of <see cref="CutsceneSlotBindingEntry.globalObjectId"/>.
        /// </summary>
        public List<CutsceneSceneBinding> sceneBindings = new List<CutsceneSceneBinding>();

        /// <summary>This cutscene's stable 64-bit identity, folded into its bake dedup key like the other identity-bearing assets.</summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        /// <summary>
        /// Assigns a fresh stable id to this asset and to every slot still carrying the reserved 0
        /// value. Idempotent, mirroring <see cref="RigAsset.EnsureStableIds"/> — the same
        /// "duplicate copies the id, a real collision is separated later" contract applies to slots.
        /// </summary>
        /// <remarks>
        /// <strong>Public for the same reason <see cref="RigAsset.EnsureStableIds"/> is.</strong> A
        /// cutscene built from code — <c>CreateInstance</c>, populate <see cref="slots"/>, save — hits
        /// no lifecycle hook between populating the list and saving it, so the slots it just added
        /// would otherwise reach disk with every id still 0. Call after populating
        /// <see cref="slots"/> and before reading any <see cref="CutsceneSlot.SlotId"/>.
        /// </remarks>
        public void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdUtility.NewAssetStableId();
                hasUnpersistedStableId = true;
            }

            if (slots != null)
            {
                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    CutsceneSlot slot = slots[slotIndex];
                    if (slot != null && slot.EnsureStableId())
                    {
                        hasUnpersistedStableId = true;
                    }
                }
            }
        }

        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself (amendment A14).
        [NonSerialized] private bool hasUnpersistedStableId;

        /// <inheritdoc />
        public bool HasUnpersistedStableId
        {
            get { return hasUnpersistedStableId; }
        }

        /// <inheritdoc />
        public void MarkStableIdPersisted()
        {
            hasUnpersistedStableId = false;
        }

        // Unity raises Awake when an instance is created and OnEnable both after creation and after
        // an asset is deserialized, so between them no asset can reach an inspector or a bake without
        // an id. Both funnel into the same idempotent assignment.
        private void Awake()
        {
            EnsureStableIds();
        }

        private void OnEnable()
        {
            EnsureStableIds();
        }

        private void OnValidate()
        {
            EnsureStableIds();
        }

        private void Reset()
        {
            EnsureStableIds();
        }
    }

    /// <summary>
    /// One named, recastable slot a cutscene stages (spec §3): an abstract role ("Bertha",
    /// "Minion A") pinning a rig and clip sets for an <see cref="CutsceneSlotKind.Actor"/>, or nothing
    /// but a transform lane for a <see cref="CutsceneSlotKind.Prop"/>.
    /// </summary>
    [Serializable]
    public sealed class CutsceneSlot
    {
        /// <summary>Freely renameable label shown in the timeline and inspector. Never the binding key — <see cref="SlotId"/> is.</summary>
        public string name = string.Empty;

        [SerializeField] internal uint slotId;

        /// <summary>Whether this slot plays clips on a rig or is a bare transform target.</summary>
        public CutsceneSlotKind kind = CutsceneSlotKind.Actor;

        /// <summary>The rig this slot's clip blocks and part tracks resolve against. Ignored for <see cref="CutsceneSlotKind.Prop"/>.</summary>
        public RigAsset rig;

        /// <summary>The clip sets a clip block's <c>clipId</c> may resolve against — the same (rig, clip sets) bind an actor's own registry bakes from (spec §5). Ignored for <see cref="CutsceneSlotKind.Prop"/>.</summary>
        public List<ClipSetAsset> clipSets = new List<ClipSetAsset>();

        /// <summary>Optional set consulted when a facing has no override key active. Ignored for <see cref="CutsceneSlotKind.Prop"/>.</summary>
        public DirectionSetAsset directionSet;

        /// <summary>
        /// The prefab the cast panel's <em>Place in Scene</em> stages this slot from (amendment A58
        /// §3.2), so an empty scene can be dressed into a full cutscene without leaving the tool.
        /// Optional — binding an already-placed GameObject by hand still works, and a slot that was
        /// bound that way never needs one.
        /// </summary>
        /// <remarks>
        /// A plain asset reference, which <c>Authoring/</c> may hold; placing it is editor work and
        /// lives in the Editor assembly (Conformance_C). Nothing at run time reads this — a baked
        /// cutscene binds entities the host supplies, never a prefab this asset names.
        /// </remarks>
        public GameObject actorPrefab;

        /// <summary>Which clip plays when, and whether it loops (spec §2). Overlapping blocks are a crossfade; touching blocks are a hard cut — both derived from <see cref="CutsceneClipBlock.start"/>/<see cref="CutsceneClipBlock.duration"/> at bake, never authored as a separate blend field.</summary>
        public List<CutsceneClipBlock> clipBlocks = new List<CutsceneClipBlock>();

        /// <summary>
        /// The slot's own transform through the scene: root motion for an
        /// <see cref="CutsceneSlotKind.Actor"/> (spec §2 — "clips play in place; root keys move the
        /// actor"), or the entire authored motion for a <see cref="CutsceneSlotKind.Prop"/>, which has
        /// no clip lane to separate it from.
        /// </summary>
        public List<CutsceneTransformKey> transformKeys = new List<CutsceneTransformKey>();

        /// <summary>Explicit facing overrides ("face the camera during this line"); absent a key here, facing derives from root travel direction (spec §2). Ignored for <see cref="CutsceneSlotKind.Prop"/>.</summary>
        public List<CutsceneFacingKey> facingKeys = new List<CutsceneFacingKey>();

        /// <summary>Per-part key lanes that layer, Override, over whatever clip is currently playing (spec §2, like <c>ApplyHeldTargetPose</c>). Ignored for <see cref="CutsceneSlotKind.Prop"/>.</summary>
        public List<CutsceneKeyedTrack> partTracks = new List<CutsceneKeyedTrack>();

        /// <summary>When this slot rides another slot, and when it lets go (amendment A63 §3.1). Authored on Actor and Prop slots alike — a crate rides a cart exactly as an actor does.</summary>
        public List<CutsceneAttachMarker> attachMarkers = new List<CutsceneAttachMarker>();

        /// <summary>Spots this slot must reach before a rendezvous hold releases (amendment A64 §3.1). Authored on Prop slots too — a self-driving cart is an actor without a rig, and the host decides what "move" means.</summary>
        public List<CutsceneMarkKey> markKeys = new List<CutsceneMarkKey>();

        /// <summary>This slot's stable 32-bit identity within the owning cutscene — what a host's <c>CutsceneActorBinding</c> buffer resolves against (spec §6), never <see cref="name"/> or list position.</summary>
        public uint SlotId
        {
            get { return slotId; }
        }

        /// <summary>
        /// Assigns a fresh stable id when this slot still carries the reserved 0 value.
        /// </summary>
        /// <returns>True when a fresh id was minted, so the owning asset knows to flag itself unpersisted.</returns>
        internal bool EnsureStableId()
        {
            if (slotId != 0u)
            {
                return false;
            }
            // Same 32-bit space RigAsset mints target/socket/billboard-root/ragdoll-body ids from
            // (per-owning-asset scope, not project-wide) — one generator, and collisions across the
            // two owners are harmless because a slot id and a target id are never compared.
            slotId = StableIdUtility.NewTargetStableId();
            return true;
        }
    }

    /// <summary>One clip block on a slot's clip lane (spec §2): names a clip, when it plays, how long, and whether it loops.</summary>
    [Serializable]
    public sealed class CutsceneClipBlock
    {
        /// <summary>The clip's stable id, resolved at bake against the slot's (rig, clip sets) bind (spec §5).</summary>
        public ulong clipId;

        /// <summary>Block start, in raw timeline seconds.</summary>
        public float start;

        /// <summary>Block length in seconds. Two adjacent blocks overlapping by part of this span cross-fade over the overlap; blocks that merely touch are a hard cut (spec §2).</summary>
        [Min(0f)] public float duration;

        /// <summary>Whether the clip loops for the block's duration rather than playing once.</summary>
        public bool loop;

        /// <summary>
        /// Playback speed for this block's clip (amendment A65 §3.3), multiplied into whatever speed
        /// the host has the cutscene running at. Floored well above 0 — a stopped clip is
        /// <c>CutsceneControl.paused</c>, and 0 in the baked blob means "an older bake, no opinion".
        /// </summary>
        [Min(0.01f)] public float speed = 1f;

        /// <summary>Seconds into the clip the block starts, for playing the second half of a swing.</summary>
        [Min(0f)] public float clipStartOffsetSeconds;
    }

    /// <summary>
    /// One transform key on a cutscene timeline (spec §2): a <see cref="ClipAsset"/>
    /// <see cref="TransformKey"/> reshaped for absolute seconds rather than a clip's normalized
    /// [0, 1] duration fraction, because a cutscene's timeline has no fixed duration to normalize
    /// against.
    /// </summary>
    [Serializable]
    public struct CutsceneTransformKey
    {
        /// <summary>Key time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>Local offset, matching <see cref="TransformKey.position"/>.</summary>
        public float3 position;

        /// <summary>Local rotation in degrees, Euler ZXY — matching <see cref="TransformKey.rotation"/>, so a typed angle means the same thing everywhere in this toolkit.</summary>
        public float3 rotation;

        /// <summary>Non-uniform x/y/z scale.</summary>
        public float3 scale;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight), in segment space; read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>One facing override key (spec §2, decision G-D3): a direction angle, not a discrete <see cref="Direction"/> — the same continuous model the Direction Sets pane's 0–360° slider already uses, resolved through the same <c>FacingResolver</c> call site family.</summary>
    [Serializable]
    public struct CutsceneFacingKey
    {
        /// <summary>Key time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The facing angle, degrees, 0–360.</summary>
        [Range(0f, 360f)] public float angleDegrees;
    }

    /// <summary>
    /// One per-part key lane on an actor slot (spec §2): keys addressed by a rig target's tag, layering
    /// Override over the composited pose from whatever the clip lane is currently playing.
    /// </summary>
    /// <remarks>
    /// <strong>Tag-addressed only — no <see cref="TransformTrack.targetId"/> fallback.</strong> Spec §5
    /// is explicit that a slot recast to a different rig keeps its keys wherever tags line up (T2's
    /// lenient skip-with-warning, Phase E/F rules); a raw target id has no meaning once the rig
    /// changes out from under it, where the whole point of a cutscene slot is that it can be recast.
    /// </remarks>
    [Serializable]
    public sealed class CutsceneKeyedTrack
    {
        /// <summary>The role this track poses, resolved against the slot's rig at bake/preview time (rule T2: unresolved is a warning and a skip, never an error).</summary>
        public uint tagId;

        /// <summary>Which pose channels this track owns; channels outside the mask are left to the composited clip beneath it.</summary>
        public AnimatedChannels channels = AnimatedChannels.PositionXY;

        /// <summary>Keys in ascending <see cref="CutsceneTransformKey.time"/> order.</summary>
        public List<CutsceneTransformKey> keys = new List<CutsceneTransformKey>();
    }

    /// <summary>The cutscene camera's keyed pose/FOV curve and hard-cut markers (spec §2, §4).</summary>
    /// <remarks>Continuous by default — "one camera just moving around the scene" — with <see cref="cutMarkers"/> as the named exception, never the rule.</remarks>
    [Serializable]
    public sealed class CutsceneCameraLane
    {
        /// <summary>Keys in ascending <see cref="CutsceneCameraKey.time"/> order.</summary>
        public List<CutsceneCameraKey> keys = new List<CutsceneCameraKey>();

        /// <summary>Times where the camera pose is a hard cut rather than an interpolated move between the surrounding keys.</summary>
        public List<CutsceneCameraCutMarker> cutMarkers = new List<CutsceneCameraCutMarker>();
    }

    /// <summary>One camera pose key (spec §2): position, rotation and field of view.</summary>
    [Serializable]
    public struct CutsceneCameraKey
    {
        /// <summary>Key time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>World-space position.</summary>
        public float3 position;

        /// <summary>World-space rotation in degrees, Euler ZXY — matching <see cref="TransformKey.rotation"/>'s authored-angle convention, even though this key is usually written by "align to Scene view camera" rather than typed by hand.</summary>
        public float3 rotation;

        /// <summary>
        /// Vertical field of view in degrees. The struct's zero value is not a usable FOV; the
        /// editor's Add Key path is responsible for writing a real one (default 60°), the same
        /// contract <see cref="TransformKey.scale"/>'s zero-is-not-meaningful convention documents.
        /// </summary>
        public float fieldOfView;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>One hard-cut marker on the camera lane (spec §2): at this time the camera pose snaps rather than interpolating from the previous key.</summary>
    [Serializable]
    public struct CutsceneCameraCutMarker
    {
        /// <summary>Marker time, in raw timeline seconds.</summary>
        public float time;
    }

    /// <summary>One typed marker on the cutscene's event lane (spec §2), using the same <c>AnimEventKeyRegistry</c> vocabulary and payload shape as <see cref="EventMarker"/>.</summary>
    /// <remarks>
    /// A class rather than a struct, unlike <see cref="EventMarker"/> — decision G-D4 fixes
    /// <see cref="fireOnSkip"/> default <em>on</em>, and only a field initializer gets that for free
    /// when Unity's list-element UI constructs a fresh row; a struct's zero value would default it off.
    /// </remarks>
    [Serializable]
    public sealed class CutsceneEventMarker
    {
        /// <summary>Marker time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>User event key, same vocabulary as <see cref="EventMarker.eventKey"/>.</summary>
        public uint eventKey;

        /// <summary>User integer payload delivered with the emitted event.</summary>
        public int intParam;

        /// <summary>User float payload delivered with the emitted event.</summary>
        public float floatParam;

        /// <summary>
        /// Whether a skip still fires this event (decision G-D4). Default on: a skipped cutscene must
        /// leave the same world state as a watched one unless a marker explicitly opts out.
        /// </summary>
        public bool fireOnSkip = true;

        /// <summary>
        /// Makes this event a <em>cue</em> (amendment A65 §3.1, decision A65-D1): the clock pauses
        /// the instant the event fires and resumes when the host releases a hold named after the
        /// event's registry name. One marker instead of an event plus a hold whose id an author has
        /// to keep matching by hand.
        /// </summary>
        public bool holdUntilReleased;
    }

    /// <summary>
    /// One hold point (spec §2, decision G-D5): the clock pauses here until the host releases it by
    /// <see cref="holdId"/>. Cutscene length is therefore elastic, never a fixed end time.
    /// </summary>
    /// <remarks>
    /// <strong>Plain string id, not an <see cref="IVocabularyRegistry"/> vocabulary (decision G-D5).</strong>
    /// A hold is released by whatever host code is waiting on it — a dialogue system advancing a line,
    /// a UI button — and unlike a tag or an event key, nothing resolves a hold id against a shared
    /// project vocabulary or bakes it into a dense index; it is compared for equality exactly once, by
    /// the host, against a control component it wrote itself. A registry's dropdown-only selection and
    /// duplicate-guard machinery exists to keep a *shared* vocabulary from drifting — with no shared
    /// resolution step for hold ids to drift out of sync with, that machinery would be pure overhead.
    /// </remarks>
    [Serializable]
    public sealed class CutsceneHoldMarker
    {
        /// <summary>Hold time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The id a <c>CutsceneHoldRelease</c> names to release this specific hold.</summary>
        public string holdId = string.Empty;

        /// <summary>
        /// Makes this a <em>rendezvous</em> hold (amendment A64 §3.1): the clock resumes on its own
        /// once every slot with an outstanding mark has arrived. A host's own
        /// <c>CutsceneHoldRelease</c> still overrides it — leaving without them stays possible.
        /// </summary>
        public bool autoReleaseWhenMarksReached = true;
    }

    /// <summary>
    /// One spot a slot is ordered to move to (amendment A64 §3.1). The toolkit issues the order and
    /// judges arrival; what walks the entity there is the host's pathfinding (decision A64-D1).
    /// </summary>
    /// <remarks>
    /// A struct, unlike <see cref="CutsceneAttachMarker"/>: every field's zero is either a sane
    /// default or explicitly means "off" except the two the editor's own Add path writes
    /// (<see cref="toleranceMeters"/>, <see cref="previewTravelSeconds"/>), which a fresh row from
    /// Unity's list UI would otherwise leave at 0 — the same contract
    /// <see cref="CutsceneCameraKey.fieldOfView"/> documents.
    /// </remarks>
    [Serializable]
    public struct CutsceneMarkKey
    {
        /// <summary>When the move order is issued, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The world position to reach.</summary>
        public float3 position;

        /// <summary>Arrival facing, same 0–360 model as <see cref="CutsceneFacingKey"/>.</summary>
        [Range(0f, 360f)] public float facingDegrees;

        /// <summary>XZ distance that counts as "there". Authored default 0.5.</summary>
        public float toleranceMeters;

        /// <summary>0 waits forever; otherwise the mark resolves by teleport after this many real seconds (decision A64-D3, so a paused cutscene never ticks it down).</summary>
        public float timeoutSeconds;

        /// <summary>Editor-only rehearsal of how long the walk takes; also where the merged root key lands (decision A64-D2). Authored default 2.</summary>
        public float previewTravelSeconds;
    }

    /// <summary>
    /// One attach/detach moment on a slot's attach lane (amendment A63 §3.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Attach while already attached is a hand-over</strong>, not an error: the previous
    /// binding is dropped silently — no <c>CutsceneDetachSignal</c>, no impulse — and the new one
    /// takes its place. Two markers at the same instant apply in authored order.
    /// </para>
    /// <para>
    /// <strong>An attached slot's root lane is ignored</strong> for as long as the attachment lasts;
    /// the host owns the transform. Clip blocks and part tracks keep playing, so an actor riding a
    /// cart can still wave. <see cref="CutsceneAttachKind.Detach"/> leaves the slot at the world pose
    /// it was let go at (Phase G §4, "actors stay where the cutscene left them") and the root lane
    /// resumes from its next key.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class CutsceneAttachMarker
    {
        /// <summary>Marker time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>Whether this marker binds the slot to a host or releases it.</summary>
        public CutsceneAttachKind kind = CutsceneAttachKind.Attach;

        /// <summary>Attach only: the <see cref="CutsceneSlot.SlotId"/> of the slot this one rides.</summary>
        public uint hostSlotId;

        /// <summary>A <c>SocketDefinition.Id</c> on the host slot's rig, or 0 for the host's root.</summary>
        public uint socketId;

        /// <summary>Extra offset in socket space, or host-root space for a root attach.</summary>
        public float3 localOffset;

        /// <summary>Root attach only — a socket carries its own rotation, which this would fight.</summary>
        public float3 localEulerDegrees;

        /// <summary>Hides the slot's renderers while the attachment lasts: riders inside a cart.</summary>
        public bool hideWhileAttached;

        /// <summary>Detach only: an impulse in host space, rotated to world at detach time and handed to the host through <c>CutsceneDetachSignal</c> (decision A63-D2 — the toolkit applies no physics of its own).</summary>
        public float3 detachImpulse;
    }

    /// <summary>Editor-only slot→GameObject bindings for one scene (spec §5).</summary>
    [Serializable]
    public sealed class CutsceneSceneBinding
    {
        /// <summary>GUID of the scene these bindings apply to.</summary>
        public string sceneGuid = string.Empty;

        /// <summary>One entry per bound slot.</summary>
        public List<CutsceneSlotBindingEntry> slotBindings = new List<CutsceneSlotBindingEntry>();
    }

    /// <summary>One slot's binding within a <see cref="CutsceneSceneBinding"/>.</summary>
    [Serializable]
    public sealed class CutsceneSlotBindingEntry
    {
        /// <summary>The bound <see cref="CutsceneSlot.SlotId"/>.</summary>
        public uint slotId;

        /// <summary>
        /// <c>GlobalObjectId.ToString()</c> of the bound GameObject. Stored as a string, never the
        /// editor-only identifier type itself, so this stays parseable by editor code only while the
        /// asset itself carries no editor-assembly reference (Conformance_C).
        /// </summary>
        public string globalObjectId = string.Empty;
    }
}
