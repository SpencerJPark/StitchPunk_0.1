// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The authoring definition of an animatable thing: its named, stable-id'd <see cref="targets"/>,
    /// its ordered <see cref="layers"/>, and the mirror table the Mirror Clip utility consumes. One
    /// rig serves many clips and many actors.
    /// </summary>
    // Targets carry stable ids because their meaning is independent of order; layers deliberately do
    // not, since a layer's meaning IS its compositing priority (index = priority, higher composites
    // later), so reordering layers is a semantic edit, not a rename.
    [CreateAssetMenu(
        fileName = "NewRig",
        menuName = "DOTS Animation Toolkit/Rig Asset",
        order = 0)]
    public sealed class RigAsset : ScriptableObject, IStableIdMintReporter
    {
        /// <summary>The maximum number of playback layers a rig may define.</summary>
        public const int MaxLayerCount = 8;

        [SerializeField] internal ulong stableId;

        [Tooltip("Animatable slots of this rig. Bound to by stable id, never by name or list position.")]
        public List<RigTargetDefinition> targets = new List<RigTargetDefinition>();

        [Tooltip("Playback layers, lowest priority first. At most MaxLayerCount entries, at least one.")]
        public List<LayerDefinition> layers = new List<LayerDefinition>();

        [Tooltip("Left/right target pairs the Mirror Clip utility swaps. Editor data only; never reaches the baked blob.")]
        public MirrorPair[] mirrorPairs = Array.Empty<MirrorPair>();

#if UNITY_EDITOR
        // Lives here, not on the Clip Editor window, since a source prefab is a property of the rig
        // rather than of whichever window has it open — every window loading this rig gets it for free.
        // UNITY_EDITOR-guarded like SocketDefinition.previewAttachment: an unguarded hard reference
        // here would drag the rigged prefab into every player build, for a value only editor-time
        // operations (VAT bake, Clip Editor preview) ever read.
        [Tooltip("Rigged prefab this rig's Clip Editor preview and VAT bake sample.")]
        public GameObject sourcePrefab;
#endif

        [Tooltip("Attachment points on this rig. Empty bakes no socket blob and no socket component.")]
        public List<SocketDefinition> sockets = new List<SocketDefinition>();

        [Tooltip("Nodes of this rig that turn to face the viewer, and how. Empty bakes no billboard components.")]
        public List<BillboardRootDefinition> billboardRoots = new List<BillboardRootDefinition>();

        // A body's parent is implied — its nearest ragdolled ancestor in the addressed hierarchy,
        // resolved by walking the prefab at bake time — never authored directly.
        [Tooltip("Box-collider bodies of this rig's ragdoll. Empty bakes no ragdoll components.")]
        public List<RagdollBodyDefinition> ragdollBodies = new List<RagdollBodyDefinition>();

        [Tooltip("Rig-wide ragdoll tuning every body obeys together: plane of freedom, gravity scale, solver settings.")]
        public RagdollRigSettings ragdollSettings = RagdollRigSettings.Default;

        /// <summary>This rig's stable 64-bit identity. Assigned once at creation and never changed except through the editor's explicit remap tooling.</summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        // Public because building a rig from code cannot do without it: OnValidate/OnEnable cover a
        // rig a human authors, but a script that does CreateInstance, assigns targets, and saves via
        // AssetDatabase.CreateAsset fires neither, so it would otherwise save with every target id
        // still 0. Call after populating targets, sockets and billboardRoots, and before reading any Id.
        /// <summary>
        /// Assigns a fresh stable id to this rig and to every target and socket row that still
        /// carries the reserved 0 value. Idempotent, so duplicate-then-edit copies the id rather
        /// than minting a new one.
        /// </summary>
        public void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdMinting.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
            // Each list is guarded independently, not an early return on the first null one — a rig
            // with no sockets must not leave its billboard roots unidentified as a side effect.
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
                        targetDefinition.stableId = StableIdMinting.NewTargetStableId();
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
                        socketDefinition.stableId = StableIdMinting.NewTargetStableId();
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
                        rootDefinition.stableId = StableIdMinting.NewTargetStableId();
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
                        bodyDefinition.stableId = StableIdMinting.NewTargetStableId();
                        hasUnpersistedStableId = true;
                    }
                }
            }
        }


        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself.
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
    /// One animatable slot on a rig: a 2D cutout part quad, a flipbook plane, or a VAT sub-mesh.
    /// Identified by its <see cref="Id"/>, never by <see cref="displayName"/> and never by list
    /// position.
    /// </summary>
    [Serializable]
    public sealed class RigTargetDefinition
    {
        /// <summary>Freely renameable label shown in the inspectors and the clip editor.</summary>
        public string displayName = string.Empty;

        [SerializeField] internal uint stableId;

        // Editor data only — no bake or runtime path reads it. Lets an ordinary prefab node be
        // shown as a part before a RigTargetAuthoring is placed on it in the scene; a path rather
        // than a name because two planes both called "Plane" is the ordinary case, not the exotic one.
        /// <summary>The path, from the previewed prefab's root, of the node this target stands for. Empty for a target not tied to one.</summary>
        public string sourceNodePath = string.Empty;

        /// <summary>How this target is presented, which decides the components its part entity gets.</summary>
        public TargetKind kind = TargetKind.Quad;

        /// <summary>Conservative local half-extents used by the bake-time bounds math. Negative components are clamped to 0 at bake.</summary>
        public float3 boundsExtents = new float3(0.5f, 0.5f, 0.5f);

        // Above 1, offsets wrap inside the block rather than escaping into a neighboring variant's
        // art — invisible to any automated test, immediately obvious to a player.
        /// <summary>How many consecutive frames one variant of this target owns in its texture array. 1 means no variant blocks.</summary>
        [Min(1)] public int framesPerVariant = 1;

        // Explicit rather than derived from framesPerVariant > 1: an alt view is a different slice
        // and needs a variant block, while a mirror is the same art reflected and needs no block at
        // all, so deriving the opt-in from block count excludes every mirror-only target.
        /// <summary>Whether this target's presentation changes with the direction the actor faces. Opting in bakes a <c>PartFacing</c> component.</summary>
        public bool facesDirection;

        /// <summary>This target's stable 32-bit identity. Unique within the owning rig.</summary>
        public TargetId Id
        {
            get { return new TargetId(stableId); }
        }

        // HideInInspector on purpose: the default array drawer would otherwise show this as a bare
        // uint spinner. RigAssetEditor's Target Tags section is the only surface that writes it,
        // through a picker offering nothing but the registry's existing tags.
        /// <summary>The role this target plays, or 0 for untagged — legal and ordinary, not a to-do. Unique within the owning rig when non-zero.</summary>
        [HideInInspector] public uint tagId;
    }

    /// <summary>One playback layer slot on a rig. The layer's identity is its list position: index = priority, higher composites later and wins.</summary>
    [Serializable]
    public sealed class LayerDefinition
    {
        /// <summary>Cosmetic label only — layer identity is the list position, never this name.</summary>
        public string displayName = string.Empty;

        /// <summary>Whether the baked actor starts with this layer active.</summary>
        public bool defaultActive;
    }

    // A socket either follows a RigTarget — a part whose transform the sampler already computes
    // every frame, so nothing needs baking — or a Bone of the VAT source rig, whose motion exists
    // only inside a texture at runtime and is sampled into the socket blob at bake time.
    /// <summary>One attachment point on a rig: a named place other entities can ride.</summary>
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

        // Named, not id'd: the bone lives in an imported hierarchy this package does not own and
        // cannot assign ids to. Renaming a bone in the DCC tool breaks the binding.
        /// <summary>Name of the followed bone, for <see cref="SocketAttachMode.Bone"/>.</summary>
        public string boneName = string.Empty;

        /// <summary>Which playback layer drives a bone socket's time. Ignored by rig-target sockets, which follow their part whatever drove it.</summary>
        [Min(0)] public int layerIndex;

        /// <summary>Offset from the followed target or bone, in its local space.</summary>
        public Vector3 localPosition = Vector3.zero;

        /// <summary>Rotation offset from the followed target or bone, in degrees.</summary>
        public Vector3 localEulerAngles = Vector3.zero;

#if UNITY_EDITOR
        // Authoring aid only — nothing reads this at run time; what a game attaches is decided
        // through SocketAttachmentAuthoring on a real entity. UNITY_EDITOR-guarded so the reference
        // does not drag a weapon mesh into every player build that ships the rig.
        [Tooltip("A prefab the Clip Editor hangs off this socket so its placement can be judged.")]
        public GameObject previewAttachment;
#endif

        /// <summary>This socket's stable 32-bit identity. Unique within the owning rig.</summary>
        public SocketId Id
        {
            get { return new SocketId(stableId); }
        }
    }

    // Three kinds because the rig has three kinds of node and only one of them has an id. A rig
    // target is a row this package owns, so it is addressed by a stable id. A bare grouping
    // transform has no such row, so it falls back to its path below the prefab root. An imported
    // skinned-mesh bone is neither, and its path is unstable across reparents inside the armature,
    // so it is addressed by name instead — the only handle the VAT bake has on a bone.
    /// <summary>How a <see cref="BillboardRootDefinition"/> or a <see cref="RagdollBodyDefinition"/> names the node it applies to.</summary>
    public enum RigNodeAddressKind : byte
    {
        /// <summary>Addresses a <see cref="RigTargetDefinition"/> by its stable id.</summary>
        RigTarget = 0,

        // Carries the same rename fragility a bone name does, for the same reason: the node is not
        // a row this package owns. The bake reports an address it cannot resolve rather than
        // silently dropping the root.
        /// <summary>Addresses a transform of the authoring prefab by its path below the prefab root.</summary>
        HierarchyPath = 1,

        // Billboarding rejects this kind at validation: a bone has no billboard frame of its own to turn.
        /// <summary>Addresses a bone of the imported skinned mesh by name.</summary>
        Bone = 2
    }

    /// <summary>Which node a billboard root or a ragdoll body applies to.</summary>
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

    // Inheritance is nearly free: nodes below a root are transform children, so the root's rotation
    // already reaches them through parent composition. A nested root is the real work — its
    // ancestor's rotation is already in its parent chain and must be cancelled before applying its
    // own, which is what lets a held item billboard independently of the character holding it.
    /// <summary>One billboard root on a rig: a node that turns to face the viewer, and the pivot every node beneath it inherits unless one of them declares a root of its own.</summary>
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

        /// <summary>Full width of the arc the node may turn within, centred on its rest orientation in degrees. The rest orientation is the node's animated pose, so a clip carries the arc with it.</summary>
        [Range(0f, 360f)] public float clampArcDegrees = 180f;

        /// <summary>This root's stable 32-bit identity. Unique within the owning rig.</summary>
        public BillboardRootId Id
        {
            get { return new BillboardRootId(stableId); }
        }
    }

    /// <summary>
    /// Rig-wide ragdoll tuning: the plane of freedom every body simulates in, gravity, and the
    /// fixed-step solver's own knobs.
    /// </summary>
    [Serializable]
    public struct RagdollRigSettings
    {
        // Rig-wide by design, not an oversight: a ragdoll is one articulated body, and half of it
        // constrained to a plane while the rest is free is not a mode, it's a bug.
        /// <summary>Which plane of freedom this rig's ragdoll simulates in. Defaults to <see cref="RagdollSpace.Planar2D"/> for billboarded 2.5D characters.</summary>
        public RagdollSpace space;

        /// <summary>Multiplies the world gravity for this rig. A paper cutout and a stone golem fall differently.</summary>
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

        // Per-body softness deliberately not offered: the joint pin itself is always solved
        // rigidly, and only the limit is allowed to feel soft.
        /// <summary>Limit-constraint softness, rig-wide, [0, 1]. 1 corrects a violated limit fully within one solver iteration; lower softens it.</summary>
        public float jointStiffness;

        /// <summary>Limit-constraint damping, rig-wide, [0, 1] — how much of a limited axis's angular speed a correcting iteration removes, so a joint settles rather than rings.</summary>
        public float jointDamping;

        /// <summary>Position-solve iterations per fixed substep. Default 6.</summary>
        public byte solverIterations;

        /// <summary>The fixed solver rate, in steps per second. Default 120.</summary>
        public float substepHz;

        // Assigned as RigAsset.ragdollSettings' field initializer, so a brand-new rig starts here
        // rather than at every numeric field's zero value — a 0 Hz substep rate or 0 gravity scale
        // would silently disable the whole feature the moment a rig ever authors a body.
        /// <summary>A rig that has never touched these settings: Planar2D, no gravity scaling, light default damping, a fully rigid but damped joint limit, and the usual solver defaults.</summary>
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

    // No isRoot flag and no parent reference: a body whose ancestor chain contains no other
    // ragdolled body IS the root, and the chain itself is the parent — both facts the hierarchy
    // already states once, derived by walking the prefab at bake time. No enabled flag either: the
    // on/off toggle a game flips on death and revive is RagdollActor, a runtime component on the
    // actor root, since whether a ragdoll is active is a fact about a live instance, not the rig.
    /// <summary>
    /// One box-collider body of a rig's ragdoll: the node it is welded to, its collider, its
    /// physical properties, and the joint limits measured against its implied parent — the nearest
    /// other ragdoll body above it in the addressed hierarchy.
    /// </summary>
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

        // Stored as a full size, not a half-extent, because that is what an author drags a handle
        // to in the viewport; the solver's own half-extent form is a bake-time conversion.
        /// <summary>The box collider's full extents, local to the addressed node. All three components must be greater than 0 (rule V-R4).</summary>
        public float3 boxSize = new float3(1f, 1f, 1f);

        /// <summary>The box collider's local rotation, in degrees, applied ZXY — matching <c>TransformKey</c>'s convention.</summary>
        public float3 boxEulerAngles;

        /// <summary>Mass in the rig's own units. Must be greater than 0 (rule V-R7) — a zero or negative mass has no closed-form inertia tensor.</summary>
        public float mass = 1f;

        /// <summary>Linear velocity damping per second. −1 means "inherit <see cref="RagdollRigSettings.defaultLinearDamping"/>", resolved once at bake.</summary>
        public float linearDamping = -1f;

        /// <summary>Angular velocity damping per second. Same −1 "inherit" sentinel as <see cref="linearDamping"/>.</summary>
        public float angularDamping = -1f;

        /// <summary>Contact restitution (bounce) for this body, [0, 1].</summary>
        public float restitution;

        /// <summary>Contact friction coefficient for this body.</summary>
        public float friction = 0.5f;

        // This pair, and swingLimitDegrees/twistLimitDegrees below, are always stored regardless of
        // RagdollRigSettings.space — switching space and back must not destroy tuning authored for
        // the other space.
        /// <summary><see cref="RagdollSpace.Planar2D"/> signed hinge range minimum, in degrees. Must not exceed <see cref="limitMaxDegrees"/>; both must stay within [−180, 180] (rule V-R5).</summary>
        public float limitMinDegrees = -45f;

        /// <summary><see cref="RagdollSpace.Planar2D"/> signed hinge range maximum, in degrees.</summary>
        public float limitMaxDegrees = 45f;

        /// <summary><see cref="RagdollSpace.Spatial3D"/> cone half-angle, in degrees, [0, 180].</summary>
        public float swingLimitDegrees = 45f;

        /// <summary><see cref="RagdollSpace.Spatial3D"/> half-range about the joint's own axis, in degrees, [0, 180].</summary>
        public float twistLimitDegrees = 45f;

        /// <summary>Which of 8 self-collision groups this body belongs to (a bit index, 0-7, not a mask).</summary>
        public byte selfGroup;

        // Both bodies of a pair must admit each other's group to collide — a disagreement means no
        // collision. A body's own parent-child pairs are excluded automatically at bake regardless
        // of this mask: two boxes sharing a joint overlap by construction.
        /// <summary>Bitmask of the 8 self-collision groups this body collides with. Default: all.</summary>
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

    /// <summary>One left/right target pairing consumed by the editor's Mirror Clip utility. Authored per rig; the package never infers mirrors from names.</summary>
    [Serializable]
    public struct MirrorPair
    {
        /// <summary>Stable id of the left-hand target.</summary>
        public uint leftTargetId;

        /// <summary>Stable id of the right-hand target.</summary>
        public uint rightTargetId;

    }
}
