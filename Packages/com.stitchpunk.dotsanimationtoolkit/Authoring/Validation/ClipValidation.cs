// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The single authoritative implementation of the architecture section 3.5 rule table
    /// (V01–V16), shared by the inspectors, the clip editor, and the bake so that all three agree on
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
        /// <returns>
        /// The findings in discovery order — layer checks (V13) first, then per-target id
        /// uniqueness (V05) in target-list order. Deliberately not sorted by rule number: the
        /// inspector and the clip editor list findings in the order the asset reads, so a reader
        /// can walk the asset top to bottom. Empty when the rig is fully valid.
        /// </returns>
        public static List<ValidationMessage> ValidateRig(RigAsset rig)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateRigInto(rig, messages);
            return messages;
        }

        /// <summary>
        /// Validates one clip against the rules that concern a clip in isolation: V01, V02, V03,
        /// V04, V09, V10, V12, V14, V15 and V16. Set-scoped rules (V05, V06, V07, V08, V11) are
        /// checked by <see cref="ValidateSet"/>. V16 (duplicate bone name) is a clip-local sibling of
        /// V05 rather than a V05 case itself: a bone track has no stable id, so its only identity is
        /// the name, and uniqueness of that name only ever needs judging within one clip — there is
        /// no set- or rig-scoped notion of "the same bone" the way there is for a <c>ClipId</c> or a
        /// <c>TargetId</c>.
        /// </summary>
        /// <param name="clip">The clip to validate.</param>
        /// <returns>
        /// The findings in discovery order — the clip-level rules (V01, V10, V12) first, then each
        /// transform and sprite track in authoring order (V02, V03, V04, V14), then each bone track
        /// in authoring order (V03, V04, V15, V16), then each event (V04, V09). Deliberately not
        /// sorted by rule number, so a reader can walk the asset top to bottom. Empty when the clip
        /// is fully valid.
        /// </returns>
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

        /// <summary>
        /// Validates the clip's authored bone tracks (amendment A42): V03, V04, V15 and V16.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A bone track is checked for a <em>name</em> where a transform or sprite track is checked
        /// for a target binding. That asymmetry is the design: a rig target is a row this package
        /// owns and can assign a stable id to, while a bone lives in an imported hierarchy it does
        /// not own, so the name is the only handle Unity offers.
        /// </para>
        /// <para>
        /// Whether the name resolves to a real bone is deliberately <strong>not</strong> checked
        /// here. Validation sees only the asset graph, and the skeleton lives on a prefab the clip
        /// does not reference — the VAT bake is the first point where the hierarchy exists, so that
        /// is where an unresolved name is reported. Guessing here would produce false errors for
        /// every clip authored before its rig was imported.
        /// </para>
        /// </remarks>
        private static void ValidateBoneTracksInto(ClipAsset clip, List<ValidationMessage> messages)
        {
            int boneTrackCount = clip.boneTracks == null ? 0 : clip.boneTracks.Count;
            for (int trackIndex = 0; trackIndex < boneTrackCount; trackIndex++)
            {
                BoneTrack boneTrack = clip.boneTracks[trackIndex];
                if (boneTrack == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(boneTrack.boneName))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V15,
                        clip,
                        "Bone track " + trackIndex + " of clip '" + clip.name +
                        "' has no bone name, so it names nothing for the VAT bake to pose."));
                }
                else
                {
                    for (int earlierIndex = 0; earlierIndex < trackIndex; earlierIndex++)
                    {
                        BoneTrack earlierTrack = clip.boneTracks[earlierIndex];
                        if (earlierTrack == null || earlierTrack.boneName != boneTrack.boneName)
                        {
                            continue;
                        }
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V16,
                            clip,
                            "Bone tracks " + earlierIndex + " and " + trackIndex + " of clip '" +
                            clip.name + "' both animate bone '" + boneTrack.boneName +
                            "'. The bake applies tracks in order, so the later one would silently " +
                            "win and the earlier one's keys would never be seen."));
                        break;
                    }
                }

                int keyCount = boneTrack.keys == null ? 0 : boneTrack.keys.Count;
                float previousKeyTime = float.NegativeInfinity;
                bool reportedUnsortedKeys = false;
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    BoneKey boneKey = boneTrack.keys[keyIndex];
                    if (!reportedUnsortedKeys && boneKey.normalizedTime <= previousKeyTime)
                    {
                        reportedUnsortedKeys = true;
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V03,
                            clip,
                            "Bone track " + trackIndex + " of clip '" + clip.name +
                            "' is not strictly time-sorted: key " + keyIndex + " is at " +
                            boneKey.normalizedTime + " but the previous key is at " +
                            previousKeyTime + "."));
                    }
                    previousKeyTime = boneKey.normalizedTime;
                    ValidateNormalizedTimeInto(
                        clip,
                        boneKey.normalizedTime,
                        "Bone track " + trackIndex + " key " + keyIndex,
                        messages);
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

            ValidateBoneTracksInto(clip, messages);

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
            // Amendment A36: a VAT source counts as present only when it actually names a source
            // clip. `vatSource` is a plain [Serializable] class field rather than a
            // [SerializeReference] one, so Unity cannot represent null for it on disk — every clip
            // asset that has ever been saved and re-read carries a default-constructed
            // VatClipSource with a null sourceClip. Testing the field for null therefore reported
            // "has a VAT source" for every non-VAT clip in the project, failing V07 on any set
            // without a texture set, which throws out of ClipRegistryBuilder and bakes no registry
            // at all. An empty source names nothing for VatTextureBaker to sample, so it carries no
            // VAT intent and must not be treated as one.
            bool hasLegacySource = clip.vatSource != null && clip.vatSource.sourceClip != null;

            // C10: `vatTracks` does NOT repeat the A36 trap. It is a List<VatTrack>, and Unity
            // round-trips an empty list as an empty list rather than manufacturing a phantom element
            // the way it does for a lone [Serializable] class field, so a clip that never used this
            // feature reads back with a genuinely empty list — no null-vs-default disambiguation is
            // needed here the way it is for vatSource. A row with no sourceClip yet (added in the
            // inspector but not filled in) still carries no VAT intent, so it is skipped exactly
            // like an empty vatSource.
            int vatTrackCount = clip.vatTracks == null ? 0 : clip.vatTracks.Count;
            bool hasAnyTrackSource = false;
            for (int trackIndex = 0; trackIndex < vatTrackCount; trackIndex++)
            {
                VatTrack track = clip.vatTracks[trackIndex];
                if (track != null && track.sourceClip != null)
                {
                    hasAnyTrackSource = true;
                    break;
                }
            }

            if (!hasLegacySource && !hasAnyTrackSource)
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

            if (hasLegacySource)
            {
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

            for (int trackIndex = 0; trackIndex < vatTrackCount; trackIndex++)
            {
                VatTrack track = clip.vatTracks[trackIndex];
                if (track == null || track.sourceClip == null)
                {
                    continue;
                }

                ValidateTargetBindingInto(clip, track.targetId, "VAT track", trackIndex, messages);

                if (!HasExactVatTrackRange(clipSet.vatTextures, clip.stableId, track.targetId))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V07,
                        clipSet.vatTextures,
                        "VAT texture set '" + clipSet.vatTextures.name +
                        "' holds no baked frame range for target " +
                        new TargetId(track.targetId).ToString() + " of VAT-sourced clip '" +
                        clip.name + "'."));
                }
            }
        }

        /// <summary>
        /// True when <paramref name="vatTextures"/> holds a range baked specifically for
        /// (<paramref name="clipId"/>, <paramref name="targetId"/>) — an exact match, never the
        /// untargeted-range fallback <see cref="VatTextureSetAsset.TryGetTrackRange"/> performs.
        /// </summary>
        /// <remarks>
        /// Coverage for a <see cref="VatTrack"/> must be judged strictly: if this fell back to the
        /// untargeted range the way runtime resolution does, a track naming a target that was never
        /// actually baked would pass validation while silently rendering whatever motion the
        /// clip-wide <c>vatSource</c> baked instead — the wrong mesh's animation, discovered only by
        /// looking at the actor rather than at a validation message.
        /// </remarks>
        /// <param name="vatTextures">The texture set to search; must not be null.</param>
        /// <param name="clipId">Stable id of the clip the track belongs to.</param>
        /// <param name="targetId">Stable id of the target the track names.</param>
        /// <returns>True when an exact (clip, target) range was baked.</returns>
        private static bool HasExactVatTrackRange(VatTextureSetAsset vatTextures, ulong clipId, uint targetId)
        {
            if (vatTextures.clipRanges == null)
            {
                return false;
            }
            for (int rangeIndex = 0; rangeIndex < vatTextures.clipRanges.Count; rangeIndex++)
            {
                VatClipRange candidate = vatTextures.clipRanges[rangeIndex];
                if (candidate.clipId == clipId && candidate.targetId == targetId)
                {
                    return true;
                }
            }
            return false;
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
