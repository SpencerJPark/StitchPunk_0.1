// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>Whether a frame's displacement counts as "moving" for auto locomotion — shared by the runtime player and the editor preview's rehearsal lane, so the two agree.</summary>
    [BurstCompile]
    public static class CutsceneLocomotionMath
    {
        [BurstCompile]
        public static bool IsMoving(in float3 displacement, float deltaTime, float threshold)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }
            return math.length(displacement) / deltaTime > threshold;
        }
    }
}
