// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The single authoritative implementation of the architecture section 3.5 rule table
    /// (V01–V37), shared by the inspectors, the clip editor, and the bake so that all three agree on
    /// what is legal. Pure static managed code — no editor-assembly dependency, no ECS world, and
    /// no side effects on the assets it inspects.
    /// </summary>
    public static class ClipValidation
    {
        /// <summary>
        /// Validates a rig against the rules that concern it: V13 (layer count), V05 (target id
        /// uniqueness), V34 (target tag uniqueness, Phase E target-tags spec §6 rule T1), the
        /// billboard-root rules V21, V22, V23 and V25 (amendment A44; V25 is the ragdoll spec's
        /// V-R8), and the ragdoll-body rules V26–V32 (amendment A50, spec §4's V-R1–V-R7).
        /// </summary>
        /// <param name="rig">The rig to validate. A null rig reports V13, since a set without a rig
        /// has no layers.</param>
        /// <returns>
        /// The findings in discovery order — layer checks (V13) first, then per-target id
        /// uniqueness (V05) and tag uniqueness (V34, rule T1) together in target-list order, then
        /// the billboard roots (V21, V25, V23, V22) in
        /// billboard-root order, then the ragdoll bodies (V27, V26, V28, V29, V30, V32 per body,
        /// then V31 once for the whole rig) in ragdoll-body order. Deliberately not sorted by rule
        /// number: the inspector and the clip editor list findings in the order the asset reads, so
        /// a reader can walk the asset top to bottom. Empty when the rig is fully valid.
        /// </returns>
        public static List<ValidationMessage> ValidateRig(RigAsset rig)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateRigInto(rig, messages);
            return messages;
        }

        /// <summary>
        /// Validates one clip against the rules that concern a clip in isolation: V01, V02, V03,
        /// V04, V09, V10, V12, V14, V15, V16, V35 and V36. Set-scoped rules (V05, V06, V07, V08,
        /// V11) are checked by <see cref="ValidateSet"/>; T4 (V37) is a project-wide rule about which
        /// sets reference a clip, which a single clip or set cannot answer on its own, and is checked
        /// by the Editor-assembly utility that can see the whole project. V16 (duplicate bone name)
        /// is a clip-local sibling of V05 rather than a V05 case itself: a bone track has no stable
        /// id, so its only identity is the name, and uniqueness of that name only ever needs judging
        /// within one clip — there is no set- or rig-scoped notion of "the same bone" the way there
        /// is for a <c>ClipId</c> or a <c>TargetId</c>.
        /// </summary>
        /// <param name="clip">The clip to validate.</param>
        /// <param name="tagRegistry">
        /// The project's target tag registry, used to judge T3 (V36) — a track's tag id that no
        /// longer exists anywhere — and to name a tag in a T2 (V35) message instead of showing its
        /// raw hex id. Optional, mirroring rule V08's own gap (architecture section 3.5): a caller
        /// with no registry to hand still gets T2 findings (checking a rig's own target list needs
        /// no registry), it just cannot tell a T2 "not on this rig" apart from a T3 "deleted
        /// entirely" and reports the milder T2 for both rather than silently passing either.
        /// </param>
        /// <returns>
        /// The findings in discovery order — the clip-level rules (V01, V10, V12) first, then each
        /// transform and sprite track in authoring order (V02/V35/V36, V03, V04, V14), then each
        /// bone track in authoring order (V03, V04, V15, V16), then each event (V04, V09).
        /// Deliberately not sorted by rule number, so a reader can walk the asset top to bottom.
        /// Empty when the clip is fully valid.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip"/> is null.</exception>
        public static List<ValidationMessage> ValidateClip(
            ClipAsset clip, TargetTagRegistry tagRegistry = null)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }
            List<ValidationMessage> messages = new List<ValidationMessage>();
            ValidateClipInto(clip, clip.rig, tagRegistry, messages);
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
        /// <param name="tagRegistry">
        /// The project's target tag registry, passed through to each clip's T2/T3 (V35/V36) checks.
        /// See <see cref="ValidateClip"/> for what a null registry costs.
        /// </param>
        /// <returns>The findings, rig first and then clip by clip in list order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clipSet"/> is null.</exception>
        public static List<ValidationMessage> ValidateSet(
            ClipSetAsset clipSet,
            ValidationStage stage = ValidationStage.Authoring,
            bool vatSourceHashRecomputed = false,
            ulong recomputedVatSourceHash = 0UL,
            TargetTagRegistry tagRegistry = null)
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

                    // A null clip.rig is exempt (Phase E target-tags spec §1, §4.3): a clip with no
                    // assigned rig has no target-id-bound track that could be "against the wrong
                    // rig" in the first place - a track either binds by tag (resolved fresh against
                    // whichever rig the set being baked actually declares, spec §5) or, on a
                    // null-rig clip, is unauthored. A clip that still carries a specific clip.rig
                    // keeps V06 exactly as before: it has committed to that rig as its home, and a
                    // target-id-bound track on it is only ever meaningful there. This is what makes a
                    // fully tag-bound clip referenceable from any number of differently-rigged sets
                    // (the whole point of spec §1's "nothing else is in the way" claim) without
                    // opening the door to an ordinary, non-shared clip drifting onto the wrong rig by
                    // accident.
                    if (clip.rig != null && clip.rig != clipSet.rig)
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.V06,
                            clip,
                            "Clip '" + clip.name + "' is authored against a different rig than set '" +
                            clipSet.name + "'; every clip in a set must share the set's rig, unless " +
                            "the clip has no rig assigned at all (a fully tag-bound, shareable clip)."));
                    }

                    ValidateClipInto(clip, clipSet.rig, tagRegistry, messages);
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

        /// <summary>
        /// Validates a target tag registry against T5 (Phase E target-tags spec §6): every tag id
        /// is non-zero and unique within the registry.
        /// </summary>
        /// <param name="registry">
        /// The registry to validate. A null registry reports nothing — unlike a null
        /// <see cref="RigAsset"/> in <see cref="ValidateRig"/>, a target tag registry is optional
        /// project furniture with no required-presence rule of its own.
        /// </param>
        /// <returns>
        /// The findings in entry order. Empty when every id is non-zero and unique, including when
        /// <paramref name="registry"/> is null or holds no entries.
        /// </returns>
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

            // Same duplicate-identity shape as ValidateRigInto's V05 pass: a name-keyed map of ids
            // already seen, walked in entry order so a reader can match a finding back to the row
            // it names without hunting.
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

                // T1 (Phase E target-tags spec §6): a tag appears at most once per rig. 0 ("untagged")
                // is exempt - it is the ordinary state for most targets, not a shared role.
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

        /// <summary>
        /// Validates the rig's billboard roots (amendment A44): V21, V22, V23 and V25.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>V21 is only half-reachable here, and the reachable half is the target one.</strong>
        /// A <see cref="RigNodeAddressKind.RigTarget"/> address names a row of this very asset, so
        /// it can be resolved against <paramref name="targetNamesById"/> and reported now, while the
        /// author is looking at the rig. A <see cref="RigNodeAddressKind.HierarchyPath"/> address
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
        /// <para>
        /// <strong>V25 needs no such split.</strong> A <see cref="RigNodeAddressKind.Bone"/> address
        /// is wrong for a billboard root regardless of which bone it names — the rig asset does not
        /// need to see the prefab to know that billboarding has no bone path — so it is fully
        /// reachable here, unlike V21's path half.
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

                // V-R8 (ragdoll spec): billboarding has no bone path. The Bone kind exists for the
                // ragdoll body list that shares this address struct, not for billboard roots, so a
                // row that carries one is treated the same as an unresolved address — it must not
                // also be checked for an address-key duplicate below.
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

        /// <summary>
        /// Validates the rig's ragdoll bodies (Phase D ragdoll spec, amendment A50): V26, V27, V28,
        /// V29, V30, V31 and V32.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>V26 is only half-reachable here, mirroring V21's split for billboard roots.</strong>
        /// A <see cref="RigNodeAddressKind.RigTarget"/> address names a row of this very asset and is
        /// resolved against <paramref name="targetNamesById"/>; a
        /// <see cref="RigNodeAddressKind.HierarchyPath"/> or <see cref="RigNodeAddressKind.Bone"/>
        /// address names something a <see cref="RigAsset"/> cannot see on its own, and is left to the
        /// entity bake (Phase D3) to resolve or report, the same way an unresolved billboard path is
        /// left to <c>BillboardRootResolver</c>.
        /// </para>
        /// <para>
        /// <strong>V31 checks only what this asset can itself confirm.</strong> Whether the ragdoll
        /// bodies form a single tree is, in general, a question about the real prefab hierarchy —
        /// exactly the gap V26 documents for address resolution. A
        /// <see cref="RigNodeAddressKind.HierarchyPath"/> address is the one kind whose ancestry this
        /// asset can verify unaided, by string prefix, so only those bodies are placed in the tree;
        /// a rig whose bodies are entirely target- or bone-addressed never trips V31 at authoring
        /// time, and that silence must not be read as "the hierarchy was checked and is fine".
        /// </para>
        /// </remarks>
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

                // V-R2 (V27): the id must exist and must not repeat.
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

                // V-R1 (V26): only the RigTarget half is reachable at rig scope.
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

                // V-R3 (V28): no two bodies on the same node. An address that does not resolve
                // cannot meaningfully duplicate another - one fault, not two, mirroring V22's own
                // discipline against V21's unresolved case.
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

                // V-R4 (V29): every box extent must be positive.
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

                // V-R5 (V30): both limit pairs are always stored, so both are always checked,
                // regardless of the rig's current RagdollRigSettings.space.
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

                // V-R7 (V32): mass must be positive.
                if (bodyDefinition.mass <= 0f)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.V32,
                        rig,
                        "Ragdoll body '" + bodyDefinition.displayName + "' in rig '" + rig.name +
                        "' has mass " + bodyDefinition.mass + "; it must be greater than 0."));
                }

                // V-R6 (V31) data collection: only a HierarchyPath address's ancestry is a fact this
                // asset can confirm on its own. See this method's remarks. A duplicate node address
                // is excluded here too - it already reported V28, and counting the same node twice
                // toward the tree would report a second, unrelated-looking fault for one mistake.
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

        /// <summary>
        /// V-R6 (V31): among the bodies whose ancestry this asset can confirm (hierarchy-path
        /// addresses only - see <see cref="ValidateRagdollBodiesInto"/>'s remarks), exactly one must
        /// have no other such body as an ancestor.
        /// </summary>
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

        /// <summary>
        /// Renders a ragdoll body address as the key V28 compares on, and as the text it reports.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="DescribeBillboardAddress"/>, this must also render
        /// <see cref="RigNodeAddressKind.Bone"/>: billboard roots reject that kind outright (V25),
        /// but a ragdoll body may legitimately be welded to a skinned bone.
        /// </remarks>
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

        /// <param name="resolutionRig">
        /// The rig this clip's bindings are judged against - the rig it will actually play on. From
        /// <see cref="ValidateSet"/> that is the <em>set's</em> rig, not <c>clip.rig</c>, and the
        /// distinction is the whole of Phase E: a shareable clip carries no rig of its own (the V06
        /// exemption in <see cref="ValidateSet"/>), so judging its tags against <c>clip.rig</c>
        /// would fire T2 on every tag-bound track of every shareable clip - against a rig that is
        /// null by design - and drown the one rule §6.1 relies on to catch a mis-picked tag. Null
        /// when nothing declares a rig at all, in which case a binding cannot be judged and is left
        /// alone rather than blamed.
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
                    clip, resolutionRig, transformTrack.targetId, transformTrack.tagId, tagRegistry,
                    "Transform track", trackIndex, messages);

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
                    clip, resolutionRig, spriteTrack.targetId, spriteTrack.tagId, tagRegistry,
                    "Sprite track", trackIndex, messages);

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

        /// <summary>
        /// Validates one <see cref="TransformTrack"/> or <see cref="SpriteTrack"/>'s binding (Phase E
        /// target-tags spec §4.3): by target id when <paramref name="tagId"/> is 0 (today's V02
        /// path, unchanged), or by tag otherwise (T2/T3, V35/V36).
        /// </summary>
        /// <remarks>
        /// T3 is checked first and, when it fires, T2 is not also evaluated against the same track —
        /// mirroring this file's existing discipline for V21/V22 and V26/V28: an id that cannot be
        /// resolved at all (deleted from the registry) is one fault, not a second, unrelated-looking
        /// one about which rig happens to lack it.
        /// </remarks>
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
                ValidateTargetBindingInto(
                    clip, resolutionRig, targetId, trackKindLabel, trackIndex, messages);
                return;
            }

            // T3 (V36): the tag id no longer exists anywhere. Only judged when a registry was
            // supplied — see ValidateClip's remarks on why an absent registry cannot tell this apart
            // from T2 and reports the milder finding instead of staying silent.
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

            // No rig to judge against - a shareable clip inspected on its own, outside any set.
            // T2 asks "does the rig this will play on carry the tag?", and with no rig in hand there
            // is no answer, only a guess. Staying silent is the same discipline T3 uses for an
            // absent registry above: report what can be known, never invent a finding out of
            // missing context. The set-scoped pass judges it properly once a rig is declared.
            if (resolutionRig == null)
            {
                return;
            }

            if (RigContainsTagTarget(resolutionRig, tagId))
            {
                return;
            }

            // T2 (V35): the tag exists (or its existence could not be judged) but this rig has no
            // target carrying it. Spec §6.1 requires the message to name all four things — clip,
            // track, tag name, and rig — without the reader having to open anything else.
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

        /// <summary>
        /// True when <paramref name="rig"/> declares a target row carrying <paramref name="tagId"/>.
        /// A null rig, a null row, and the reserved id 0 all answer false — the same shape
        /// <see cref="RigContainsTarget"/> uses for a target's own id.
        /// </summary>
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

                ValidateTargetBindingInto(
                    clip, clipSet.rig, track.targetId, "VAT track", trackIndex, messages);

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
