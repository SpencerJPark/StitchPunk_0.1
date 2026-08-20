// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The authoring definition of an animatable thing (architecture section 3.1): its named,
    /// stable-id'd <see cref="targets"/>, its ordered <see cref="layers"/>, and the mirror table the
    /// Mirror Clip utility consumes. One rig serves many clips and many actors.
    /// </summary>
    /// <remarks>
    /// Targets carry stable ids because their meaning is independent of order; layers deliberately
    /// do not, because a layer's meaning <em>is</em> its compositing priority — index = priority,
    /// higher index composites later — so reordering layers is a semantic edit, not a rename.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewRig",
        menuName = "DOTS Animation Toolkit/Rig Asset",
        order = 0)]
    public sealed class RigAsset : ScriptableObject, IStableIdMintReporter
    {
        /// <summary>The maximum number of playback layers a rig may define (architecture sections 3.1, 5.2).</summary>
        public const int MaxLayerCount = 8;

        [SerializeField] internal ulong stableId;

        /// <summary>
        /// The animatable slots of this rig. Each row's <c>stableId</c> is unique within the rig and
        /// is what tracks bind to — never the display name and never the list position.
        /// </summary>
        public List<RigTargetDefinition> targets = new List<RigTargetDefinition>();

        /// <summary>
        /// The rig's playback layers, lowest priority first. At most
        /// <see cref="MaxLayerCount"/> entries; at least one (validation rule V13).
        /// </summary>
        public List<LayerDefinition> layers = new List<LayerDefinition>();

        /// <summary>
        /// The user-configured left/right target pairs the Mirror Clip utility swaps. Empty when the
        /// rig is not mirrorable. Consumed by that editor utility (build step C7); it is authoring
        /// data only and never reaches the baked blob, so no bake or runtime path reads it.
        /// </summary>
        public MirrorPair[] mirrorPairs = Array.Empty<MirrorPair>();

        /// <summary>
        /// Attachment points on this rig. Empty for rigs nothing attaches to — a rig without
        /// sockets bakes no socket blob and its actors carry no socket component.
        /// </summary>
        public List<SocketDefinition> sockets = new List<SocketDefinition>();

        /// <summary>
        /// The nodes of this rig that turn to face the viewer, and how (amendment A44). Empty for a
        /// rig that never billboards, which bakes no billboard components at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Billboarding lives on the rig rather than on an actor component so it travels
        /// with the rig and is shared by every actor instanced from it.</strong> It is the first
        /// such block — the rig carries no constraints and no collision shapes yet — and the
        /// ragdoll work will add its rows beside these.
        /// </para>
        /// <para>
        /// A node not named here, and with no ancestor named here, is not billboarded and transforms
        /// normally in its parent's space.
        /// </para>
        /// </remarks>
        public List<BillboardRootDefinition> billboardRoots = new List<BillboardRootDefinition>();

        /// <summary>
        /// This rig's stable 64-bit identity (architecture section 3.4). Assigned once when the
        /// asset is created and never changed except through the editor's explicit remap tooling.
        /// </summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        /// <summary>
        /// Assigns a fresh stable id to this rig and to every target and socket row that still
        /// carries the reserved 0 value. Idempotent: an already-identified rig or row is left
        /// untouched, which is what makes duplicate-then-edit copy the id rather than mint a new one
        /// (the duplicate is separated later by the editor's id-collision postprocessor,
        /// architecture section 3.4).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Public because building a rig from code cannot do without it.</strong> The
        /// lifecycle hooks below cover every rig a human authors: a rig edited in the inspector gets
        /// <c>OnValidate</c>, and one loaded from disk gets <c>OnEnable</c>. Neither covers the
        /// script that does <c>CreateInstance</c>, assigns <see cref="targets"/>, and saves — the
        /// hooks all fired while the list was still empty, and <c>AssetDatabase.CreateAsset</c>
        /// fires none of them. Such a rig saves with every target id still 0, which fails validation
        /// rules V02 and V05 the moment a clip references it.
        /// </para>
        /// <para>
        /// The rig's own id is not the problem — it is minted in <c>Awake</c>, before any list
        /// exists. It is specifically the id-bearing <em>rows</em>, which is why this is the one
        /// authoring asset that needs a public entry point: no other one carries identities inside a
        /// list that a caller populates after construction.
        /// </para>
        /// <para>
        /// Call it after populating <see cref="targets"/>, <see cref="sockets"/> and
        /// <see cref="billboardRoots"/>, and before reading any <c>Id</c>. Both shipped samples do,
        /// and both produced invalid rigs before they did.
        /// </para>
        /// </remarks>
        public void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdUtility.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
            // Each list is guarded independently rather than returning early on the first null one.
            // An early return would make whichever list came last depend on the ones before it
            // being non-null — a rig with no sockets would silently leave its billboard roots
            // unidentified, which is the failure this whole method exists to prevent.
            if (targets != null)
            {
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = targets[targetIndex];
                    if (targetDefinition == null)
                    {
                        continue;
                    }
                    if (targetDefinition.stableId == 0u)
                    {
                        targetDefinition.stableId = StableIdUtility.NewTargetStableId();
                        hasUnpersistedStableId = true;
                    }
                }
            }

            if (sockets != null)
            {
                for (int socketIndex = 0; socketIndex < sockets.Count; socketIndex++)
                {
                    SocketDefinition socketDefinition = sockets[socketIndex];
                    if (socketDefinition == null)
                    {
                        continue;
                    }
                    // Sockets draw from the same 32-bit id space as targets. They are looked up in
                    // separate arrays, so a collision across the two kinds is harmless, and one
                    // generator is one fewer thing to keep in step.
                    if (socketDefinition.stableId == 0u)
                    {
                        socketDefinition.stableId = StableIdUtility.NewTargetStableId();
                        hasUnpersistedStableId = true;
                    }
                }
            }

            if (billboardRoots != null)
            {
                for (int rootIndex = 0; rootIndex < billboardRoots.Count; rootIndex++)
                {
                    BillboardRootDefinition rootDefinition = billboardRoots[rootIndex];
                    if (rootDefinition == null)
                    {
                        continue;
                    }
                    // Same 32-bit space again, for the same reason. A billboard root's id is what
                    // clip billboard tracks bind to, so it must exist before any clip can key one.
                    if (rootDefinition.stableId == 0u)
                    {
                        rootDefinition.stableId = StableIdUtility.NewTargetStableId();
                        hasUnpersistedStableId = true;
                    }
                }
            }
        }


        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself (amendment A14).
        [System.NonSerialized] private bool hasUnpersistedStableId;

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

        // Unity raises Awake when an instance is created and OnEnable both after creation and
        // after an asset is deserialized, so between them no asset can reach an inspector, a bake,
        // or a test without an id. Both funnel into the same idempotent assignment.
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
    /// One animatable slot on a rig (architecture section 3.1): a 2D cutout part quad, a flipbook
    /// plane, or a VAT sub-mesh. Identified by its <see cref="Id"/>, never by
    /// <see cref="displayName"/> and never by list position.
    /// </summary>
    [Serializable]
    public sealed class RigTargetDefinition
    {
        /// <summary>Freely renameable label shown in the inspectors and the clip editor.</summary>
        public string displayName = string.Empty;

        [SerializeField] internal uint stableId;

        /// <summary>How this target is presented, which decides the components its part entity gets.</summary>
        public TargetKind kind = TargetKind.Quad;

        /// <summary>
        /// Conservative local half-extents used by the bake-time bounds math (architecture
        /// section 4.6). Negative components are clamped to 0 at bake.
        /// </summary>
        public float3 boundsExtents = new float3(0.5f, 0.5f, 0.5f);

        /// <summary>
        /// How many consecutive frames one <em>variant</em> of this target owns in its texture
        /// array (architecture section 5.7, amendment A37). 1 means the target has no variant
        /// blocks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A design-driven target is laid out in blocks — for ears,
        /// <c>[pointy_front, pointy_back, round_front, round_back, …]</c> gives 2. A host's design
        /// system rolls the variant and writes its slice into <c>TargetRestPose.restSliceIndex</c>;
        /// a view offset then has to land on another frame of <em>that</em> block.
        /// </para>
        /// <para>
        /// Above 1, offsets wrap inside the block, so an over-large offset can never display a
        /// different variant's art. The failure that prevents — a character wearing someone else's
        /// ears — is invisible to every automated test and immediately obvious to a player, which is
        /// why the wrap is in the package rather than left to callers.
        /// </para>
        /// </remarks>
        [Min(1)] public int framesPerVariant = 1;

        /// <summary>
        /// Whether this target's presentation changes with the direction the actor faces
        /// (amendment A37). Targets that opt in are baked a <c>PartFacing</c> component.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Explicit rather than derived from <see cref="framesPerVariant"/>, which was the
        /// first attempt and was wrong.</strong> Facing changes a part in two independent ways: an
        /// <em>alt view</em> is a different slice and needs a variant block, while a <em>mirror</em>
        /// is the same art reflected and needs no block at all. Deriving the opt-in from
        /// <c>framesPerVariant &gt; 1</c> therefore excluded every mirror-only target — a nose that
        /// simply flips — which is precisely the case the owner tested first and found inert.
        /// </para>
        /// <para>
        /// Default false, so a rig that never opted in bakes exactly the archetype it did before
        /// A37 and pays nothing.
        /// </para>
        /// </remarks>
        public bool facesDirection;

        /// <summary>
        /// This target's stable 32-bit identity (architecture section 3.4). Unique within the
        /// owning rig (validation rule V05).
        /// </summary>
        public TargetId Id
        {
            get { return new TargetId(stableId); }
        }
    }

    /// <summary>
    /// One playback layer slot on a rig (architecture section 3.1). The layer's identity is its list
    /// position: index = priority, and a higher index composites later and therefore wins.
    /// </summary>
    [Serializable]
    public sealed class LayerDefinition
    {
        /// <summary>Cosmetic label only — layer identity is the list position, never this name.</summary>
        public string displayName = string.Empty;

        /// <summary>
        /// Whether the baked actor starts with this layer active. Authored here and consumed by the
        /// entity baker (build step C3), which seeds the actor's <c>PlaybackLayer</c> buffer — no
        /// module reads it yet, so an unused-field search will not find a consumer until then.
        /// </summary>
        public bool defaultActive;
    }

    /// <summary>
    /// One attachment point on a rig: a named place other entities can ride.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A socket either follows a <see cref="SocketAttachMode.RigTarget"/> — a part whose transform
    /// the sampler already computes every frame, so nothing needs baking — or a
    /// <see cref="SocketAttachMode.Bone"/> of the VAT source rig, whose motion exists only inside a
    /// texture at runtime and so is sampled into the socket blob at bake time.
    /// </para>
    /// <para>
    /// <strong>Bones are named, targets are not.</strong> A bone socket stores
    /// <see cref="boneName"/> because the bone lives in an imported hierarchy this package does not
    /// own and cannot assign ids to — the name is the only handle Unity gives us. Renaming a bone
    /// in the DCC tool therefore breaks the binding, which is why the bake reports an unresolved
    /// bone rather than silently baking a socket that never moves.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class SocketDefinition
    {
        /// <summary>Cosmetic label; socket identity is <see cref="Id"/>, never this name.</summary>
        public string displayName = string.Empty;

        [SerializeField] internal uint stableId;

        /// <summary>Whether this socket follows a rig target or an imported bone.</summary>
        public SocketAttachMode mode = SocketAttachMode.RigTarget;

        /// <summary>Stable id of the followed target, for <see cref="SocketAttachMode.RigTarget"/>.</summary>
        public uint targetId;

        /// <summary>Name of the followed bone, for <see cref="SocketAttachMode.Bone"/>.</summary>
        public string boneName = string.Empty;

        /// <summary>
        /// Which playback layer drives a bone socket's time, mirroring the VAT part contract. A
        /// hand and a cape may follow different layers, so this is per socket. Ignored by
        /// rig-target sockets, which follow their part whatever drove it.
        /// </summary>
        [Min(0)] public int layerIndex;

        /// <summary>Offset from the followed target or bone, in its local space.</summary>
        public Vector3 localPosition = Vector3.zero;

        /// <summary>Rotation offset from the followed target or bone, in degrees.</summary>
        public Vector3 localEulerAngles = Vector3.zero;

#if UNITY_EDITOR
        /// <summary>
        /// A prefab the Clip Editor hangs off this socket so its placement can be judged.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Authoring aid only — nothing reads this at run time.</strong> A socket's job is
        /// to expose a pose; what a game attaches to it is the game's decision, made through
        /// <c>SocketAttachmentAuthoring</c> on a real entity. This field exists because tuning an
        /// offset against a bare marker cube is guesswork, and "does the sword sit in the hand"
        /// is the only question that actually matters when placing one.
        /// </para>
        /// <para>
        /// Inside <c>UNITY_EDITOR</c> so the reference does not drag the prefab into a player
        /// build. A preview asset pulling a weapon mesh into every build that ships the rig would
        /// be a real cost for a purely editor-side convenience.
        /// </para>
        /// </remarks>
        public GameObject previewAttachment;
#endif

        /// <summary>This socket's stable 32-bit identity. Unique within the owning rig.</summary>
        public SocketId Id
        {
            get { return new SocketId(stableId); }
        }
    }

    /// <summary>
    /// How a <see cref="BillboardRootDefinition"/> names the node it turns (amendment A44).
    /// </summary>
    /// <remarks>
    /// Two kinds because the rig has two kinds of node and only one of them has an id.
    /// <see cref="RigAsset.targets"/> is a flat list — the rig asset carries no hierarchy at all,
    /// and the hierarchy a billboard inherits down is the authoring prefab's transforms. A rig
    /// target can therefore be addressed by a stable id that survives renames; a bare grouping
    /// transform that is nobody's animatable part has no id to offer.
    /// </remarks>
    public enum BillboardAddressKind : byte
    {
        /// <summary>Addresses a <see cref="RigTargetDefinition"/> by its stable id.</summary>
        RigTarget = 0,

        /// <summary>
        /// Addresses a transform of the authoring prefab by its path below the prefab root.
        /// </summary>
        /// <remarks>
        /// Carries the same rename fragility a bone name does (amendment A42), and for the same
        /// reason: the node is not a row this package owns, so there is no id to assign it. The
        /// bake reports an address it cannot resolve rather than silently dropping the root.
        /// </remarks>
        HierarchyPath = 1
    }

    /// <summary>
    /// Which node a billboard root turns (amendment A44).
    /// </summary>
    [Serializable]
    public struct BillboardNodeAddress
    {
        /// <summary>Whether this address names a rig target or a prefab transform.</summary>
        public BillboardAddressKind kind;

        /// <summary>Stable id of the addressed target, for <see cref="BillboardAddressKind.RigTarget"/>.</summary>
        public uint targetId;

        /// <summary>
        /// Path below the prefab root, for <see cref="BillboardAddressKind.HierarchyPath"/>. Empty
        /// addresses the prefab root itself.
        /// </summary>
        public string hierarchyPath;
    }

    /// <summary>
    /// One billboard root on a rig (amendment A44): a node that turns to face the viewer, and the
    /// pivot every node beneath it inherits unless one of them declares a root of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Inheritance is nearly free; an override is what costs something.</strong> Nodes below
    /// a root are transform children, so the root's rotation already reaches them through the usual
    /// parent composition and an inheriting node needs no rotation of its own. A <em>nested</em>
    /// root is the real work: its ancestor's rotation is already in its parent chain, so it must
    /// cancel that before applying its own or the two compose and it turns twice.
    /// </para>
    /// <para>
    /// That is what lets a character billboard as a whole while the item in its hand billboards
    /// independently — the case this feature exists for.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class BillboardRootDefinition
    {
        /// <summary>Cosmetic label; root identity is <see cref="Id"/>, never this name.</summary>
        public string displayName = string.Empty;

        [SerializeField] internal uint stableId;

        /// <summary>Which node this root turns.</summary>
        public BillboardNodeAddress address;

        /// <summary>
        /// Which billboard rule to apply. Defaults to <see cref="BillboardMode.ScreenAligned"/>,
        /// which is what the host game's original system does — every root taking the same rotation
        /// from the camera's forward vector.
        /// </summary>
        public BillboardMode mode = BillboardMode.ScreenAligned;

        /// <summary>
        /// The axis the node turns about, for <see cref="BillboardMode.AxisConstrained"/>. Ignored
        /// by every other mode. Normalised at bake; a zero axis is reported rather than assumed.
        /// </summary>
        public float3 constraintAxis = new float3(0f, 1f, 0f);

        /// <summary>
        /// A fixed yaw added to the billboard result, about the resolved frame's own up axis. Clip
        /// billboard tracks key on top of this rather than replacing it, so a rig can sit
        /// permanently three-quarters-on and still be animated off that rest.
        /// </summary>
        public float angleOffsetDegrees;

        /// <summary>Whether the resolved angle is quantised to <see cref="snapSteps"/> increments.</summary>
        public bool snapEnabled;

        /// <summary>
        /// How many discrete facings a full turn is divided into — 8 and 16 being the usual sprite
        /// counts. Minimum 2, because a one-step wheel is not a snap but a fixed direction, which
        /// <see cref="BillboardMode.Off"/> already expresses.
        /// </summary>
        [Min(2)] public int snapSteps = 8;

        /// <summary>
        /// Phase of the snap wheel in degrees, so the steps can straddle the cardinal directions
        /// rather than land on them.
        /// </summary>
        public float snapOffsetDegrees;

        /// <summary>Whether the node is limited to an arc around its rest orientation.</summary>
        public bool clampEnabled;

        /// <summary>
        /// The full width of the arc the node may turn within, centred on its rest orientation, in
        /// degrees. The rest orientation is the node's <em>animated</em> pose, so a clip that turns
        /// the node carries the arc with it.
        /// </summary>
        [Range(0f, 360f)] public float clampArcDegrees = 180f;

        /// <summary>This root's stable 32-bit identity. Unique within the owning rig.</summary>
        public BillboardRootId Id
        {
            get { return new BillboardRootId(stableId); }
        }
    }

    /// <summary>
    /// One left/right target pairing consumed by the editor's Mirror Clip utility (architecture
    /// sections 3.1, 10 answer 7). Authored per rig; the package never infers mirrors from names.
    /// </summary>
    [Serializable]
    public struct MirrorPair
    {
        /// <summary>Stable id of the left-hand target.</summary>
        public uint leftTargetId;

        /// <summary>Stable id of the right-hand target.</summary>
        public uint rightTargetId;

    }
}
