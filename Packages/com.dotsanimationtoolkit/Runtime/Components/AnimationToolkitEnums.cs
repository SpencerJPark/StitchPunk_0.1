// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// How playback time maps onto a clip's duration. Baked blob data always stores a resolved
    /// mode (<see cref="Once"/>, <see cref="Loop"/>, <see cref="PingPong"/>);
    /// <see cref="UseClipDefault"/> is a command-side sentinel resolved against the clip's default when applied.
    /// </summary>
    public enum LoopMode : byte
    {
        /// <summary>Command sentinel: use the clip's authored default loop mode.</summary>
        UseClipDefault = 0,

        /// <summary>Play once: time clamps at the end, the layer finishes and deactivates.</summary>
        Once = 1,

        /// <summary>Wrap around at the end of the clip and keep playing.</summary>
        Loop = 2,

        /// <summary>
        /// Reflect at each end: sampling follows
        /// <c>duration − |duration − mod(time, 2 × duration)|</c>; the layer never finishes.
        /// </summary>
        PingPong = 3
    }

    /// <summary>
    /// How a target's animation reaches the screen. One clip may span several techniques across
    /// its targets. Billboarding is not a technique — it is a per-target render modifier.
    /// </summary>
    public enum AnimTechnique : byte
    {
        /// <summary>Keyed TRS curves applied to the part entity's transform.</summary>
        TransformTracks = 0,

        /// <summary>Sprite frames selected by Texture2DArray slice index.</summary>
        FlipbookSlice = 1,

        /// <summary>Sprite frames selected by atlas rect (scale.xy, offset.zw).</summary>
        FlipbookAtlas = 2,

        /// <summary>Vertex animation texture storing per-bone skinning matrices.</summary>
        BoneVat = 3,

        /// <summary>Vertex animation texture storing absolute per-vertex positions.</summary>
        VertexVat = 4
    }

    /// <summary>The presentation kind of a rig target.</summary>
    public enum TargetKind : byte
    {
        /// <summary>A 2D cutout part quad driven by transform tracks.</summary>
        Quad = 0,

        /// <summary>A VAT sub-mesh driven by vertex-animation-texture playback.</summary>
        VatMesh = 1,

        /// <summary>A flipbook plane driven by sprite tracks.</summary>
        FlipbookPlane = 2
    }

    /// <summary>How a track combines with the pose composited so far.</summary>
    public enum TrackBlendOp : byte
    {
        /// <summary>Replace exactly the channels in the track's <see cref="AnimatedChannels"/> mask.</summary>
        Override = 0,

        /// <summary>Add position/rotation and multiply scale onto the composited result of the layers below.</summary>
        Additive = 1
    }

    /// <summary>Whether a sprite track's slice keys are absolute frame indices or offsets from the part's rest slice.</summary>
    public enum SpriteSliceSpace : byte
    {
        /// <summary>Keys name the frame outright; <c>-1</c> means "leave the current frame alone".</summary>
        Absolute = 0,

        /// <summary>Keys are offsets added to the part's rest slice; <c>0</c> is a no-op, and the <c>-1</c> sentinel does not apply.</summary>
        RelativeToRest = 1
    }

    /// <summary>Channel mask declaring which pose channels a transform track animates. Sprite frames are a separate track kind, not a channel.</summary>
    [Flags]
    public enum AnimatedChannels : byte
    {
        /// <summary>No channels.</summary>
        None = 0,

        /// <summary>Local x/y position offset.</summary>
        PositionXY = 1 << 0,

        // Depth for a 3D rig, draw-layer order for a 2.5D one. Bit unchanged since this was named
        // LayerZ — an enum serializes as its number, not its name.
        PositionZ = 1 << 1,

        // All three axes. Bit unchanged since this was named RotationZ and meant one angle; a clip
        // authored before this keeps working because its unused axes are zero.
        Rotation = 1 << 2,

        /// <summary>Non-uniform x/y/z scale (negative components flip).</summary>
        Scale = 1 << 3
    }

    /// <summary>How a sprite track's keys address their frames.</summary>
    public enum SpriteFrameMode : byte
    {
        /// <summary>Keys select a Texture2DArray slice index (−1 = no change).</summary>
        Slice = 0,

        /// <summary>Keys select an atlas rect: scale.xy, offset.zw.</summary>
        AtlasRect = 1
    }

    /// <summary>Which vertex-animation-texture encoding a texture set carries.</summary>
    public enum VatFlavor : byte
    {
        /// <summary>Per-bone object-space 3×4 skinning matrices; mesh carries indices/weights in UV1/UV2.</summary>
        BoneMatrix = 0,

        /// <summary>Absolute object-space per-vertex positions (optional normals texture).</summary>
        VertexPosition = 1
    }

    /// <summary>The request kinds games write through <see cref="AnimationCommand"/>.</summary>
    public enum CommandKind : byte
    {
        /// <summary>Start a clip on a layer, optionally crossfading from the current clip.</summary>
        Play = 0,

        /// <summary>Store a clip in the layer's one-deep queue; promoted when the current clip finishes.</summary>
        Queue = 1,

        /// <summary>Stop the layer, immediately or fading out over a blend duration.</summary>
        Stop = 2,

        /// <summary>Change the layer's playback speed.</summary>
        SetSpeed = 3,

        /// <summary>Jump the layer's playback time.</summary>
        SetTime = 4,

        /// <summary>Play a named entry from the actor's <c>ActorProfile</c>, resolved by key and facing.</summary>
        PlayAnimation = 5,

        /// <summary>Stop a named entry from the actor's <c>ActorProfile</c>, only if it is still the layer's active key.</summary>
        StopAnimation = 6
    }

    /// <summary>Per-layer playback state flags.</summary>
    [Flags]
    public enum PlaybackFlags : byte
    {
        /// <summary>No flags: the layer is stopped.</summary>
        None = 0,

        /// <summary>The layer is playing (or fading out a previous clip).</summary>
        Active = 1 << 0,

        /// <summary>A previous clip is crossfading into the current one.</summary>
        Blending = 1 << 1,

        /// <summary>A queued clip is waiting to be promoted when the current clip finishes.</summary>
        HasQueued = 1 << 2,

        /// <summary>A <see cref="LoopMode.Once"/> clip has reached its end.</summary>
        Finished = 1 << 3,

        /// <summary>The clip finished during the most recent playback advance.</summary>
        FinishedThisFrame = 1 << 4
    }

    /// <summary>Per-key easing between a key and the next one. The left key's mode drives the segment.</summary>
    public enum Interpolation : byte
    {
        /// <summary>Straight linear blend to the next key.</summary>
        Linear = 0,

        /// <summary>No interpolation — hold this key's values until the next key.</summary>
        Step = 1,

        /// <summary>Quadratic ease-in: <c>t²</c>.</summary>
        EaseIn = 2,

        /// <summary>Quadratic ease-out: <c>1 − (1 − t)²</c>.</summary>
        EaseOut = 3,

        /// <summary>Piecewise quadratic ease-in-out: <c>t &lt; ½ ? 2t² : 1 − 2(1 − t)²</c>.</summary>
        EaseInOut = 4,

        /// <summary>A cubic Bézier ease shaped by the key's two editable handles.</summary>
        // Handles are clamped to the unit square: x outside [0, 1] breaks monotonicity (two
        // weights for one time), and y outside it causes overshoot, which the bake's bounds union
        // does not account for.
        Bezier = 5
    }

    /// <summary>
    /// Whether a flipbook key names an array index outright or an offset from its track's
    /// <c>baseIndex</c>. Per-key, independent from <see cref="SpriteSliceSpace"/>, which is per-track.
    /// </summary>
    public enum SpriteIndexMode : byte
    {
        /// <summary>The stored number is the array index itself; −1 still means "no change".</summary>
        Absolute = 0,

        // Stores the offset, not the resolved index: moving the track's baseIndex retargets every
        // relative key at once.
        RelativeToBase = 1
    }

    /// <summary>Whether a cutscene slot is a rigged, clip-playing actor or a bare transform target.</summary>
    public enum CutsceneSlotKind : byte
    {
        /// <summary>Plays clip blocks on a rig and moves via root keys.</summary>
        Actor = 0,

        /// <summary>A plain transform target with no rig and no clip lane — a door, a crate, a light.</summary>
        Prop = 1
    }

    /// <summary>What one cutscene attach marker does: bind this slot to a host, or release it.</summary>
    public enum CutsceneAttachKind : byte
    {
        /// <summary>Bind this slot to a host slot's socket, or to the host's root.</summary>
        Attach = 0,

        /// <summary>Release the slot where it stands and raise <c>CutsceneDetachSignal</c>.</summary>
        Detach = 1
    }

    /// <summary>
    /// Reserved event-key values. Keys 0-15 belong to the package: 0 is invalid, 1-2 are the
    /// shipped built-ins, 3-15 are reserved for future built-ins. User-authored keys start at <see cref="FirstUserKey"/>.
    /// </summary>
    public enum ReservedEventKeys : uint
    {
        /// <summary>Invalid key; never emitted.</summary>
        Invalid = 0,

        /// <summary>Emitted once when a <see cref="LoopMode.Once"/> clip completes.</summary>
        ClipFinished = 1,

        /// <summary>Emitted when a Play/Queue command's <see cref="ClipId"/> fails to resolve.</summary>
        ClipResolveFailed = 2,

        /// <summary>Inclusive lower bound for user-authored event keys.</summary>
        FirstUserKey = 16
    }

    /// <summary>Whether an animation starts or stops an actor's ragdoll when it plays.</summary>
    public enum RagdollTrigger : byte
    {
        /// <summary>Playing this animation does nothing to ragdoll state.</summary>
        None = 0,

        /// <summary>Enables the actor's ragdoll.</summary>
        Start = 1,

        /// <summary>Disables the actor's ragdoll.</summary>
        Stop = 2
    }
}
