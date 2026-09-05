// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// How playback time maps onto a clip's duration (architecture sections 3.2, 5.4, 8 M3).
    /// Baked blob data always stores a resolved mode (<see cref="Once"/>, <see cref="Loop"/>, or
    /// <see cref="PingPong"/>); <see cref="UseClipDefault"/> is a command-side sentinel resolved
    /// against <see cref="ClipBlob.defaultLoop"/> when the command is applied.
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
    /// How a target's animation reaches the screen (architecture section 2.2). One clip may span
    /// several techniques across its targets. Billboarding is not a technique — it is a per-target
    /// render modifier (architecture section 6).
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

    /// <summary>
    /// The presentation kind of a rig target (architecture section 3.1).
    /// </summary>
    public enum TargetKind : byte
    {
        /// <summary>A 2D cutout part quad driven by transform tracks.</summary>
        Quad = 0,

        /// <summary>A VAT sub-mesh driven by vertex-animation-texture playback.</summary>
        VatMesh = 1,

        /// <summary>A flipbook plane driven by sprite tracks.</summary>
        FlipbookPlane = 2
    }

    /// <summary>
    /// How a track combines with the pose composited so far (architecture sections 3.2, 5.6).
    /// </summary>
    /// <summary>
    /// Whether a sprite track's slice keys are absolute frame indices or offsets from the part's
    /// rest slice (architecture section 5.7, amendment A37).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Absolute"/> is the default and the original behaviour: a key names the frame
    /// outright, and the <c>-1</c> "no change" sentinel applies.
    /// </para>
    /// <para>
    /// <see cref="RelativeToRest"/> exists because a design-driven target's rest slice is chosen
    /// per character — the ear shape a citizen rolled — so a clip that names an absolute frame
    /// destroys that choice. A relative key says "one view along from whatever this character has",
    /// which is the only form that survives a randomised variant. In this mode <c>0</c> is the
    /// natural no-op and negative offsets are legal, so the <c>-1</c> sentinel does **not** apply.
    /// </para>
    /// </remarks>
    public enum SpriteSliceSpace : byte
    {
        /// <summary>Keys name the frame outright; <c>-1</c> means "leave the current frame alone".</summary>
        Absolute = 0,

        /// <summary>Keys are offsets added to the part's rest slice; <c>0</c> is a no-op.</summary>
        RelativeToRest = 1
    }

    public enum TrackBlendOp : byte
    {
        /// <summary>Replace exactly the channels in the track's <see cref="AnimatedChannels"/> mask.</summary>
        Override = 0,

        /// <summary>
        /// Add position/rotation and multiply scale onto the composited result of the layers below
        /// (architecture section 5.6 — additive over composited lower layers, never over rest).
        /// </summary>
        Additive = 1
    }

    /// <summary>
    /// Channel mask declaring which pose channels a transform track animates
    /// (architecture section 3.2). Sprite frames are not a transform channel — sprite tracks are a
    /// separate track kind.
    /// </summary>
    [Flags]
    public enum AnimatedChannels : byte
    {
        /// <summary>No channels.</summary>
        None = 0,

        /// <summary>Local x/y position offset.</summary>
        PositionXY = 1 << 0,

        /// <summary>
        /// Local z position. Depth for a 3D rig; the draw-layer order channel for a 2.5D one.
        /// </summary>
        /// <remarks>
        /// Named <c>LayerZ</c> until 3D rigs were supported. The bit value is unchanged, so existing
        /// masks still mean what they meant — an enum serializes as its number, not its name.
        /// </remarks>
        PositionZ = 1 << 1,

        /// <summary>
        /// Rotation on all three axes.
        /// </summary>
        /// <remarks>
        /// Named <c>RotationZ</c> when a rotation was a single angle. Same bit, wider meaning: a
        /// track that masks rotation in now carries x and y as well, and a clip authored before this
        /// keeps working because its unused axes are zero.
        /// </remarks>
        Rotation = 1 << 2,

        /// <summary>Non-uniform x/y/z scale (negative components flip).</summary>
        Scale = 1 << 3
    }

    /// <summary>
    /// How a sprite track's keys address their frames (architecture section 3.2).
    /// </summary>
    public enum SpriteFrameMode : byte
    {
        /// <summary>Keys select a Texture2DArray slice index (−1 = no change).</summary>
        Slice = 0,

        /// <summary>Keys select an atlas rect: scale.xy, offset.zw.</summary>
        AtlasRect = 1
    }

    /// <summary>
    /// Which vertex-animation-texture encoding a texture set carries (architecture section 3.3).
    /// </summary>
    public enum VatFlavor : byte
    {
        /// <summary>Per-bone object-space 3×4 skinning matrices; mesh carries indices/weights in UV1/UV2.</summary>
        BoneMatrix = 0,

        /// <summary>Absolute object-space per-vertex positions (optional normals texture).</summary>
        VertexPosition = 1
    }

    /// <summary>
    /// The request kinds games write through <see cref="AnimationCommand"/>
    /// (architecture sections 5.2, 5.4).
    /// </summary>
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
        SetTime = 4
    }

    /// <summary>
    /// Per-layer playback state flags (architecture sections 5.2, 5.4).
    /// </summary>
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

    /// <summary>
    /// Per-key easing between a key and the next one (architecture section 3.2). The left key's
    /// mode drives the segment; interpolation is resolved per key at bake.
    /// </summary>
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

        /// <summary>
        /// A cubic Bézier ease shaped by the key's two editable handles.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The curve warps the segment's blend weight, exactly as the four fixed modes above do —
        /// it is not a per-channel value curve. That keeps one key driving position, rotation and
        /// scale together, which is the shape the whole track model is built on; per-channel curves
        /// would mean per-channel keys.
        /// </para>
        /// <para>
        /// Handles are constrained to the unit square (validation rule V17). x outside [0, 1] makes
        /// the curve non-functional — two weights for one time — and y outside it makes the eased
        /// weight leave the segment, which is overshoot. Overshoot is expressive and deliberately
        /// not allowed yet: the bake's bounds union assumes the keys bound the sampled extremes
        /// (architecture section 4.6), so a curve that travels past its keys would produce a box
        /// too small and parts that cull while still on screen.
        /// </para>
        /// </remarks>
        Bezier = 5
    }

    /// <summary>
    /// Whether a flipbook key names an array index outright or an offset from its track's
    /// <c>baseIndex</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per <em>key</em>, unlike <see cref="SpriteSliceSpace"/>, which is per track. The two are
    /// different axes and both apply: this one decides how a key's stored number becomes a track
    /// value, and <see cref="SpriteSliceSpace"/> decides whether that value replaces the pose's
    /// slice or is added to the rest slice the character's variant chose.
    /// </para>
    /// <para>
    /// <strong>A relative key stores its offset, never the resolved index.</strong> That is the
    /// whole point: moving a track's <c>baseIndex</c> retargets every relative key at once, and no
    /// offset is recomputed or lost in the process. Storing the resolved value instead would make
    /// <c>baseIndex</c> a one-shot edit that silently bakes itself into the keys.
    /// </para>
    /// </remarks>
    public enum SpriteIndexMode : byte
    {
        /// <summary>The stored number is the array index itself; −1 still means "no change".</summary>
        Absolute = 0,

        /// <summary>The stored number is an offset; the index is the track's <c>baseIndex</c> plus it.</summary>
        RelativeToBase = 1
    }

    /// <summary>
    /// Whether a cutscene slot is a rigged, clip-playing actor or a bare transform target (Phase G
    /// spec §2). Lives here rather than in <c>Authoring</c> because both the authored
    /// <c>CutsceneSlot</c> and the baked <see cref="CutsceneSlotMetaBlob"/> need it, and an
    /// authoring type nests inside this namespace precisely so it can see enums declared here — not
    /// the other way around.
    /// </summary>
    public enum CutsceneSlotKind : byte
    {
        /// <summary>Plays clip blocks on a rig and moves via root keys.</summary>
        Actor = 0,

        /// <summary>A plain transform target with no rig and no clip lane — a door, a crate, a light.</summary>
        Prop = 1
    }

    /// <summary>
    /// What one cutscene attach marker does (amendment A63): bind this slot to a host, or release it.
    /// Lives here rather than in <c>Authoring</c> for the same reason <see cref="CutsceneSlotKind"/>
    /// does — both the authored marker and its baked form need it.
    /// </summary>
    public enum CutsceneAttachKind : byte
    {
        /// <summary>Bind this slot to a host slot's socket, or to the host's root.</summary>
        Attach = 0,

        /// <summary>Release the slot where it stands and raise <c>CutsceneDetachSignal</c>.</summary>
        Detach = 1
    }

    /// <summary>
    /// Reserved event-key values (architecture sections 5.4, 5.5). Keys 0–15 belong to the
    /// package: 0 is invalid, 1–2 are the shipped built-ins, 3–15 are reserved for future
    /// built-ins. User-authored keys start at <see cref="FirstUserKey"/> (validation rule V09).
    /// </summary>
    public enum ReservedEventKeys : uint
    {
        /// <summary>Invalid key; never emitted.</summary>
        Invalid = 0,

        /// <summary>Emitted once when a <see cref="LoopMode.Once"/> clip completes.</summary>
        ClipFinished = 1,

        /// <summary>Emitted when a Play/Queue command's <see cref="ClipId"/> fails to resolve.</summary>
        ClipResolveFailed = 2,

        /// <summary>Inclusive lower bound for user-authored event keys (validation rule V09).</summary>
        FirstUserKey = 16
    }
}
