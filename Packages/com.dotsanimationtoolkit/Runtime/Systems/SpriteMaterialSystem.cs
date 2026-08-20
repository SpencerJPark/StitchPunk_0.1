// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Publishes each visible flipbook part's sampled frame to its per-instance shader properties
    /// (architecture section 5.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One write, not two hops.</strong> The sampled pose goes straight to
    /// <see cref="SpriteSliceProperty"/> and <see cref="AtlasFrameProperty"/>. The audited host
    /// staged frames through an `ImageIndex → ImageIndexOverride` pair guarded by a dirty flag that
    /// had rotted, so a frame could be computed and then never uploaded; section 5.7 replaces that
    /// arrangement outright rather than repairing it.
    /// </para>
    /// <para>
    /// <strong>Both properties are written for every flipbook part, whichever mode its clip uses.</strong>
    /// A part carries both components because the choice between slice and atlas addressing is a
    /// per-track <see cref="SpriteFrameMode"/> decision, so one plane may be driven either way
    /// across different clips. The unused one keeps the value composition left in it — the rest
    /// slice, or <see cref="ClipSampler.IdentityAtlasRect"/> — which is a valid frame rather than
    /// stale garbage.
    /// </para>
    /// <para>
    /// <strong>There is deliberately no <c>sliceIndex &gt;= 0</c> guard.</strong> It would be dead
    /// code, and the reason is worth stating because the "−1 means no change" convention is real —
    /// it just lives on the authored key, never on the pose. Three things close every route to a
    /// negative pose slice: <c>RigTargetBaker</c> clamps <c>restSliceIndex</c> through
    /// <c>math.max(0, …)</c>; <see cref="ClipSampler.RestToPose"/> seeds the pose from that
    /// non-negative rest value; and <see cref="ClipSampler.SampleSpriteTrack"/> overwrites it only
    /// when the key's own index is <c>&gt;= 0</c>. <see cref="ClipSampler.LerpPose"/> picks whole
    /// slice values and never interpolates them, so a blend cannot introduce one either. A guard
    /// here would silently absorb a future regression in any of those four places instead of
    /// letting a test catch it.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformSampleSystem))]
    [BurstCompile]
    public partial struct SpriteMaterialSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpriteSliceProperty>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WriteSpritePropertiesJob writeJob = new WriteSpritePropertiesJob();
            state.Dependency = writeJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Copies one flipbook part's sampled frame into its material-property components.
    /// </summary>
    /// <remarks>
    /// The query is the flipbook archetype by construction: only
    /// <see cref="TargetKind.FlipbookPlane"/> parts carry the two property components, and
    /// <c>RigTargetBaker</c> adds them as a pair. Quads and VAT meshes are excluded without needing
    /// a tag to say so.
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct WriteSpritePropertiesJob : IJobEntity
    {
        private void Execute(
            in TargetPose pose,
            ref SpriteSliceProperty spriteSlice,
            ref AtlasFrameProperty atlasFrame)
        {
            // int → float on the way to the GPU: the shader contract of section 6.2 uploads the
            // slice as a float, which is why the component's field is one.
            spriteSlice.Value = pose.sliceIndex;
            atlasFrame.Value = pose.atlasRect;
        }
    }
}
