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
        /// The box-collider bodies of this rig's ragdoll, addressed by hierarchy rather than by an
        /// authored parent reference (Phase D, amendment A50). Empty for a rig that never opts in,
        /// which bakes no ragdoll components at all — the same free-by-default contract
        /// <see cref="billboardRoots"/> already established.
        /// </summary>
        /// <remarks>
        /// A body's parent is implied: its nearest ragdolled ancestor in the addressed hierarchy,
        /// resolved by walking the prefab at bake time (Phase D3). Nothing here states that parent
        /// directly — see <see cref="RagdollBodyDefinition"/>'s remarks for why a second, authored
        /// statement of the hierarchy was deliberately left out.
        /// </remarks>
        public List<RagdollBodyDefinition> ragdollBodies = new List<RagdollBodyDefinition>();

        /// <summary>
        /// Rig-wide ragdoll tuning — the plane of freedom, gravity scale, and solver settings every
        /// body in <see cref="ragdollBodies"/> obeys together (Phase D, amendment A50). See
        /// <see cref="RagdollRigSettings"/>'s remarks for why these are never authored per body.
        /// </summary>
        public RagdollRigSettings ragdollSettings = RagdollRigSettings.Default;

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

            if (ragdollBodies != null)
            {
                for (int bodyIndex = 0; bodyIndex < ragdollBodies.Count; bodyIndex++)
                {
                    RagdollBodyDefinition bodyDefinition = ragdollBodies[bodyIndex];
                    if (bodyDefinition == null)
                    {
                        continue;
                    }
                    // Same 32-bit space again, for the same reason. A ragdoll body's id is what the
                    // runtime buffer element and the Clip Editor's selection state bind to, so it
                    // must exist before either can address it.
                    if (bodyDefinition.stableId == 0u)
                    {
                        bodyDefinition.stableId = StableIdUtility.NewTargetStableId();
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

        /// <summary>
        /// The path, from the previewed prefab's root, of the node this target stands for. Empty
        /// for a target that is not tied to one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>This is what lets an ordinary node of a prefab be a part.</strong> A rig target
        /// is an abstract slot with a stable id, and until something says which node fills it the
        /// only answer lives in the scene, on a <c>RigTargetAuthoring</c> the clip editor cannot
        /// see. A plane sitting in the hierarchy therefore had no id for a sprite track to bind to,
        /// which is why the editor could not put a flipbook on it. Writing the path here gives the
        /// editor the answer before the bake does.
        /// </para>
        /// <para>
        /// Authoring data for the editor only — no bake or runtime path reads it, exactly like
        /// <see cref="RigAsset.mirrorPairs"/>. The scene binding is still
        /// <c>RigTargetAuthoring.targetStableId</c>; this says which node that component is
        /// expected to sit on, so the editor can show the part where the user is looking at it.
        /// </para>
        /// <para>
        /// A path rather than a name, matching <c>RigNodeAddress.hierarchyPath</c>: two
        /// planes called "Plane" is the ordinary case, not the exotic one.
        /// </para>
        /// </remarks>
        public string sourceNodePath = string.Empty;

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
    /// How a <see cref="BillboardRootDefinition"/> or a <see cref="RagdollBodyDefinition"/> names
    /// the node it applies to (amendment A44; generalised for the ragdoll work, Phase D).
    /// </summary>
    /// <remarks>
    /// Three kinds because the rig has three kinds of node and only one of them has an id. A rig
    /// target (<see cref="RigAsset.targets"/>) is a row this package owns, so it can be addressed by
    /// a stable id that survives renames. A bare grouping transform that is nobody's animatable part
    /// has no such row and no id to offer, so it falls back to its path below the prefab root — the
    /// rig asset carries no hierarchy of its own; the hierarchy a billboard or a ragdoll body
    /// inherits down is the authoring prefab's transforms. An imported skinned-mesh bone is neither:
    /// it is not a row this package owns, and its path below the prefab root changes whenever an
    /// artist reparents inside the armature, which a rig target's path does not. It is addressed by
    /// name instead, because a name is the only handle the VAT bake has on a bone — the
    /// <see cref="SocketDefinition.boneName"/> precedent already works this way, for the same
    /// reason.
    /// </remarks>
    public enum RigNodeAddressKind : byte
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
        HierarchyPath = 1,

        /// <summary>
        /// Addresses a bone of the imported skinned mesh by name (Phase D, the ragdoll work).
        /// </summary>
        /// <remarks>
        /// A skinned bone's path below the prefab root is not stable the way a rig target's is: an
        /// artist reparenting inside the armature changes it with no edit to the rig asset at all,
        /// where a rig target's path only ever changes when the target itself is repointed. Naming
        /// carries its own fragility — a rename in the DCC tool breaks the binding — but it is the
        /// only handle the VAT bake has on a bone, exactly as <see cref="SocketDefinition.boneName"/>
        /// already established. Billboarding rejects this kind at validation (rule V-R8): a bone has
        /// no billboard frame of its own to turn.
        /// </remarks>
        Bone = 2
    }

    /// <summary>
    /// Which node a billboard root or a ragdoll body applies to (amendment A44; generalised for the
    /// ragdoll work, Phase D).
    /// </summary>
    [Serializable]
    public struct RigNodeAddress
    {
        /// <summary>Whether this address names a rig target, a prefab transform, or a bone.</summary>
        public RigNodeAddressKind kind;

        /// <summary>Stable id of the addressed target, for <see cref="RigNodeAddressKind.RigTarget"/>.</summary>
        public uint targetId;

        /// <summary>
        /// Path below the prefab root, for <see cref="RigNodeAddressKind.HierarchyPath"/>. Empty
        /// addresses the prefab root itself.
        /// </summary>
        public string hierarchyPath;

        /// <summary>Name of the addressed bone, for <see cref="RigNodeAddressKind.Bone"/>.</summary>
        public string boneName;
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
        public RigNodeAddress address;

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
    /// Rig-wide ragdoll tuning (Phase D, amendment A50, spec §3.2): the plane of freedom every body
    /// simulates in, gravity, and the fixed-step solver's own knobs. Never per body — see
    /// <see cref="space"/>'s remarks for why.
    /// </summary>
    [Serializable]
    public struct RagdollRigSettings
    {
        /// <summary>
        /// Which plane of freedom this rig's ragdoll simulates in. Defaults to
        /// <see cref="RagdollSpace.Planar2D"/> — most rigs this feature targets are billboarded
        /// 2.5D characters, and a flat character should fall <em>via its billboard</em>, not in
        /// world space.
        /// </summary>
        /// <remarks>
        /// <strong>Rig-wide by design, not an oversight.</strong> A ragdoll is one articulated body;
        /// half of it constrained to a plane and half of it free is not a mode, it is a bug. The
        /// Clip Editor shows this field in every Ragdoll component body (Phase D5), badged
        /// rig-scope exactly as billboard settings already are, so it is editable from wherever the
        /// author is looking without pretending to be a per-node choice.
        /// </remarks>
        public RagdollSpace space;

        /// <summary>
        /// Multiplies <see cref="RagdollConfig.worldGravity"/> (Phase D3) for this rig. A paper
        /// cutout and a stone golem fall differently, and this is the one knob that lets a rig say
        /// so without every body repeating it.
        /// </summary>
        public float gravityScale;

        /// <summary>
        /// Linear velocity damping a body adopts when its own <c>linearDamping</c> is left at the
        /// −1 "inherit" sentinel (see <see cref="RagdollBodyDefinition.linearDamping"/>).
        /// </summary>
        public float defaultLinearDamping;

        /// <summary>
        /// Angular velocity damping a body adopts when its own <c>angularDamping</c> is left at the
        /// −1 "inherit" sentinel.
        /// </summary>
        public float defaultAngularDamping;

        /// <summary>
        /// Limit-constraint softness, rig-wide, [0, 1]. 1 corrects a violated joint limit fully
        /// within one solver iteration; lower softens it. Per-body softness is a knob spec §3.3
        /// deliberately did not add — the joint pin itself is always solved rigidly, and only the
        /// limit is allowed to feel soft.
        /// </summary>
        public float jointStiffness;

        /// <summary>
        /// Limit-constraint damping, rig-wide, [0, 1] — the fraction of a body's angular speed along
        /// a limited axis removed on an iteration that is actively correcting it, so a joint settles
        /// at its stop instead of ringing against it.
        /// </summary>
        public float jointDamping;

        /// <summary>Position-solve iterations per fixed substep. Default 6 (spec §3.2).</summary>
        public byte solverIterations;

        /// <summary>The fixed solver rate, in steps per second. Default 120 (spec §3.2).</summary>
        public float substepHz;

        /// <summary>
        /// A rig that has never touched these settings: <see cref="RagdollSpace.Planar2D"/>, no
        /// gravity scaling, a light default damping so a dropped ragdoll settles rather than
        /// spinning forever, a fully rigid joint limit with enough damping to stop it ringing, and
        /// the spec's own solver defaults (6 iterations at 120 Hz). Assigned as
        /// <see cref="RigAsset.ragdollSettings"/>'s field initializer, so a brand-new rig starts
        /// here rather than at every numeric field's zero value — a 0 Hz substep rate or a 0-second
        /// gravity scale would silently disable the whole feature the moment a rig ever authors a
        /// body.
        /// </summary>
        public static RagdollRigSettings Default
        {
            get
            {
                return new RagdollRigSettings
                {
                    space = RagdollSpace.Planar2D,
                    gravityScale = 1f,
                    defaultLinearDamping = 0.05f,
                    defaultAngularDamping = 0.05f,
                    jointStiffness = 1f,
                    jointDamping = 0.5f,
                    solverIterations = 6,
                    substepHz = 120f
                };
            }
        }
    }

    /// <summary>
    /// One box-collider body of a rig's ragdoll (Phase D, amendment A50, spec §3.3): the node it is
    /// welded to, its collider, its physical properties, and the joint limits measured against its
    /// implied parent — the nearest other ragdoll body above it in the addressed hierarchy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>No <c>isRoot</c> flag and no parent reference (spec §3.4).</strong> A body whose
    /// ancestor chain contains no other ragdolled body <em>is</em> the root, and the chain itself is
    /// the parent — both facts the hierarchy already states once. Storing either again would be a
    /// second statement that could disagree with it, exactly the failure mode this package's stable
    /// ids exist to avoid for identity. Phase D3's baker derives both by walking the prefab that
    /// <see cref="address"/> resolves against, the same walk that already produces
    /// <c>restRelativeRotation</c>.
    /// </para>
    /// <para>
    /// <strong>No enabled flag.</strong> The on/off toggle a game flips on death and revive is
    /// <c>RagdollActor</c>, a runtime component on the actor root (spec §5.1) — never authoring
    /// data, because whether a ragdoll is currently active is a fact about a live instance, not
    /// about the rig every instance shares.
    /// </para>
    /// <para>
    /// <strong>Mass is authored; the inertia tensor is derived, at bake, from <see cref="mass"/> and
    /// <see cref="boxSize"/>.</strong> A box has a closed-form inertia tensor, and asking an author
    /// for one directly is asking for a wrong one — the same reasoning that keeps the box the only
    /// collider shape this feature offers (spec §12 row 2).
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class RagdollBodyDefinition
    {
        /// <summary>Cosmetic label; body identity is <see cref="Id"/>, never this name.</summary>
        public string displayName = string.Empty;

        [SerializeField] internal uint stableId;

        /// <summary>Which node this body is welded to.</summary>
        public RigNodeAddress address;

        /// <summary>
        /// The box collider's center, local to the addressed node's origin, so it travels with the
        /// animated pose.
        /// </summary>
        public float3 boxCenter;

        /// <summary>
        /// The box collider's full extents, local to the addressed node. All three components must
        /// be greater than 0 (rule V-R4). Stored as a full size rather than a half-extent because
        /// that is what an author drags a handle to in the viewport (Phase D6); the solver's own
        /// half-extent form is a bake-time conversion (spec §5.2), done once rather than repeated
        /// every substep.
        /// </summary>
        public float3 boxSize = new float3(1f, 1f, 1f);

        /// <summary>
        /// The box collider's local rotation, in degrees, applied ZXY — matching
        /// <c>TransformKey</c>, so a typed angle means the same thing everywhere in this toolkit.
        /// Radians only exist from bake onward (spec §3.3), the same split
        /// <see cref="BillboardRootDefinition.angleOffsetDegrees"/> already makes.
        /// </summary>
        public float3 boxEulerAngles;

        /// <summary>
        /// Mass in the rig's own units. Must be greater than 0 (rule V-R7) — a zero or negative mass
        /// has no closed-form inertia tensor for the bake to derive.
        /// </summary>
        public float mass = 1f;

        /// <summary>
        /// Linear velocity damping per second. <strong>−1 means "inherit
        /// <see cref="RagdollRigSettings.defaultLinearDamping"/>"</strong> — a negative sentinel
        /// rather than a companion bool, following this toolkit's existing <c>snapSteps &lt; 2</c>
        /// and <c>sliceIndex == -1</c> conventions. Resolved once at bake (Phase D3), never at
        /// runtime.
        /// </summary>
        public float linearDamping = -1f;

        /// <summary>
        /// Angular velocity damping per second. Same −1 "inherit
        /// <see cref="RagdollRigSettings.defaultAngularDamping"/>" sentinel as
        /// <see cref="linearDamping"/>.
        /// </summary>
        public float angularDamping = -1f;

        /// <summary>Contact restitution (bounce) for this body, [0, 1].</summary>
        public float restitution;

        /// <summary>Contact friction coefficient for this body.</summary>
        public float friction = 0.5f;

        /// <summary>
        /// <see cref="RagdollSpace.Planar2D"/> signed hinge range minimum, in degrees, measured
        /// against this body's rest relative orientation to its implied parent. Must not exceed
        /// <see cref="limitMaxDegrees"/>, and both must stay within [−180, 180] (rule V-R5).
        /// </summary>
        /// <remarks>
        /// <strong>This pair, and <see cref="swingLimitDegrees"/>/<see cref="twistLimitDegrees"/>
        /// below, are both always stored regardless of <see cref="RagdollRigSettings.space"/>.</strong>
        /// Switching the rig's space to look and switching back must not destroy tuning authored
        /// for the other space, so neither pair is ever conditionally serialized away — a rig that
        /// has only ever used Planar2D still keeps whatever swing and twist limits happen to hold.
        /// </remarks>
        public float limitMinDegrees = -45f;

        /// <summary><see cref="RagdollSpace.Planar2D"/> signed hinge range maximum, in degrees.</summary>
        public float limitMaxDegrees = 45f;

        /// <summary>
        /// <see cref="RagdollSpace.Spatial3D"/> cone half-angle, in degrees, [0, 180].
        /// </summary>
        public float swingLimitDegrees = 45f;

        /// <summary>
        /// <see cref="RagdollSpace.Spatial3D"/> half-range about the joint's own axis, in degrees,
        /// [0, 180]. Which local axis "twist" is measured about is an open question the ragdoll spec
        /// carries forward to Phase D8 (spec §13 Q1); it does not affect authoring this value.
        /// </summary>
        public float twistLimitDegrees = 45f;

        /// <summary>Which of 8 self-collision groups this body belongs to (a bit index, 0–7, not a mask).</summary>
        public byte selfGroup;

        /// <summary>
        /// Bitmask of the 8 self-collision groups this body collides with. Default: all. Both
        /// bodies of a pair must admit each other's group for the pair to collide — a disagreement
        /// between the two masks means no collision, the conservative reading spec §3.3 calls for.
        /// A body's own parent–child pairs are excluded automatically regardless of this mask
        /// (resolved at bake, Phase D3): two boxes sharing a joint overlap by construction, and
        /// letting them collide is a ragdoll that explodes on its first frame.
        /// </summary>
        public byte selfCollidesWith = 0xFF;

        /// <summary>
        /// Whether this body resolves against world geometry. Default true. A designer turns this
        /// off for a body that should pass through geometry — a cape tip, most often — while it
        /// still takes part in self-collision and its own joint.
        /// </summary>
        public bool collidesWithWorld = true;

        /// <summary>This body's stable 32-bit identity. Unique within the owning rig.</summary>
        public RagdollBodyId Id
        {
            get { return new RagdollBodyId(stableId); }
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
