// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The single authoritative implementation of the validation rule table, shared by the
    /// inspectors, the clip editor, and the bake so that all three agree on what is legal. Pure
    /// static managed code with no side effects on the assets it inspects.
    /// </summary>
    public static class ClipValidation
    {
        /// <summary>Validates a rig: target/tag id uniqueness, billboard roots, ragdoll bodies.</summary>
        /// <returns>
        /// Findings in discovery order — the order the asset reads top to bottom, not sorted by
        /// rule number. Empty when the rig is fully valid.
        /// </returns>
        public static List<ValidationMessage> ValidateRig(RigAsset rig)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateRigInto(rig, messages);
            return messages;
        }

        /// <summary>
        /// Validates one clip in isolation — no binding rule is judged here, since a clip names no
        /// rig and cannot answer whether a tag or target id resolves; that is
        /// <see cref="ValidateBind"/>'s question.
        /// </summary>
        /// <param name="tagRegistry">
        /// Optional: without it, a track's tag id that no longer exists anywhere cannot be told
        /// apart from one that just doesn't apply to a particular rig, and the milder finding is
        /// reported for both rather than silently passing either.
        /// </param>
        /// <returns>Findings in discovery order (asset reading order, not rule number). Empty when the clip is fully valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip"/> is null.</exception>
        public static List<ValidationMessage> ValidateClip(
            ClipAsset clip, TargetTagRegistry tagRegistry = null)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateClipInto(clip, null, tagRegistry, messages);
            return messages;
        }

        /// <summary>Validates one bind — a rig and the clip sets played on it — plus every clip in the merged union.</summary>
        /// <param name="rig">
        /// The rig the sets are bound to. Null means unbound, not broken: a clip set on its own
        /// genuinely has no rig, so a null rig judges everything a set can answer alone and stays
        /// silent on every rule that needs a rig to answer.
        /// </param>
        /// <param name="clipSets">Nulls and repeated entries are ignored; canonicalised by set id before anything is judged.</param>
        /// <param name="stage">
        /// <see cref="ValidationStage.Bake"/> downgrades a stale VAT bake to a warning, since
        /// outdated textures still render, and skips the rig-target-existence check entirely.
        /// </param>
        /// <param name="vatSourceHashRecomputed">
        /// True when <paramref name="recomputedVatSourceHash"/> holds a freshly recomputed hash;
        /// the stale-bake check can only run when it does, since recomputing needs the editor-only
        /// VAT baker this assembly cannot reach.
        /// </param>
        /// <param name="recomputedVatSourceHash">
        /// Compared against <see cref="VatTextureSetAsset.sourceHash"/>. Ignored unless
        /// <paramref name="vatSourceHashRecomputed"/> is true.
        /// </param>
        /// <returns>The findings, rig first and then clip by clip in canonical bind order.</returns>
        public static List<ValidationMessage> ValidateBind(
            RigAsset rig,
            IReadOnlyList<ClipSetAsset> clipSets,
            ValidationStage stage = ValidationStage.Authoring,
            bool vatSourceHashRecomputed = false,
            ulong recomputedVatSourceHash = 0UL,
            TargetTagRegistry tagRegistry = null)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            if (rig != null)
            {
                ValidateRigInto(rig, messages);
            }

            List<ClipSetAsset> canonicalClipSets = ClipRegistryBuilder.BuildCanonicalClipSets(clipSets);
            VatTextureSetAsset bindVatTextures =
                ValidateVatTextureSetsInto(rig, canonicalClipSets, messages);

            // Dedup by asset identity. UnityEngine.Object overrides Equals/GetHashCode to compare
            // instances, so the set itself carries the identity semantics — no instance-id call.
            // Both maps span the whole union, not one set: the moment two independently-authored
            // sets meet on one actor, a clip listed in both (V11) or two distinct clips minted with
            // the same id (V05) become possible for the first time.
            HashSet<ClipAsset> seenClips = new HashSet<ClipAsset>();
            Dictionary<ulong, ClipAsset> clipsByStableId = new Dictionary<ulong, ClipAsset>();
            for (int setIndex = 0; setIndex < canonicalClipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = canonicalClipSets[setIndex];
                if (clipSet.clips == null)
                {
                    continue;
                }
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
                            "Clip '" + clip.name + "' is registered more than once across the sets " +
                            "bound here (set '" + clipSet.name +
                            "'); the duplicate entry is dropped at bake."));
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
                            " among the sets bound to rig '" + DescribeRig(rig) +
                            "'; one of them would be unreachable."));
                    }
                    else
                    {
                        clipsByStableId.Add(clip.stableId, clip);
                    }

                    ValidateClipInto(clip, rig, tagRegistry, messages);
                    ValidateVatCoverageInto(clipSet, clip, rig, messages);
                }
            }

            if (vatSourceHashRecomputed &&
                bindVatTextures != null &&
                bindVatTextures.sourceHash != recomputedVatSourceHash)
            {
                ValidationSeverity staleBakeSeverity = stage == ValidationStage.Bake
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error;
                messages.Add(new ValidationMessage(
                    staleBakeSeverity,
                    ValidationCode.V08,
                    bindVatTextures,
                    "VAT texture set '" + bindVatTextures.name +
                    "' was baked from different sources than the ones referenced now; rebake it."));
            }

            return messages;
        }

        /// <summary>
        /// Judges the VAT texture sets a bind carries — at most one across the whole bind, and its
        /// source rig must match — and returns the one the registry blob will address, or null.
        /// </summary>
        private static VatTextureSetAsset ValidateVatTextureSetsInto(
            RigAsset rig,
            List<ClipSetAsset> canonicalClipSets,
            List<ValidationMessage> messages)
        {
            VatTextureSetAsset bindVatTextures = null;
            ClipSetAsset bindVatOwner = null;
            for (int setIndex = 0; setIndex < canonicalClipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = canonicalClipSets[setIndex];
                if (clipSet.vatTextures == null)
                {
                    continue;
                }

                if (bindVatTextures == null)
                {
                    bindVatTextures = clipSet.vatTextures;
                    bindVatOwner = clipSet;
                }
                else if (clipSet.vatTextures != bindVatTextures)
                {
                    // bindVatTextures is left pointing at the first set found, not cleared, so the
                    // coverage checks below still have something to compare against.
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V39,
                        clipSet,
                        "Sets '" + bindVatOwner.name + "' and '" + clipSet.name +
                        "' each supply a VAT texture set ('" + bindVatTextures.name + "' and '" +
                        clipSet.vatTextures.name + "'), but an actor addresses exactly one."));
                }

                // Key 0 is anything baked before sourceRigKey existed; it passes rather than fails.
                if (rig != null &&
                    clipSet.vatTextures.sourceRigKey != 0UL &&
                    clipSet.vatTextures.sourceRigKey != rig.StableId)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V40,
                        clipSet.vatTextures,
                        "VAT texture set '" + clipSet.vatTextures.name + "' of set '" +
                        clipSet.name + "' was baked from a different rig's mesh than '" + rig.name +
                        "'. A VAT texture cannot retarget, so this would play another character's " +
                        "vertex motion."));
                }
            }
            return bindVatTextures;
        }

        private static string DescribeRig(RigAsset rig)
        {
            return rig != null ? rig.name : "(none assigned)";
        }

        /// <summary>True when any finding in the list blocks baking. A null list counts as no errors.</summary>
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

        /// <summary>Validates that every tag id in the registry is non-zero and unique. A null registry reports nothing.</summary>
        public static List<ValidationMessage> ValidateTargetTagRegistry(TargetTagRegistry registry)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateTargetTagRegistryInto(registry, messages);
            return messages;
        }

        // -----------------------------------------------------------------------------------
        // Rule implementations.
        // -----------------------------------------------------------------------------------

        private static void ValidateTargetTagRegistryInto(
            TargetTagRegistry registry,
            List<ValidationMessage> messages)
        {
            if (registry == null || registry.entries == null)
            {
                return;
            }

            // Walked in entry order so a reader can match a finding back to the row it names.
            Dictionary<uint, string> namesById = new Dictionary<uint, string>();
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                TargetTagEntry entry = registry.entries[entryIndex];
                if (entry == null)
                {
                    continue;
                }

                string label = string.IsNullOrEmpty(entry.name)
                    ? "Entry " + entryIndex
                    : "'" + entry.name + "'";

                if (entry.stableId == 0u)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V33,
                        registry,
                        label + " has no id (0 is reserved for \"untagged\"); it cannot be " +
                        "assigned to a rig target or a track."));
                    continue;
                }

                string previousName;
                if (namesById.TryGetValue(entry.stableId, out previousName))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V33,
                        registry,
                        "Tags '" + previousName + "' and " + label + " share id " +
                        entry.stableId.ToString() + " in registry '" + registry.name + "'."));
                }
                else
                {
                    namesById.Add(entry.stableId, label);
                }
            }
        }

        private static void ValidateRigInto(RigAsset rig, List<ValidationMessage> messages)
        {
            if (rig == null)
            {
                return;
            }

            // Guarded rather than returned on: a rig with no target list still has billboard roots
            // worth checking. A null target list simply resolves no target addresses.
            Dictionary<uint, string> targetNamesById = new Dictionary<uint, string>();
            Dictionary<uint, string> targetNamesByTagId = new Dictionary<uint, string>();
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

                // A tag appears at most once per rig. 0 ("untagged") is exempt - it is the ordinary
                // state for most targets, not a shared role.
                if (targetDefinition.tagId != 0u)
                {
                    string previousTaggedTargetName;
                    if (targetNamesByTagId.TryGetValue(targetDefinition.tagId, out previousTaggedTargetName))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V34,
                            rig,
                            "Targets '" + previousTaggedTargetName + "' and '" +
                            targetDefinition.displayName + "' both carry tag id " +
                            targetDefinition.tagId.ToString("X8") + " in rig '" + rig.name +
                            "'; a tag-bound track would not know which one to animate."));
                    }
                    else
                    {
                        targetNamesByTagId.Add(targetDefinition.tagId, targetDefinition.displayName);
                    }
                }
            }

            ValidateBillboardRootsInto(rig, targetNamesById, messages);
            ValidateRagdollBodiesInto(rig, targetNamesById, messages);
        }

        /// <summary>Validates the rig's billboard roots: address resolution, duplicate addresses, and axis-constrained mode.</summary>
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

                // Only a RigTarget address can be checked here — a HierarchyPath address names a
                // prefab transform this asset cannot see, and is resolved by the entity bake instead.
                bool addressResolves = true;
                if (rootDefinition.address.kind == RigNodeAddressKind.RigTarget
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

                // Billboarding has no bone path. The Bone kind exists for the ragdoll body list that
                // shares this address struct, so treat it as unresolved and skip the duplicate check below.
                if (rootDefinition.address.kind == RigNodeAddressKind.Bone)
                {
                    addressResolves = false;
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V25,
                        rig,
                        "Billboard root '" + rootDefinition.displayName + "' in rig '" + rig.name +
                        "' addresses bone '" + rootDefinition.address.boneName + "'; billboarding " +
                        "has no bone path, only a rig target or a hierarchy path."));
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

                // Two roots pointing at a target that isn't there is one fault, not two; skip the
                // duplicate-address check so it isn't reported twice.
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

        // The kind is part of the key: target id 7 and the path "7" are not the same node.
        private static string DescribeBillboardAddress(RigNodeAddress address)
        {
            if (address.kind == RigNodeAddressKind.RigTarget)
            {
                return "target " + address.targetId.ToString();
            }
            // An empty path addresses the prefab root, which is a real node and a legal thing to
            // billboard — so it is named rather than treated as a missing value.
            return string.IsNullOrEmpty(address.hierarchyPath)
                ? "the prefab root"
                : "path '" + address.hierarchyPath + "'";
        }

        /// <summary>Validates the rig's ragdoll bodies: id uniqueness, address resolution, box/limit/mass ranges, and tree shape.</summary>
        private static void ValidateRagdollBodiesInto(
            RigAsset rig,
            Dictionary<uint, string> targetNamesById,
            List<ValidationMessage> messages)
        {
            if (rig.ragdollBodies == null)
            {
                return;
            }

            Dictionary<uint, string> bodyNamesById = new Dictionary<uint, string>();
            Dictionary<string, string> bodyNamesByAddress = new Dictionary<string, string>();
            List<KeyValuePair<string, string>> hierarchyPathBodies = new List<KeyValuePair<string, string>>();

            for (int bodyIndex = 0; bodyIndex < rig.ragdollBodies.Count; bodyIndex++)
            {
                RagdollBodyDefinition bodyDefinition = rig.ragdollBodies[bodyIndex];
                if (bodyDefinition == null)
                {
                    continue;
                }

                // V27: the id must exist and must not repeat.
                if (bodyDefinition.stableId == 0u)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V27,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has no stable id; EnsureStableIds must run before this rig is bakeable."));
                }
                else
                {
                    string previousBodyName;
                    if (bodyNamesById.TryGetValue(bodyDefinition.stableId, out previousBodyName))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V27,
                            rig,
                            "Ragdoll bodies '" + previousBodyName + "' and '" +
                            bodyDefinition.displayName + "' share body id " +
                            bodyDefinition.Id.ToString() + " in rig '" + rig.name + "'."));
                    }
                    else
                    {
                        bodyNamesById.Add(bodyDefinition.stableId, bodyDefinition.displayName);
                    }
                }

                // V26: only the RigTarget half is reachable at rig scope; a path or bone address
                // names something this asset cannot see, and is left to the entity bake.
                bool addressResolves = true;
                if (bodyDefinition.address.kind == RigNodeAddressKind.RigTarget
                    && !targetNamesById.ContainsKey(bodyDefinition.address.targetId))
                {
                    addressResolves = false;
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V26,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' addresses target id " +
                        bodyDefinition.address.targetId.ToString() + ", which rig '" + rig.name +
                        "' does not define."));
                }

                // V28: no two bodies on the same node. An address that does not resolve cannot
                // meaningfully duplicate another — one fault, not two.
                bool isDuplicateNodeAddress = false;
                if (addressResolves)
                {
                    string addressKey = DescribeRagdollAddress(bodyDefinition.address);
                    string previousBodyName;
                    if (bodyNamesByAddress.TryGetValue(addressKey, out previousBodyName))
                    {
                        isDuplicateNodeAddress = true;
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V28,
                            rig,
                            "Ragdoll bodies '" + previousBodyName + "' and '" +
                            bodyDefinition.displayName + "' both address " + addressKey +
                            " in rig '" + rig.name +
                            "'; a node may carry at most one ragdoll body."));
                    }
                    else
                    {
                        bodyNamesByAddress.Add(addressKey, bodyDefinition.displayName);
                    }
                }

                // V29: every box extent must be positive.
                if (bodyDefinition.boxSize.x <= 0f
                    || bodyDefinition.boxSize.y <= 0f
                    || bodyDefinition.boxSize.z <= 0f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V29,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has box size (" + bodyDefinition.boxSize.x + ", " +
                        bodyDefinition.boxSize.y + ", " + bodyDefinition.boxSize.z +
                        "); every component must be greater than 0."));
                }

                // V30: both limit pairs are always stored, so both are always checked, regardless
                // of the rig's current RagdollRigSettings.space.
                if (bodyDefinition.limitMinDegrees > bodyDefinition.limitMaxDegrees
                    || bodyDefinition.limitMinDegrees < -180f || bodyDefinition.limitMinDegrees > 180f
                    || bodyDefinition.limitMaxDegrees < -180f || bodyDefinition.limitMaxDegrees > 180f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V30,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has a Planar2D hinge range of [" + bodyDefinition.limitMinDegrees + ", " +
                        bodyDefinition.limitMaxDegrees +
                        "] degrees; the minimum must not exceed the maximum and both must stay " +
                        "within [-180, 180]."));
                }
                if (bodyDefinition.swingLimitDegrees < 0f || bodyDefinition.swingLimitDegrees > 180f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V30,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has a Spatial3D swing limit of " + bodyDefinition.swingLimitDegrees +
                        " degrees; it must stay within [0, 180]."));
                }
                if (bodyDefinition.twistLimitDegrees < 0f || bodyDefinition.twistLimitDegrees > 180f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V30,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has a Spatial3D twist limit of " + bodyDefinition.twistLimitDegrees +
                        " degrees; it must stay within [0, 180]."));
                }

                // V32: mass must be positive.
                if (bodyDefinition.mass <= 0f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V32,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has mass " + bodyDefinition.mass + "; it must be greater than 0."));
                }

                // V31 data collection: only a HierarchyPath address's ancestry is a fact this asset
                // can confirm on its own. A duplicate node address is excluded — it already
                // reported V28, and counting the same node twice would be a second fault for one mistake.
                if (bodyDefinition.address.kind == RigNodeAddressKind.HierarchyPath
                    && !isDuplicateNodeAddress)
                {
                    string hierarchyPath = bodyDefinition.address.hierarchyPath ?? string.Empty;
                    hierarchyPathBodies.Add(
                        new KeyValuePair<string, string>(hierarchyPath, bodyDefinition.displayName));
                }
            }

            ValidateRagdollBodyTreeInto(rig, hierarchyPathBodies, messages);
        }

        // V31: among the bodies whose ancestry this asset can confirm (hierarchy-path addresses
        // only — a target or bone address names something this asset cannot see the ancestry of),
        // exactly one must have no other such body as an ancestor.
        private static void ValidateRagdollBodyTreeInto(
            RigAsset rig,
            List<KeyValuePair<string, string>> hierarchyPathBodies,
            List<ValidationMessage> messages)
        {
            if (hierarchyPathBodies.Count < 2)
            {
                return;
            }

            int rootCount = 0;
            for (int bodyIndex = 0; bodyIndex < hierarchyPathBodies.Count; bodyIndex++)
            {
                string candidatePath = hierarchyPathBodies[bodyIndex].Key;
                bool hasRagdolledAncestor = false;
                for (int otherIndex = 0; otherIndex < hierarchyPathBodies.Count; otherIndex++)
                {
                    if (otherIndex == bodyIndex)
                    {
                        continue;
                    }
                    if (IsAncestorPath(hierarchyPathBodies[otherIndex].Key, candidatePath))
                    {
                        hasRagdolledAncestor = true;
                        break;
                    }
                }
                if (!hasRagdolledAncestor)
                {
                    rootCount++;
                }
            }

            if (rootCount != 1)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.V31,
                    rig,
                    "Rig '" + rig.name + "' has " + rootCount +
                    " hierarchy-path-addressed ragdoll bodies with no ragdolled ancestor among the " +
                    "bodies this asset can place in the hierarchy; a single articulated ragdoll " +
                    "has exactly one root."));
            }
        }

        /// <summary>
        /// True when <paramref name="ancestorPath"/> is a proper ancestor of
        /// <paramref name="descendantPath"/> below the prefab root, by string prefix. An empty path
        /// (the prefab root itself) is an ancestor of every other non-empty path.
        /// </summary>
        private static bool IsAncestorPath(string ancestorPath, string descendantPath)
        {
            if (ancestorPath == descendantPath)
            {
                return false;
            }
            if (ancestorPath.Length == 0)
            {
                return descendantPath.Length > 0;
            }
            return descendantPath.StartsWith(ancestorPath + "/", StringComparison.Ordinal);
        }

        // Unlike DescribeBillboardAddress, this must also render Bone: a ragdoll body may
        // legitimately be welded to a skinned bone, where a billboard root rejects that kind outright.
        private static string DescribeRagdollAddress(RigNodeAddress address)
        {
            if (address.kind == RigNodeAddressKind.RigTarget)
            {
                return "target " + address.targetId.ToString();
            }
            if (address.kind == RigNodeAddressKind.Bone)
            {
                return "bone '" + address.boneName + "'";
            }
            return string.IsNullOrEmpty(address.hierarchyPath)
                ? "the prefab root"
                : "path '" + address.hierarchyPath + "'";
        }

        /// <summary>Reports V17 for one key's Bezier handles outside the unit square; says nothing for any other interpolation mode.</summary>
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
            // All-zero is the value a key deserializes to before these fields existed, and
            // ClipSampler.EaseBezier reads it as linear rather than as a curve — exempt, or every
            // pre-Bezier clip would fail for a shape nothing evaluates.
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

        // Checked separately from the transform-track loop, which resolves ids against rig.targets
        // and would report the wrong list for a billboard-root id. Skipped entirely with no rig,
        // since a billboard root id is minted per rig and an unbound clip has nothing to check against.
        private static void ValidateBillboardTracksInto(
            ClipAsset clip, RigAsset resolutionRig, List<ValidationMessage> messages)
        {
            if (clip.billboardTracks == null || resolutionRig == null)
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

                if (!RigDeclaresBillboardRoot(resolutionRig, track.rootStableId))
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

        // Checked for a name, not a target binding: a bone lives in an imported hierarchy this
        // package does not own, so the name is the only handle available. Whether it resolves to a
        // real bone is checked later, at the VAT bake, where the skeleton actually exists.
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

        /// <param name="resolutionRig">
        /// The rig this clip's bindings are judged against — the rig it is being played on. A clip
        /// has no rig of its own to fall back to, so null means "no rig in hand": every binding rule
        /// stays silent rather than inventing a finding out of missing context, and the clip-local
        /// rules are judged as usual.
        /// </param>
        private static void ValidateClipInto(
            ClipAsset clip,
            RigAsset resolutionRig,
            TargetTagRegistry tagRegistry,
            List<ValidationMessage> messages)
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
                ValidateTrackBindingInto(
                    clip, resolutionRig, transformTrack.targetId, transformTrack.tagId,
                    tagRegistry, "Transform track", trackIndex, messages);

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
                ValidateTrackBindingInto(
                    clip, resolutionRig, spriteTrack.targetId, spriteTrack.tagId,
                    tagRegistry, "Sprite track", trackIndex, messages);

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
            ValidateBillboardTracksInto(clip, resolutionRig, messages);

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

        // A window longer than the clip is not reported: on a looping clip that just means "open for
        // the whole loop", and on a Once clip the layer going inactive already resolves it.
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
            RigAsset resolutionRig,
            uint targetId,
            string trackKindLabel,
            int trackIndex,
            List<ValidationMessage> messages)
        {
            if (RigContainsTarget(resolutionRig, targetId))
            {
                return;
            }
            string rigLabel = resolutionRig == null
                ? "no rig (none assigned)"
                : "rig '" + resolutionRig.name + "'";
            messages.Add(new ValidationMessage(
                ValidationSeverity.Error,
                ValidationCode.V02,
                clip,
                trackKindLabel + " " + trackIndex + " of clip '" + clip.name + "' targets id " +
                new TargetId(targetId).ToString() + ", which is not defined by " + rigLabel + "."));
        }

        // Validates one track's binding: by tag when tagId is non-zero, by target id otherwise. Both
        // halves report a warning, never an error — a clip records no rig, so there is no "wrong id",
        // only one that doesn't line up with the rig it happens to be playing on.
        private static void ValidateTrackBindingInto(
            ClipAsset clip,
            RigAsset resolutionRig,
            uint targetId,
            uint tagId,
            TargetTagRegistry tagRegistry,
            string trackKindLabel,
            int trackIndex,
            List<ValidationMessage> messages)
        {
            if (tagId == 0u)
            {
                // V38: this rig does not declare the target the track names — a warning, since a
                // clip records no "home rig" this could be an error against.
                if (resolutionRig != null && !RigContainsTarget(resolutionRig, targetId))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Warning,
                        ValidationCode.V38,
                        clip,
                        trackKindLabel + " " + trackIndex + " of clip '" + clip.name +
                        "' targets id " + new TargetId(targetId).ToString() + ", which rig '" +
                        resolutionRig.name + "' does not declare; the track is skipped when this " +
                        "clip plays on that rig."));
                }
                return;
            }

            // V36: the tag id no longer exists anywhere. Only judged when a registry is supplied —
            // without one this cannot be told apart from V35 and reports the milder finding instead.
            if (tagRegistry != null && !tagRegistry.ContainsId(tagId))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V36,
                    clip,
                    trackKindLabel + " " + trackIndex + " of clip '" + clip.name + "' binds tag id 0x" +
                    tagId.ToString("X8") + ", which no longer exists in registry '" +
                    tagRegistry.name + "'; this is a dangling reference on every rig it meets."));
                return;
            }

            // No rig to judge against — a shareable clip inspected on its own, outside any set.
            // Staying silent rather than guessing; the set-scoped pass judges it once a rig is declared.
            if (resolutionRig == null)
            {
                return;
            }

            if (RigContainsTagTarget(resolutionRig, tagId))
            {
                return;
            }

            // V35: the tag exists but this rig has no target carrying it. The message names the
            // clip, track, tag and rig so it is actionable without opening anything else.
            string tagLabel = tagRegistry != null && tagRegistry.FindName(tagId) != null
                ? "'" + tagRegistry.FindName(tagId) + "'"
                : "id 0x" + tagId.ToString("X8");
            string rigLabel = "rig '" + resolutionRig.name + "'";
            messages.Add(new ValidationMessage(
                ValidationSeverity.Warning,
                ValidationCode.V35,
                clip,
                trackKindLabel + " " + trackIndex + " of clip '" + clip.name + "' binds tag " +
                tagLabel + ", which " + rigLabel + " has no target for; the track is skipped " +
                "when this clip plays on that rig."));
        }

        private static bool RigContainsTagTarget(RigAsset rig, uint tagId)
        {
            if (rig == null || rig.targets == null || tagId == 0u)
            {
                return false;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition != null && targetDefinition.tagId == tagId)
                {
                    return true;
                }
            }
            return false;
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

        // VAT tracks keep the strict V02 check rather than V38's lenient skip: a VAT texture cannot
        // retarget, so a VAT track naming a target this rig does not declare has nothing to fall back to.
        private static void ValidateVatCoverageInto(
            ClipSetAsset clipSet,
            ClipAsset clip,
            RigAsset rig,
            List<ValidationMessage> messages)
        {
            // vatSource is a plain [Serializable] class field, so Unity cannot represent null for it
            // on disk — every saved clip carries a default-constructed VatClipSource with a null
            // sourceClip. Testing the field itself for null would flag every non-VAT clip as having
            // a VAT source, so an empty sourceClip is what actually means "no VAT intent" here.
            bool hasLegacySource = clip.vatSource != null && clip.vatSource.sourceClip != null;

            // vatTracks is a List<VatTrack>, which round-trips a genuinely empty list as empty (no
            // phantom-element trap like vatSource above), so a row with no sourceClip yet is simply skipped.
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

                ValidateTargetBindingInto(
                    clip, rig, track.targetId, "VAT track", trackIndex, messages);

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

        // Exact match only, never the untargeted-range fallback runtime resolution performs: falling
        // back here would pass a track naming a never-baked target while it silently plays the wrong mesh's motion.
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

        /// <summary>True when the rig declares a target carrying this id. A null rig or id 0 answers false.</summary>
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
