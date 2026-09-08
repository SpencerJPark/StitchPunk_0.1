// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// A multi-actor timeline: named actor/prop slots, clip blocks, root motion, facing overrides,
    /// per-part keys, a camera lane, an event lane and hold markers, staged against a remembered
    /// scene and baked to a <c>CutsceneBlob</c> for ECS playback.
    /// </summary>
    // Every lane's time is raw authored seconds along one flat timeline — hold points are markers
    // on it, not a break in it. Nothing here is normalized against a duration, since a cutscene's
    // duration is elastic by design; splitting into segments is bake-only work.
    [CreateAssetMenu(
        fileName = "NewCutscene",
        menuName = "DOTS Animation Toolkit/Cutscene Asset",
        order = 4)]
    public sealed class CutsceneAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong stableId;

        [Tooltip("GUID of the scene this cutscene is authored against. Authoritative; opening the cutscene resolves this to find the scene.")]
        public string sceneGuid = string.Empty;

        [Tooltip("Display-only scene path. May go stale if the scene is moved; sceneGuid is what resolution actually reads.")]
        public string scenePath = string.Empty;

        [Tooltip("The abstract actor/prop slots this cutscene stages.")]
        public List<CutsceneSlot> slots = new List<CutsceneSlot>();

        [Tooltip("The camera's keyed pose/FOV curve and hard-cut markers.")]
        public CutsceneCameraLane cameraLane = new CutsceneCameraLane();

        [Tooltip("Typed markers on the shared timeline, using the same event-key vocabulary clips use.")]
        public List<CutsceneEventMarker> events = new List<CutsceneEventMarker>();

        [Tooltip("Points where the clock pauses until the host releases it. Looping clips and the camera hold their state.")]
        public List<CutsceneHoldMarker> holdMarkers = new List<CutsceneHoldMarker>();

        // Stored as strings, never the editor-only GlobalObjectId type, so Authoring/ never
        // references the editor assembly — only editor code ever parses one back out.
        /// <summary>Editor-only slot to GameObject bindings, one entry per scene this cutscene has been opened against.</summary>
        public List<CutsceneSceneBinding> sceneBindings = new List<CutsceneSceneBinding>();

        /// <summary>This cutscene's stable 64-bit identity, folded into its bake dedup key like the other identity-bearing assets.</summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        // Public for the same reason RigAsset.EnsureStableIds is: a cutscene built from code
        // (CreateInstance, populate slots, save) hits no lifecycle hook in between, so call this
        // after populating slots and before reading any CutsceneSlot.SlotId.
        /// <summary>Assigns a fresh stable id to this asset and to every slot still carrying the reserved 0 value. Idempotent.</summary>
        public void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdMinting.NewAssetStableId();
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
        // persisted "needs persisting" flag would contradict itself.
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
    /// One named, recastable slot a cutscene stages: an abstract role ("Bertha", "Minion A")
    /// pinning a rig and clip sets for an <see cref="CutsceneSlotKind.Actor"/>, or nothing but a
    /// transform lane for a <see cref="CutsceneSlotKind.Prop"/>.
    /// </summary>
    [Serializable]
    public sealed class CutsceneSlot
    {
        [Tooltip("Freely renameable label shown in the timeline and inspector. Never the binding key.")]
        public string name = string.Empty;

        [SerializeField] internal uint slotId;

        [Tooltip("Whether this slot plays clips on a rig or is a bare transform target.")]
        public CutsceneSlotKind kind = CutsceneSlotKind.Actor;

        [Tooltip("The rig this slot's clip blocks and part tracks resolve against. Ignored for Prop slots.")]
        public RigAsset rig;

        [Tooltip("The clip sets a clip block's clip id may resolve against. Ignored for Prop slots.")]
        public List<ClipSetAsset> clipSets = new List<ClipSetAsset>();

        [Tooltip("Optional set consulted when a facing has no override key active. Ignored for Prop slots.")]
        public DirectionSetAsset directionSet;

        [Tooltip("What this slot's actor can play: rig, clip sets, layers, names and turn granularity. Ignored for Prop slots.")]
        public ActorProfileAsset profile;

        [Tooltip("Auto locomotion: which of the profile's entries play while this slot's actor is moving or standing. Ignored for Prop slots.")]
        public CutsceneLocomotion locomotion = new CutsceneLocomotion();

        [Tooltip("Times a profile layer is stopped, handing it back to auto locomotion. Ignored for Prop slots.")]
        public List<CutsceneLayerStopKey> layerStops = new List<CutsceneLayerStopKey>();

        /// <summary>The rig every clip block, part track and attach socket on this slot resolves against. Every reader of a slot's rig goes through this, never <see cref="profile"/> directly.</summary>
        public RigAsset ResolvedRig
        {
            get { return profile != null ? profile.rig : null; }
        }

        /// <summary>The clip sets this slot's clip blocks may resolve against. Empty, never null, when the slot has no profile.</summary>
        public IReadOnlyList<ClipSetAsset> ResolvedClipSets
        {
            get { return profile != null ? (IReadOnlyList<ClipSetAsset>)profile.clipSets : Array.Empty<ClipSetAsset>(); }
        }

        // A plain asset reference, which Authoring/ may hold; placing it is editor work. Nothing at
        // run time reads this — a baked cutscene binds entities the host supplies, never a prefab.
        [Tooltip("Prefab the cast panel's Place in Scene stages this slot from. Optional; binding an already-placed GameObject by hand still works.")]
        public GameObject actorPrefab;

        [Tooltip("Which clip plays when, and whether it loops. Overlapping blocks cross-fade; touching blocks hard-cut.")]
        public List<CutsceneClipBlock> clipBlocks = new List<CutsceneClipBlock>();

        [Tooltip("The slot's own transform through the scene: root motion for an Actor (clips play in place), or the entire authored motion for a Prop.")]
        public List<CutsceneTransformKey> transformKeys = new List<CutsceneTransformKey>();

        [Tooltip("Explicit facing overrides. Absent a key here, facing derives from root travel direction. Ignored for Prop slots.")]
        public List<CutsceneFacingKey> facingKeys = new List<CutsceneFacingKey>();

        [Tooltip("Per-part key lanes that layer Override over whatever clip is currently playing. Ignored for Prop slots.")]
        public List<CutsceneKeyedTrack> partTracks = new List<CutsceneKeyedTrack>();

        [Tooltip("When this slot rides another slot, and when it lets go. A crate can ride a cart exactly as an actor does.")]
        public List<CutsceneAttachMarker> attachMarkers = new List<CutsceneAttachMarker>();

        [Tooltip("Spots this slot must reach before a rendezvous hold releases. A self-driving cart is a Prop with marks of its own.")]
        public List<CutsceneMarkKey> markKeys = new List<CutsceneMarkKey>();

        /// <summary>This slot's stable 32-bit identity within the owning cutscene — what a host's binding buffer resolves against, never <see cref="name"/> or list position.</summary>
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
            slotId = StableIdMinting.NewTargetStableId();
            return true;
        }
    }

    /// <summary>One clip block on a slot's clip lane: names an animation by its profile key, when it plays, how long, and how it loops.</summary>
    [Serializable]
    public sealed class CutsceneClipBlock
    {
        /// <summary>Animation name registry id; the entry's layer is derived from the slot's profile at bake and at play time.</summary>
        public uint animationKey;

        /// <summary>Block start, in raw timeline seconds.</summary>
        public float start;

        /// <summary>Block length in seconds. Overlapping blocks on the same profile layer cross-fade over the overlap; blocks that merely touch are a hard cut.</summary>
        [Min(0f)] public float duration;

        /// <summary>How the block loops. <c>UseClipDefault</c> defers to the profile entry's own loop, which may itself defer to the clip.</summary>
        public LoopMode loop = LoopMode.UseClipDefault;

        // Floored well above 0: a stopped clip is CutsceneControl.paused, and 0 in the baked blob
        // means "an older bake, no opinion".
        /// <summary>Playback speed for this block's clip, multiplied into whatever speed the host has the cutscene running at.</summary>
        [Min(0.01f)] public float speed = 1f;

        /// <summary>Seconds into the clip the block starts, for playing the second half of a swing.</summary>
        [Min(0f)] public float clipStartOffsetSeconds;
    }

    /// <summary>
    /// One transform key on a cutscene timeline: a <see cref="ClipAsset"/> <see cref="TransformKey"/>
    /// reshaped for absolute seconds rather than a clip's normalized [0, 1] duration fraction,
    /// because a cutscene's timeline has no fixed duration to normalize against.
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

    /// <summary>One facing key: <see cref="CutsceneFacingMode.Fixed"/> pins <see cref="angleDegrees"/> from this key's time; <see cref="CutsceneFacingMode.Auto"/> hands facing back to auto-derivation.</summary>
    [Serializable]
    public struct CutsceneFacingKey
    {
        /// <summary>Key time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The facing angle, degrees, 0–360. Meaningless for an <see cref="CutsceneFacingMode.Auto"/> key.</summary>
        [Range(0f, 360f)] public float angleDegrees;

        /// <summary>Whether this key pins <see cref="angleDegrees"/> or releases the pin. Default (0) is <see cref="CutsceneFacingMode.Fixed"/>, matching every key authored before this field existed.</summary>
        public CutsceneFacingMode mode;
    }

    /// <summary>Auto locomotion for an Actor slot: while the bound actor is moving, the profile's moving entry plays and facing follows travel; standing, the standing entry plays.</summary>
    [Serializable]
    public sealed class CutsceneLocomotion
    {
        /// <summary>Master switch. Off leaves every layer to authored blocks and stop keys only.</summary>
        public bool enabled = true;

        /// <summary>Animation name registry id played when the slot's actor is not moving. 0 = none — the moving entry's layer is stopped instead of switched.</summary>
        public uint standingAnimationKey;

        /// <summary>Animation name registry id played while the slot's actor is moving. 0 = locomotion is inert; the bake warns.</summary>
        public uint movingAnimationKey;

        /// <summary>Displacement speed, meters/second, above which the slot's actor counts as moving.</summary>
        public float movingSpeedThresholdMetersPerSecond = 0.05f;
    }

    /// <summary>One stop key on a profile layer row: hands the layer back to auto locomotion (or silence, with no locomotion) from this time.</summary>
    [Serializable]
    public struct CutsceneLayerStopKey
    {
        /// <summary>Key time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The profile layer's <c>displayName</c>, resolved to an index against the slot's profile at bake and at draw time — never an index, so a layer reorder keeps the key on the layer the author meant.</summary>
        public string layerName;

        /// <summary>Fade-out duration, seconds. <c>NaN</c> = the stopped entry's own clip default.</summary>
        public float blendOutSeconds;
    }

    // Tag-addressed only, no target-id fallback: a slot recast to a different rig keeps its keys
    // wherever tags line up, and a raw target id has no meaning once the rig changes out from
    // under it — the whole point of a cutscene slot is that it can be recast.
    /// <summary>
    /// One per-part key lane on an actor slot: keys addressed by a rig target's tag, layering
    /// Override over the composited pose from whatever the clip lane is currently playing.
    /// </summary>
    [Serializable]
    public sealed class CutsceneKeyedTrack
    {
        /// <summary>The role this track poses, resolved against the slot's rig at bake/preview time. Unresolved is a warning and a skip, never an error.</summary>
        public uint tagId;

        /// <summary>Which pose channels this track owns; channels outside the mask are left to the composited clip beneath it.</summary>
        public AnimatedChannels channels = AnimatedChannels.PositionXY;

        /// <summary>Keys in ascending <see cref="CutsceneTransformKey.time"/> order.</summary>
        public List<CutsceneTransformKey> keys = new List<CutsceneTransformKey>();
    }

    /// <summary>
    /// The cutscene camera's keyed pose/FOV curve and hard-cut markers. Continuous by default —
    /// "one camera just moving around the scene" — with <see cref="cutMarkers"/> as the named
    /// exception, never the rule.
    /// </summary>
    [Serializable]
    public sealed class CutsceneCameraLane
    {
        /// <summary>Keys in ascending <see cref="CutsceneCameraKey.time"/> order.</summary>
        public List<CutsceneCameraKey> keys = new List<CutsceneCameraKey>();

        /// <summary>Times where the camera pose is a hard cut rather than an interpolated move between the surrounding keys.</summary>
        public List<CutsceneCameraCutMarker> cutMarkers = new List<CutsceneCameraCutMarker>();
    }

    /// <summary>One camera pose key: position, rotation and field of view.</summary>
    [Serializable]
    public struct CutsceneCameraKey
    {
        /// <summary>Key time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>World-space position.</summary>
        public float3 position;

        /// <summary>World-space rotation in degrees, Euler ZXY, matching <see cref="TransformKey.rotation"/>'s convention.</summary>
        public float3 rotation;

        // The struct's zero value is not a usable FOV; the editor's Add Key path writes a real one
        // (default 60) — the same zero-is-not-meaningful convention as TransformKey.scale.
        /// <summary>Vertical field of view in degrees.</summary>
        public float fieldOfView;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>One hard-cut marker on the camera lane: at this time the camera pose snaps rather than interpolating from the previous key.</summary>
    [Serializable]
    public struct CutsceneCameraCutMarker
    {
        /// <summary>Marker time, in raw timeline seconds.</summary>
        public float time;
    }

    // A class rather than a struct, unlike EventMarker: fireOnSkip defaults on, and only a field
    // initializer gets that for free when Unity's list-element UI constructs a fresh row — a
    // struct's zero value would default it off.
    /// <summary>One typed marker on the cutscene's event lane, using the same event-key vocabulary and payload shape as <see cref="EventMarker"/>.</summary>
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

        /// <summary>Whether a skip still fires this event. Default on: a skipped cutscene must leave the same world state as a watched one unless a marker opts out.</summary>
        public bool fireOnSkip = true;

        /// <summary>
        /// Makes this event a cue: the clock pauses the instant the event fires and resumes when the
        /// host releases a hold named after the event's registry name — one marker instead of an
        /// event plus a hold whose id an author has to keep matching by hand.
        /// </summary>
        public bool holdUntilReleased;
    }

    // Plain string id, not a shared vocabulary: a hold is released by whatever host code is waiting
    // on it and is compared for equality exactly once, against a control component the host wrote
    // itself — there is no shared resolution step for a hold id to drift out of sync with.
    /// <summary>One hold point: the clock pauses here until the host releases it by <see cref="holdId"/>. Cutscene length is elastic, never a fixed end time.</summary>
    [Serializable]
    public sealed class CutsceneHoldMarker
    {
        /// <summary>Hold time, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The id a <c>CutsceneHoldRelease</c> names to release this specific hold.</summary>
        public string holdId = string.Empty;

        /// <summary>Makes this a rendezvous hold: the clock resumes on its own once every slot with an outstanding mark has arrived. A host release still overrides it.</summary>
        public bool autoReleaseWhenMarksReached = true;
    }

    /// <summary>
    /// One spot a slot is ordered to move to. The toolkit issues the order and judges arrival; what
    /// walks the entity there is the host's pathfinding.
    /// </summary>
    [Serializable]
    public struct CutsceneMarkKey
    {
        /// <summary>When the move order is issued, in raw timeline seconds.</summary>
        public float time;

        /// <summary>The world position to reach.</summary>
        public float3 position;

        /// <summary>Arrival facing, same 0-360 model as <see cref="CutsceneFacingKey"/>.</summary>
        [Range(0f, 360f)] public float facingDegrees;

        /// <summary>XZ distance that counts as "there". Authored default 0.5.</summary>
        public float toleranceMeters;

        /// <summary>0 waits forever; otherwise the mark resolves by teleport after this many real seconds. A paused cutscene never ticks it down.</summary>
        public float timeoutSeconds;

        /// <summary>Editor-only rehearsal of how long the walk takes; also where the merged root key lands. Authored default 2.</summary>
        public float previewTravelSeconds;

        /// <summary>When set, the bake derives a rendezvous hold at this mark's own <see cref="time"/> — the clock stops the instant the order goes out and resumes once every mark issued at that instant is reached.</summary>
        public bool waitUntilReached;
    }

    /// <summary>One attach/detach moment on a slot's attach lane.</summary>
    // Attach while already attached is a hand-over, not an error: the previous binding drops
    // silently and the new one takes its place; two markers at the same instant apply in authored
    // order. An attached slot's root lane is ignored for as long as the attachment lasts — the host
    // owns the transform, though clip blocks and part tracks keep playing. Detach leaves the slot
    // at the world pose it was let go at, and the root lane resumes from its next key.
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

        /// <summary>Detach only: an impulse in host space, rotated to world at detach time and handed to the host — the toolkit applies no physics of its own.</summary>
        public float3 detachImpulse;
    }

    /// <summary>Editor-only slot to GameObject bindings for one scene.</summary>
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

        // Stored as a string, never the editor-only GlobalObjectId type, so this stays parseable by
        // editor code only while the asset itself carries no editor-assembly reference.
        /// <summary><c>GlobalObjectId.ToString()</c> of the bound GameObject.</summary>
        public string globalObjectId = string.Empty;
    }
}
