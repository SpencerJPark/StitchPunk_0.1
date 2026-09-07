// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;

namespace DotsAnimationToolkit
{
    /// <summary>Animation key + facing to layer/clip resolution over a baked <see cref="ActorProfileBlob"/>: binary search by key, then the direction fold for a directional entry.</summary>
    [BurstCompile]
    public static class ActorProfileApi
    {
        /// <param name="layerIndex">The entry's layer on success; 0 on failure.</param>
        /// <param name="clip">The resolved clip on success; <c>default</c> on failure.</param>
        /// <param name="animationIndex">The entry's dense index on success; -1 on failure.</param>
        [BurstCompile]
        public static bool TryResolve(
            ref ActorProfileBlob blob,
            uint animationKey,
            Direction facing,
            out byte layerIndex,
            out ClipId clip,
            out int animationIndex)
        {
            layerIndex = 0;
            clip = default;

            if (!TryFindAnimation(ref blob, animationKey, out animationIndex))
            {
                return false;
            }

            ref ActorAnimationBlob animation = ref blob.animations[animationIndex];
            layerIndex = animation.layerIndex;

            if (animation.hasDirections)
            {
                FacingResolver.ResolveClipFacing(
                    facing, blob.turnDirections, out Direction clipFacing, out bool _);
                clip = animation.slots.ResolveSlot(clipFacing);
            }
            else
            {
                clip = animation.clip;
            }
            return true;
        }

        /// <param name="animationIndex">The dense index of the entry on success; -1 on failure.</param>
        [BurstCompile]
        public static bool TryFindAnimation(ref ActorProfileBlob blob, uint animationKey, out int animationIndex)
        {
            animationIndex = -1;

            int lowBound = 0;
            int highBound = blob.animations.Length - 1;
            while (lowBound <= highBound)
            {
                int middleIndex = lowBound + ((highBound - lowBound) >> 1);
                uint middleKey = blob.animations[middleIndex].animationKey;
                if (middleKey == animationKey)
                {
                    animationIndex = middleIndex;
                    return true;
                }
                if (middleKey < animationKey)
                {
                    lowBound = middleIndex + 1;
                }
                else
                {
                    highBound = middleIndex - 1;
                }
            }
            return false;
        }
    }
}
