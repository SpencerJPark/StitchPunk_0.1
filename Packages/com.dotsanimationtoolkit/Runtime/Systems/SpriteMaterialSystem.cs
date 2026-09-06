// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>TransformSampleSystem</c>: publishes each visible flipbook part's sampled
    /// frame to its per-instance shader properties. One direct write, not staged through a dirty
    /// flag that could rot and leave a computed frame never uploaded.
    /// </summary>
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

    // The query is the flipbook archetype by construction: only FlipbookPlane parts carry both
    // property components (RigTargetBaker adds them as a pair), so quads and VAT meshes are
    // excluded without needing a tag to say so.
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct WriteSpritePropertiesJob : IJobEntity
    {
        private void Execute(
            in TargetPose pose,
            ref SpriteSliceProperty spriteSlice,
            ref AtlasFrameProperty atlasFrame)
        {
            // Both properties are written for every part regardless of which mode its clip
            // actually uses (slice vs atlas is a per-track decision) — the unused one just keeps
            // whatever valid frame composition left in it, never stale garbage. No sliceIndex >= 0
            // guard here on purpose: RigTargetBaker, RestToPose, SampleSpriteTrack and LerpPose
            // between them already close every route to a negative pose slice, and a guard here
            // would silently absorb a regression in one of those instead of letting a test catch it.
            spriteSlice.Value = pose.sliceIndex;
            atlasFrame.Value = pose.atlasRect;
        }
    }
}
