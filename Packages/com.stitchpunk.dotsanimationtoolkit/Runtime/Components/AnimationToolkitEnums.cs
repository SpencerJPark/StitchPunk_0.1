// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;

namespace StitchPunk.AnimationToolkit
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

        /// <summary>Local z position — the 2.5D draw-layer order channel.</summary>
        LayerZ = 1 << 1,

        /// <summary>Rotation about the z axis.</summary>
        RotationZ = 1 << 2,

        /// <summary>Non-uniform x/y scale (negative values flip).</summary>
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
        EaseInOut = 4
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
