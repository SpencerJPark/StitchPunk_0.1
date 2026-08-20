// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The single authoritative implementation of the architecture section 3.5 rule table
    /// (V01–V23), shared by the inspectors, the clip editor, and the bake so that all three agree on
    /// what is legal. Pure static managed code — no editor-assembly dependency, no ECS world, and
    /// no side effects on the assets it inspects.
    /// </summary>
    public static class ClipValidation
    {
        /// <summary>
        /// Validates a rig against the rules that concern it: V13 (layer count), V05 (target id
        /// uniqueness) and the billboard-root rules V21, V22 and V23 (amendment A44).
        /// </summary>
        /// <param name="rig">The rig to validate. A null rig reports V13, since a set without a rig
        /// has no layers.</param>
        /// <returns>
        /// The findings in discovery order — layer checks (V13) first, then per-target id
        /// uniqueness (V05) in target-list order, then the billboard roots (V21, V23, V22) in
        /// billboard-root order. Deliberately not sorted by rule number: the
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

            // Guarded rather than returned on: a rig with no target list still has billboard roots
            // worth checking, and an early return here would make V21–V23 depend on a list they do
            // not concern. A null target list simply resolves no target addresses.
            Dictionary<uint, string> targetNamesById = new Dictionary<uint, string>();
            int targetCount = rig.targets == null ? 0 : rig.targets.Count;
            for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
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

            ValidateBillboardRootsInto(rig, targetNamesById, messages);
        }

        /// <summary>
        /// Validates the rig's billboard roots (amendment A44): V21, V22 and V23.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>V21 is only half-reachable here, and the reachable half is the target one.</strong>
        /// A <see cref="BillboardAddressKind.RigTarget"/> address names a row of this very asset, so
        /// it can be resolved against <paramref name="targetNamesById"/> and reported now, while the
        /// author is looking at the rig. A <see cref="BillboardAddressKind.HierarchyPath"/> address
        /// names a transform of the authoring prefab, which a <see cref="RigAsset"/> does not
        /// reference and cannot see — the rig asset carries no hierarchy of its own. Path addresses
        /// are therefore resolved by the entity bake, which does hold the prefab, and which reports
        /// an unresolved one rather than silently dropping the root.
        /// </para>
        /// <para>
        /// This is the same shape as V08's split (amendment A12): a rule whose evidence lives in an
        /// assembly the validator cannot legally reach is checked where the evidence is, and saying
        /// so here is what stops a later reader assuming the silence means "valid".
        /// </para>
        /// </remarks>
        private static void ValidateBillboardRootsInto(
            RigAsset rig,
            Dictionary<uint, string> targetNamesById,
            List<ValidationMessage> messages)
        {
            if (rig.billboardRoots == null)
            {
                return;
            }

            Dictionary<string, string> rootNamesByAddress = new Dictionary<string, string>();
            for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
            {
                BillboardRootDefinition rootDefinition = rig.billboardRoots[rootIndex];
                if (rootDefinition == null)
                {
                    continue;
                }

                bool addressResolves = true;
                if (rootDefinition.address.kind == BillboardAddressKind.RigTarget
                    && !targetNamesById.ContainsKey(rootDefinition.address.targetId))
                {
                    addressResolves = false;
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V21,
                        rig,
                        "Billboard root '" + rootDefinition.displayName + "' addresses target id " +
                        rootDefinition.address.targetId.ToString() + ", which rig '" + rig.name +
                        "' does not define."));
                }

                if (rootDefinition.mode == BillboardMode.AxisConstrained
                    && math.lengthsq(rootDefinition.constraintAxis) <= 0f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V23,
                        rig,
                        "Billboard root '" + rootDefinition.displayName + "' in rig '" + rig.name +
                        "' is axis-constrained but its constraint axis is zero-length, so there is " +
                        "no axis to turn about."));
                }

                // An address that does not resolve cannot duplicate another one in any meaningful
                // sense — two roots both pointing at a target that is not there is one fault, not
                // two, and reporting it twice buries the fix under its own symptom.
                if (!addressResolves)
                {
                    continue;
                }

                string addressKey = DescribeBillboardAddress(rootDefinition.address);
                string previousRootName;
                if (rootNamesByAddress.TryGetValue(addressKey, out previousRootName))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V22,
                        rig,
                        "Billboard roots '" + previousRootName + "' and '" +
                        rootDefinition.displayName + "' both address " + addressKey + " in rig '" +
                        rig.name + "'; a node may declare at most one billboard root."));
                }
                else
                {
                    rootNamesByAddress.Add(addressKey, rootDefinition.displayName);
                }
            }
        }

        /// <summary>
        /// Renders a billboard address as the key V22 compares on, and as the text it reports.
        /// </summary>
        /// <remarks>
        /// The kind is part of the key because the two kinds address disjoint things: target id 7
        /// and the path "7" are not the same node, and a key that could not tell them apart would
        /// report a duplicate that is not one.
        /// </remarks>
        private static string DescribeBillboardAddress(BillboardNodeAddress address)
        {
            if (address.kind == BillboardAddressKind.RigTarget)
            {
                return "target " + address.targetId.ToString();
            }
            // An empty path addresses the prefab root, which is a real node and a legal thing to
            // billboard — so it is named rather than treated as a missing value.
            return string.IsNullOrEmpty(address.hierarchyPath)
                ? "the prefab root"
                : "path '" + address.hierarchyPath + "'";
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
        /// <summary>
        /// Reports rule V17 for one key's Bézier handles, and says nothing for any other mode.
        /// </summary>
        /// <remarks>
        /// The all-zero pair is exempt. That is the value a key deserializes to when these fields
        /// did not exist yet, and <c>ClipSampler.EaseBezier</c> reads it as linear rather than as a
        /// curve — so reporting it would flag every clip authored before Bézier existed for a shape
        /// nothing will ever evaluate.
        /// </remarks>
        private static void ValidateBezierHandlesInto(
            ClipAsset clip,
            Interpolation interpolation,
            Unity.Mathematics.float2 startHandle,
            Unity.Mathematics.float2 endHandle,
            string keyDescription,
            List<ValidationMessage> messages)
        {
            if (interpolation != Interpolation.Bezier)
            {
                return;
            }
            if (startHandle.x == 0f && startHandle.y == 0f && endHandle.x == 0f && endHandle.y == 0f)
            {
                return;
            }

            if (IsInsideUnitSquare(startHandle) && IsInsideUnitSquare(endHandle))
            {
                return;
            }

            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                ValidationCode.V17,
                clip,
                keyDescription + " of clip '" + clip.name + "' has Bezier handles (" +
                startHandle.x + ", " + startHandle.y + ") and (" + endHandle.x + ", " +
                endHandle.y + ") outside the unit square; x must stay in [0,1] for the curve to be " +
                "a function of time, and y must stay in [0,1] because the bake's bounds assume a " +
                "segment never travels past its own keys."));
        }

        private static bool IsInsideUnitSquare(Unity.Mathematics.float2 handle)
        {
            return handle.x >= 0f && handle.x <= 1f && handle.y >= 0f && handle.y <= 1f;
        }

        private static void ValidateBoneBezierHandlesInto(
            ClipAsset clip, List<ValidationMessage> messages)
        {
            int boneTrackCount = clip.boneTracks == null ? 0 : clip.boneTracks.Count;
            for (int trackIndex = 0; trackIndex < boneTrackCount; trackIndex++)
            {
                BoneTrack boneTrack = clip.boneTracks[trackIndex];
                if (boneTrack == null || boneTrack.keys == null)
                {
                    continue;
                }
                for (int keyIndex = 0; keyIndex < boneTrack.keys.Count; keyIndex++)
                {
                    BoneKey boneKey = boneTrack.keys[keyIndex];
                    ValidateBezierHandlesInto(
                        clip,
                        boneKey.interpolation,
                        boneKey.bezierStartHandle,
                        boneKey.bezierEndHandle,
                        "Bone track " + trackIndex + " key " + keyIndex,
                        messages);
                }
            }
        }

        /// <summary>
        /// Validates the clip's billboard tracks (amendment A44): V24, V03 and V04.
        /// </summary>
        /// <remarks>
        /// V24 is V02's shape against a different id space — a billboard root rather than a rig
        /// target — so it is checked here rather than folded into the transform-track loop, which
        /// resolves ids against <c>rig.targets</c> and would report the wrong list.
        /// </remarks>
        private static void ValidateBillboardTracksInto(
            ClipAsset clip, List<ValidationMessage> messages)
        {
            if (clip.billboardTracks == null)
            {
                return;
            }

            for (int trackIndex = 0; trackIndex < clip.billboardTracks.Count; trackIndex++)
            {
                BillboardTrack track = clip.billboardTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }

                if (!RigDeclaresBillboardRoot(clip.rig, track.rootStableId))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V24,
                        clip,
                        "Billboard track " + trackIndex + " in clip '" + clip.name +
                        "' animates billboard root id " + track.rootStableId.ToString() +
                        ", which its rig does not declare."));
                    continue;
                }

                int keyCount = track.keys == null ? 0 : track.keys.Count;
                float previousTime = float.NegativeInfinity;
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    BillboardKey key = track.keys[keyIndex];
                    ValidateNormalizedTimeInto(
                        clip,
                        key.normalizedTime,
                        "Billboard track " + trackIndex + " key " + keyIndex,
                        messages);

                    if (key.normalizedTime <= previousTime)
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V03,
                            clip,
                            "Billboard track " + trackIndex + " in clip '" + clip.name +
                            "' has keys out of order at index " + keyIndex +
                            "; keys must ascend strictly in normalized time."));
                    }
                    previousTime = key.normalizedTime;
                }
            }
        }

        private static bool RigDeclaresBillboardRoot(RigAsset rig, uint rootStableId)
        {
            if (rig == null || rig.billboardRoots == null || rootStableId == 0u)
            {
                return false;
            }
            for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
            {
                BillboardRootDefinition definition = rig.billboardRoots[rootIndex];
                if (definition != null && definition.stableId == rootStableId)
                {
                    return true;
                }
            }
            return false;
        }

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

                    ValidateBezierHandlesInto(
                        clip,
                        transformTrack.keys[keyIndex].interpolation,
                        transformTrack.keys[keyIndex].bezierStartHandle,
                        transformTrack.keys[keyIndex].bezierEndHandle,
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

                    // V14 is about the absolute-mode sentinel and applies only there. A relative
                    // key's number is a displacement, so -3 is three frames back, not a malformed
                    // index — reporting it would train authors to ignore the rule.
                    if (spriteKey.indexMode == SpriteIndexMode.Absolute && spriteKey.sliceIndex < -1)
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Warning,
                            ValidationCode.V14,
                            clip,
                            "Sprite track " + trackIndex + " key " + keyIndex + " of clip '" +
                            clip.name + "' has slice index " + spriteKey.sliceIndex +
                            "; -1 is the lowest meaningful value and means \"no change\"."));
                    }

                    if (spriteKey.indexMode == SpriteIndexMode.RelativeToBase)
                    {
                        int resolvedIndex = SpriteIndexResolver.Resolve(
                            spriteKey.sliceIndex, spriteKey.indexMode, spriteTrack.baseIndex);
                        if (resolvedIndex < 0)
                        {
                            messages.Add(new ValidationMessage(
                                ValidationSeverity.Error,
                                ValidationCode.V18,
                                clip,
                                "Sprite track " + trackIndex + " key " + keyIndex + " of clip '" +
                                clip.name + "' is relative with offset " + spriteKey.sliceIndex +
                                " against base index " + spriteTrack.baseIndex +
                                ", which resolves to " + resolvedIndex +
                                "; a relative key has no \"no change\" sentinel, so this cannot " +
                                "name a frame."));
                        }
                    }
                }
            }

            ValidateBoneTracksInto(clip, messages);
            ValidateBoneBezierHandlesInto(clip, messages);
            ValidateBillboardTracksInto(clip, messages);

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

                ValidateEventWindowInto(clip, eventMarker, eventIndex, messages);
            }
        }

        /// <summary>
        /// Checks one marker's window duration: negative is an error (V19), and a window on a key
        /// that owns no mask bit is a warning (V20).
        /// </summary>
        /// <remarks>
        /// A window longer than the clip is deliberately <em>not</em> reported. On a looping clip it
        /// is the ordinary way to say "open for the whole loop", and on a Once clip it simply means
        /// the window outlives the clip, which the layer going inactive already resolves. Flagging
        /// it would be flagging a legitimate authoring choice.
        /// </remarks>
        private static void ValidateEventWindowInto(
            ClipAsset clip,
            EventMarker eventMarker,
            int eventIndex,
            List<ValidationMessage> messages)
        {
            if (eventMarker.windowSeconds < 0f)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V19,
                    clip,
                    "Event " + eventIndex + " of clip '" + clip.name + "' has a window of " +
                    eventMarker.windowSeconds + " seconds; a window cannot be negative. The bake " +
                    "clamps it to 0, which makes the event pulse-only."));
                return;
            }

            if (eventMarker.windowSeconds > 0f
                && !AnimEventMaskKeys.IsMaskable(eventMarker.eventKey))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.V20,
                    clip,
                    "Event " + eventIndex + " of clip '" + clip.name + "' authors a " +
                    eventMarker.windowSeconds + "s window on key " + eventMarker.eventKey +
                    ", which is outside the maskable range " + AnimEventMaskKeys.FirstMaskKey +
                    "–" + AnimEventMaskKeys.LastMaskKey + ". The event still fires, but no " +
                    "AnimEventMask bit exists for it, so the window can never be observed."));
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
