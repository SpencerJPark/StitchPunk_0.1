// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>Turns one <see cref="ActorProfileAsset"/> into the <see cref="ActorProfileBlob"/> the runtime reads, flattening layers and sorting entries by <c>animationKey</c>.</summary>
    public static class ActorProfileBuilder
    {
        /// <summary>Layout version stamped into <see cref="ActorProfileBlob.schemaVersion"/>. Bump on any blob layout or hashed-stream change.</summary>
        public const int SchemaVersion = 1;

        private readonly struct FlattenedAnimation
        {
            public readonly uint animationKey;
            public readonly byte layerIndex;
            public readonly ActorAnimationDefinition definition;

            public FlattenedAnimation(uint animationKey, byte layerIndex, ActorAnimationDefinition definition)
            {
                this.animationKey = animationKey;
                this.layerIndex = layerIndex;
                this.definition = definition;
            }
        }

        /// <summary>Builds the profile blob.</summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        /// <exception cref="ClipValidationException">
        /// Thrown when the profile carries any P1-P4 error. P2 (animation name registry
        /// membership) is skipped here — Authoring cannot reach the editor-only vocabulary
        /// provider — and is judged instead by the Actor Editor badge and at entity bake.
        /// </exception>
        public static BlobAssetReference<ActorProfileBlob> Build(ActorProfileAsset profile, Allocator allocator)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            List<ValidationMessage> validationMessages = ActorProfileValidation.Validate(profile, null);
            if (ClipValidation.HasErrors(validationMessages))
            {
                throw new ClipValidationException(validationMessages);
            }

            List<FlattenedAnimation> flattenedAnimations = FlattenAndSortAnimations(profile);
            return BuildBlob(profile, flattenedAnimations, allocator);
        }

        /// <summary>Hashes what <see cref="Build"/> would produce, without allocating a blob the caller must own. Returns 0 when the profile is null or fails validation.</summary>
        public static ulong ComputeContentHash(ActorProfileAsset profile)
        {
            if (profile == null)
            {
                return 0UL;
            }

            List<ValidationMessage> validationMessages = ActorProfileValidation.Validate(profile, null);
            if (ClipValidation.HasErrors(validationMessages))
            {
                return 0UL;
            }

            List<FlattenedAnimation> flattenedAnimations = FlattenAndSortAnimations(profile);
            BlobAssetReference<ActorProfileBlob> probeBlob = BuildBlob(profile, flattenedAnimations, Allocator.Temp);
            try
            {
                return HashBlob(ref probeBlob.Value);
            }
            finally
            {
                probeBlob.Dispose();
            }
        }

        private static List<FlattenedAnimation> FlattenAndSortAnimations(ActorProfileAsset profile)
        {
            List<FlattenedAnimation> flattenedAnimations = new List<FlattenedAnimation>();
            int layerCount = profile.layers == null ? 0 : profile.layers.Count;
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                ActorLayerDefinition layer = profile.layers[layerIndex];
                if (layer == null || layer.animations == null)
                {
                    continue;
                }
                for (int animationIndex = 0; animationIndex < layer.animations.Count; animationIndex++)
                {
                    ActorAnimationDefinition animation = layer.animations[animationIndex];
                    if (animation == null)
                    {
                        continue;
                    }
                    flattenedAnimations.Add(
                        new FlattenedAnimation(animation.animationKey, (byte)layerIndex, animation));
                }
            }
            flattenedAnimations.Sort(CompareByAnimationKey);
            return flattenedAnimations;
        }

        private static int CompareByAnimationKey(FlattenedAnimation left, FlattenedAnimation right)
        {
            return left.animationKey.CompareTo(right.animationKey);
        }

        private static BlobAssetReference<ActorProfileBlob> BuildBlob(
            ActorProfileAsset profile,
            List<FlattenedAnimation> flattenedAnimations,
            AllocatorManager.AllocatorHandle allocator)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref ActorProfileBlob profileRoot = ref builder.ConstructRoot<ActorProfileBlob>();
                profileRoot.schemaVersion = SchemaVersion;
                profileRoot.turnDirections = profile.turnDirections;
                profileRoot.layerCount = (byte)(profile.layers == null ? 0 : profile.layers.Count);

                BlobBuilderArray<ActorAnimationBlob> animationArray =
                    builder.Allocate(ref profileRoot.animations, flattenedAnimations.Count);
                for (int animationIndex = 0; animationIndex < flattenedAnimations.Count; animationIndex++)
                {
                    animationArray[animationIndex] =
                        BuildAnimationBlob(flattenedAnimations[animationIndex]);
                }

                return builder.CreateBlobAssetReference<ActorProfileBlob>(allocator);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static ActorAnimationBlob BuildAnimationBlob(FlattenedAnimation flattenedAnimation)
        {
            ActorAnimationDefinition definition = flattenedAnimation.definition;
            ActorAnimationBlob animationBlob = new ActorAnimationBlob
            {
                animationKey = flattenedAnimation.animationKey,
                layerIndex = flattenedAnimation.layerIndex,
                hasDirections = definition.hasDirections,
                clip = definition.hasDirections || definition.clip == null
                    ? default
                    : definition.clip.Id,
                loop = definition.loop,
                speed = definition.speed,
                blendIn = definition.blendIn,
                ragdollTrigger = definition.ragdollTrigger,
                ragdollAtEventKey = definition.ragdollAtEventKey
            };

            if (definition.hasDirections)
            {
                animationBlob.slots = BuildDirectionSlotsBlob(definition.directionSlots);
            }

            return animationBlob;
        }

        private static DirectionSlotsBlob BuildDirectionSlotsBlob(DirectionSlots directionSlots)
        {
            if (directionSlots == null)
            {
                return default;
            }

            // A pattern that fails TryGetEffectiveDirections still returns the largest coverage its
            // filled slots actually support, rounded down — the same behaviour the editor badge
            // reports as a P4 error, so a build that somehow reaches this point degrades rather than
            // throwing on an author's malformed fill pattern.
            directionSlots.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            return new DirectionSlotsBlob
            {
                southEast = ClipIdOf(directionSlots.southEast),
                northEast = ClipIdOf(directionSlots.northEast),
                south = ClipIdOf(directionSlots.south),
                north = ClipIdOf(directionSlots.north),
                east = ClipIdOf(directionSlots.east),
                effectiveDirections = effectiveDirections
            };
        }

        private static ClipId ClipIdOf(ClipAsset clip)
        {
            return clip == null ? default : clip.Id;
        }

        // -----------------------------------------------------------------------------------
        // Content hash.
        // -----------------------------------------------------------------------------------

        private static ulong HashBlob(ref ActorProfileBlob profileRoot)
        {
            xxHash3.StreamingState hashState = new xxHash3.StreamingState(true);
            hashState.Update(profileRoot.schemaVersion);
            hashState.Update((byte)profileRoot.turnDirections);
            hashState.Update(profileRoot.layerCount);

            hashState.Update(profileRoot.animations.Length);
            for (int animationIndex = 0; animationIndex < profileRoot.animations.Length; animationIndex++)
            {
                HashAnimation(ref hashState, ref profileRoot.animations[animationIndex]);
            }

            uint2 digest = hashState.DigestHash64();
            return ((ulong)digest.y << 32) | digest.x;
        }

        private static void HashAnimation(ref xxHash3.StreamingState hashState, ref ActorAnimationBlob animationBlob)
        {
            hashState.Update(animationBlob.animationKey);
            hashState.Update(animationBlob.layerIndex);
            hashState.Update((byte)(animationBlob.hasDirections ? 1 : 0));
            hashState.Update(animationBlob.clip.Value);
            HashDirectionSlots(ref hashState, ref animationBlob.slots);
            hashState.Update((byte)animationBlob.loop);
            hashState.Update(math.asuint(animationBlob.speed));
            hashState.Update(math.asuint(animationBlob.blendIn));
            hashState.Update((byte)animationBlob.ragdollTrigger);
            hashState.Update(animationBlob.ragdollAtEventKey);
        }

        private static void HashDirectionSlots(ref xxHash3.StreamingState hashState, ref DirectionSlotsBlob slots)
        {
            hashState.Update(slots.southEast.Value);
            hashState.Update(slots.northEast.Value);
            hashState.Update(slots.south.Value);
            hashState.Update(slots.north.Value);
            hashState.Update(slots.east.Value);
            hashState.Update((byte)slots.effectiveDirections);
        }
    }
}
