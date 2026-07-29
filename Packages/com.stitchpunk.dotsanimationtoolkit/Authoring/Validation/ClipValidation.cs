// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The single authoritative implementation of the architecture section 3.5 rule table
    /// (V01–V14), shared by the inspectors, the clip editor, and the bake so that all three agree on
    /// what is legal. Pure static managed code — no editor-assembly dependency, no ECS world, and
    /// no side effects on the assets it inspects.
    /// </summary>
    public static class ClipValidation
    {
        /// <summary>
        /// Validates a rig against the rules that concern it: V13 (layer count) and V05 (target id
        /// uniqueness).
        /// </summary>
        /// <param name="rig">The rig to validate. A null rig reports V13, since a set without a rig
        /// has no layers.</param>
        /// <returns>The findings, in rule order; empty when the rig is fully valid.</returns>
        public static List<ValidationMessage> ValidateRig(RigAsset rig)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateRigInto(rig, messages);
            return messages;
        }

        /// <summary>
        /// Validates one clip against the rules that concern a clip in isolation: V01, V02, V03,
        /// V04, V09, V10, V12 and V14. Set-scoped rules (V05, V06, V07, V08, V11) are checked by
        /// <see cref="ValidateSet"/>.
        /// </summary>
        /// <param name="clip">The clip to validate.</param>
        /// <returns>The findings, in rule order; empty when the clip is fully valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip"/> is null.</exception>
        public static List<ValidationMessage> ValidateClip(ClipAsset clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateClipInto(clip, messages);
            return messages;
        }

        /// <summary>
        /// Validates a whole clip set: its rig, every clip it registers, and the set-scoped rules
        /// V05, V06, V07, V08 and V11.
        /// </summary>
        /// <param name="clipSet">The set to validate.</param>
        /// <param name="stage">
        /// Which caller is validating. <see cref="ValidationStage.Bake"/> downgrades V08 to a
        /// warning, because outdated VAT textures still render (architecture section 3.5).
        /// </param>
        /// <param name="vatSourceHashRecomputed">
        /// True when <paramref name="recomputedVatSourceHash"/> holds a freshly recomputed hash of
        /// the texture set's sources. V08 can only be judged when it does; the authoring assembly
        /// cannot recompute the hash itself, since that requires the editor-only VAT baker.
        /// </param>
        /// <param name="recomputedVatSourceHash">
        /// The freshly recomputed source hash to compare against
        /// <see cref="VatTextureSetAsset.sourceHash"/>. Ignored unless
        /// <paramref name="vatSourceHashRecomputed"/> is true.
        /// </param>
        /// <returns>The findings, rig first and then clip by clip in list order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clipSet"/> is null.</exception>
        public static List<ValidationMessage> ValidateSet(
            ClipSetAsset clipSet,
            ValidationStage stage = ValidationStage.Authoring,
            bool vatSourceHashRecomputed = false,
            ulong recomputedVatSourceHash = 0UL)
        {
            if (clipSet == null)
            {
                throw new ArgumentNullException(nameof(clipSet));
            }

            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateRigInto(clipSet.rig, messages);

            // Dedup by asset identity. UnityEngine.Object overrides Equals/GetHashCode to compare
            // instances, so the set itself carries the identity semantics — no instance-id call.
            HashSet<ClipAsset> seenClips = new HashSet<ClipAsset>();
            Dictionary<ulong, ClipAsset> clipsByStableId = new Dictionary<ulong, ClipAsset>();
            if (clipSet.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }
                    if (!seenClips.Add(clip))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Warning,
                            ValidationCode.V11,
                            clipSet,
                            "Clip '" + clip.name + "' is listed more than once in set '" +
                            clipSet.name + "'; the duplicate entry is dropped at bake."));
                        continue;
                    }

                    ClipAsset clipWithSameId;
                    if (clipsByStableId.TryGetValue(clip.stableId, out clipWithSameId))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V05,
                            clip,
                            "Clips '" + clipWithSameId.name + "' and '" + clip.name +
                            "' share clip id " + new ClipId(clip.stableId).ToString() +
                            " inside set '" + clipSet.name + "'."));
                    }
                    else
                    {
                        clipsByStableId.Add(clip.stableId, clip);
                    }

                    if (clip.rig != clipSet.rig)
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V06,
                            clip,
                            "Clip '" + clip.name + "' is authored against a different rig than set '" +
                            clipSet.name + "'; every clip in a set must share the set's rig."));
                    }

                    ValidateClipInto(clip, messages);
                    ValidateVatCoverageInto(clipSet, clip, messages);
                }
            }

            if (vatSourceHashRecomputed &&
                clipSet.vatTextures != null &&
                clipSet.vatTextures.sourceHash != recomputedVatSourceHash)
            {
                ValidationSeverity staleBakeSeverity = stage == ValidationStage.Bake
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error;
                messages.Add(new ValidationMessage(
                    staleBakeSeverity,
                    ValidationCode.V08,
                    clipSet.vatTextures,
                    "VAT texture set '" + clipSet.vatTextures.name +
                    "' was baked from different sources than the ones referenced now; rebake it."));
            }

            return messages;
        }

        /// <summary>
        /// True when any finding in the list blocks baking.
        /// </summary>
        /// <param name="messages">The findings to scan; a null list counts as no errors.</param>
        /// <returns>True when at least one finding has <see cref="ValidationSeverity.Error"/>.</returns>
        public static bool HasErrors(IReadOnlyList<ValidationMessage> messages)
        {
            if (messages == null)
            {
                return false;
            }
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                if (messages[messageIndex].IsError)
                {
                    return true;
                }
            }
            return false;
        }

        // -----------------------------------------------------------------------------------
        // Rule implementations.
        // -----------------------------------------------------------------------------------

        private static void ValidateRigInto(RigAsset rig, List<ValidationMessage> messages)
        {
            if (rig == null)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V13,
                    null,
                    "No rig is assigned, so no playback layers are defined; a rig must declare " +
                    "between 1 and " + RigAsset.MaxLayerCount + " layers."));
                return;
            }

            int layerCount = rig.layers == null ? 0 : rig.layers.Count;
            if (layerCount == 0 || layerCount > RigAsset.MaxLayerCount)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V13,
                    rig,
                    "Rig '" + rig.name + "' declares " + layerCount + " layers; it must declare " +
                    "between 1 and " + RigAsset.MaxLayerCount + "."));
            }

            if (rig.targets == null)
            {
                return;
            }
            Dictionary<uint, string> targetNamesById = new Dictionary<uint, string>();
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition == null)
                {
                    continue;
                }
                string previousTargetName;
                if (targetNamesById.TryGetValue(targetDefinition.stableId, out previousTargetName))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V05,
                        rig,
                        "Targets '" + previousTargetName + "' and '" + targetDefinition.displayName +
                        "' share target id " + targetDefinition.Id.ToString() + " in rig '" +
                        rig.name + "'."));
                }
                else
                {
                    targetNamesById.Add(targetDefinition.stableId, targetDefinition.displayName);
                }
            }
        }

        private static void ValidateClipInto(ClipAsset clip, List<ValidationMessage> messages)
        {
            if (clip.duration < ClipAsset.MinimumDuration)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V01,
                    clip,
                    "Clip '" + clip.name + "' has a duration of " + clip.duration +
                    " s; the minimum is " + ClipAsset.MinimumDuration + " s."));
            }

            int transformTrackCount = clip.transformTracks == null ? 0 : clip.transformTracks.Count;
            int spriteTrackCount = clip.spriteTracks == null ? 0 : clip.spriteTracks.Count;
            int eventCount = clip.events == null ? 0 : clip.events.Count;
            if (transformTrackCount == 0 && spriteTrackCount == 0 && eventCount == 0)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.V10,
                    clip,
                    "Clip '" + clip.name + "' has no tracks and no events; it holds the rest pose."));
            }

            if (clip.defaultBlendIn > clip.duration)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.V12,
                    clip,
                    "Clip '" + clip.name + "' has a default blend-in of " + clip.defaultBlendIn +
                    " s, longer than its " + clip.duration + " s duration; it is clamped at bake."));
            }
            if (clip.defaultBlendOut > clip.duration)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.V12,
                    clip,
                    "Clip '" + clip.name + "' has a default blend-out of " + clip.defaultBlendOut +
                    " s, longer than its " + clip.duration + " s duration; it is clamped at bake."));
            }

            for (int trackIndex = 0; trackIndex < transformTrackCount; trackIndex++)
            {
                TransformTrack transformTrack = clip.transformTracks[trackIndex];
                if (transformTrack == null)
                {
                    continue;
                }
                ValidateTargetBindingInto(clip, transformTrack.targetId, "Transform track", trackIndex, messages);

                int keyCount = transformTrack.keys == null ? 0 : transformTrack.keys.Count;
                float previousKeyTime = float.NegativeInfinity;
                bool reportedUnsortedKeys = false;
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    float keyTime = transformTrack.keys[keyIndex].normalizedTime;
                    if (!reportedUnsortedKeys && keyTime <= previousKeyTime)
                    {
                        reportedUnsortedKeys = true;
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V03,
                            clip,
                            "Transform track " + trackIndex + " of clip '" + clip.name +
                            "' is not strictly time-sorted: key " + keyIndex + " is at " + keyTime +
                            " but the previous key is at " + previousKeyTime + "."));
                    }
                    previousKeyTime = keyTime;
                    ValidateNormalizedTimeInto(
                        clip,
                        keyTime,
                        "Transform track " + trackIndex + " key " + keyIndex,
                        messages);
                }
            }

            for (int trackIndex = 0; trackIndex < spriteTrackCount; trackIndex++)
            {
                SpriteTrack spriteTrack = clip.spriteTracks[trackIndex];
                if (spriteTrack == null)
                {
                    continue;
                }
                ValidateTargetBindingInto(clip, spriteTrack.targetId, "Sprite track", trackIndex, messages);

                int keyCount = spriteTrack.keys == null ? 0 : spriteTrack.keys.Count;
                float previousKeyTime = float.NegativeInfinity;
                bool reportedUnsortedKeys = false;
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    SpriteKey spriteKey = spriteTrack.keys[keyIndex];
                    if (!reportedUnsortedKeys && spriteKey.normalizedTime <= previousKeyTime)
                    {
                        reportedUnsortedKeys = true;
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V03,
                            clip,
                            "Sprite track " + trackIndex + " of clip '" + clip.name +
                            "' is not strictly time-sorted: key " + keyIndex + " is at " +
                            spriteKey.normalizedTime + " but the previous key is at " +
                            previousKeyTime + "."));
                    }
                    previousKeyTime = spriteKey.normalizedTime;
                    ValidateNormalizedTimeInto(
                        clip,
                        spriteKey.normalizedTime,
                        "Sprite track " + trackIndex + " key " + keyIndex,
                        messages);

                    if (spriteKey.sliceIndex < -1)
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Warning,
                            ValidationCode.V14,
                            clip,
                            "Sprite track " + trackIndex + " key " + keyIndex + " of clip '" +
                            clip.name + "' has slice index " + spriteKey.sliceIndex +
                            "; -1 is the lowest meaningful value and means \"no change\"."));
                    }
                }
            }

            for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                EventMarker eventMarker = clip.events[eventIndex];
                ValidateNormalizedTimeInto(clip, eventMarker.normalizedTime, "Event " + eventIndex, messages);
                if (eventMarker.eventKey < (uint)ReservedEventKeys.FirstUserKey)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V09,
                        clip,
                        "Event " + eventIndex + " of clip '" + clip.name + "' uses key " +
                        eventMarker.eventKey + "; keys below " +
                        (uint)ReservedEventKeys.FirstUserKey + " are reserved by the package."));
                }
            }
        }

        private static void ValidateTargetBindingInto(
            ClipAsset clip,
            uint targetId,
            string trackKindLabel,
            int trackIndex,
            List<ValidationMessage> messages)
        {
            if (RigContainsTarget(clip.rig, targetId))
            {
                return;
            }
            string rigLabel = clip.rig == null ? "no rig (none assigned)" : "rig '" + clip.rig.name + "'";
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                ValidationCode.V02,
                clip,
                trackKindLabel + " " + trackIndex + " of clip '" + clip.name + "' targets id " +
                new TargetId(targetId).ToString() + ", which is not defined by " + rigLabel + "."));
        }

        private static void ValidateNormalizedTimeInto(
            ClipAsset clip,
            float normalizedTime,
            string locationLabel,
            List<ValidationMessage> messages)
        {
            if (normalizedTime >= 0f && normalizedTime <= 1f)
            {
                return;
            }
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                ValidationCode.V04,
                clip,
                locationLabel + " of clip '" + clip.name + "' has normalized time " +
                normalizedTime + ", which is outside [0, 1]."));
        }

        private static void ValidateVatCoverageInto(
            ClipSetAsset clipSet,
            ClipAsset clip,
            List<ValidationMessage> messages)
        {
            if (clip.vatSource == null)
            {
                return;
            }
            if (clipSet.vatTextures == null)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V07,
                    clipSet,
                    "Clip '" + clip.name + "' has a VAT source but set '" + clipSet.name +
                    "' references no VAT texture set."));
                return;
            }
            VatClipRange bakedRange;
            if (!clipSet.vatTextures.TryGetClipRange(clip.stableId, out bakedRange))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V07,
                    clipSet.vatTextures,
                    "VAT texture set '" + clipSet.vatTextures.name +
                    "' holds no baked frame range for VAT-sourced clip '" + clip.name + "'."));
            }
        }

        /// <summary>
        /// True when <paramref name="rig"/> declares a target row carrying
        /// <paramref name="targetId"/>. A null rig, a null row, and the reserved id 0 all answer
        /// false.
        /// </summary>
        /// <param name="rig">The rig to search.</param>
        /// <param name="targetId">The raw target stable id to look for.</param>
        /// <returns>True when the rig defines that target.</returns>
        public static bool RigContainsTarget(RigAsset rig, uint targetId)
        {
            if (rig == null || rig.targets == null || targetId == 0u)
            {
                return false;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition != null && targetDefinition.stableId == targetId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
