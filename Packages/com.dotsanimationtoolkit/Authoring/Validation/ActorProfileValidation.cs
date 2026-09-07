// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The rule table for an <see cref="ActorProfileAsset"/> (P1-P7): layer shape, name identity,
    /// fill-pattern legality, and the warnings a profile can carry without failing a bake.
    /// </summary>
    public static class ActorProfileValidation
    {
        /// <summary>Validates one profile against its own layers and, for P2, the supplied animation name vocabulary.</summary>
        /// <param name="animationNames">
        /// Optional: without it, an <c>animationKey</c> that no longer exists anywhere cannot be
        /// told apart from one this call was never given the means to check, so P2 is skipped
        /// rather than reported wrongly either way.
        /// </param>
        /// <returns>Findings in discovery order (layer by layer, top to bottom). Empty when the profile is fully valid.</returns>
        public static List<ValidationMessage> Validate(
            ActorProfileAsset profile, IVocabularyRegistry animationNames = null)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            if (profile == null)
            {
                return messages;
            }

            ValidateBookendsInto(profile, messages);

            HashSet<ClipAsset> registeredClips = CollectRegisteredClips(profile.clipSets);
            int ragdollBodyCount = profile.rig != null && profile.rig.ragdollBodies != null
                ? profile.rig.ragdollBodies.Count
                : 0;

            int layerCount = profile.layers == null ? 0 : profile.layers.Count;

            // First pass: which layer owns each name, so P3's duplicate check and P7's
            // cross-layer check both see the whole profile rather than only what came before them.
            Dictionary<uint, int> layerIndexByAnimationKey = new Dictionary<uint, int>();
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
                    if (animation == null || animation.animationKey == 0u)
                    {
                        continue;
                    }
                    int previousLayerIndex;
                    if (layerIndexByAnimationKey.TryGetValue(animation.animationKey, out previousLayerIndex))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error,
                            ValidationCode.P3,
                            profile,
                            "Animation id " + animation.animationKey + " appears on both layer '" +
                            LayerLabel(profile.layers[previousLayerIndex], previousLayerIndex) +
                            "' and layer '" + LayerLabel(layer, layerIndex) +
                            "'; a name must resolve to exactly one entry."));
                    }
                    else
                    {
                        layerIndexByAnimationKey.Add(animation.animationKey, layerIndex);
                    }
                }
            }

            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                ActorLayerDefinition layer = profile.layers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                if (layer.animations != null)
                {
                    for (int animationIndex = 0; animationIndex < layer.animations.Count; animationIndex++)
                    {
                        ActorAnimationDefinition animation = layer.animations[animationIndex];
                        if (animation == null)
                        {
                            continue;
                        }
                        ValidateAnimationNameInto(profile, layer, layerIndex, animation, animationIndex, animationNames, messages);
                        ValidateFillPatternInto(profile, layer, layerIndex, animation, animationIndex, messages);
                        ValidateClipRegistrationInto(profile, layer, layerIndex, animation, animationIndex, registeredClips, messages);
                        ValidateRagdollBodiesExistInto(profile, layer, layerIndex, animation, animationIndex, ragdollBodyCount, messages);
                    }
                }

                ValidateStartingAnimationInto(profile, layer, layerIndex, layerIndexByAnimationKey, messages);
            }

            return messages;
        }

        // -----------------------------------------------------------------------------------
        // Rule implementations.
        // -----------------------------------------------------------------------------------

        private static void ValidateBookendsInto(ActorProfileAsset profile, List<ValidationMessage> messages)
        {
            int layerCount = profile.layers == null ? 0 : profile.layers.Count;
            if (layerCount < 2 || layerCount > ActorProfileAsset.MaxLayerCount)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.P1,
                    profile,
                    "Profile '" + profile.name + "' declares " + layerCount +
                    " layers; it must declare between 2 and " + ActorProfileAsset.MaxLayerCount +
                    " (the Base and Override bookends count toward this)."));
                return;
            }

            bool firstIsBase = profile.layers[0] != null &&
                                profile.layers[0].displayName == ActorProfileAsset.BaseLayerName;
            bool lastIsOverride = profile.layers[layerCount - 1] != null &&
                                   profile.layers[layerCount - 1].displayName == ActorProfileAsset.OverrideLayerName;
            if (!firstIsBase || !lastIsOverride)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.P1,
                    profile,
                    "Profile '" + profile.name + "' must start with a layer named '" +
                    ActorProfileAsset.BaseLayerName + "' and end with one named '" +
                    ActorProfileAsset.OverrideLayerName + "'."));
            }
        }

        private static void ValidateAnimationNameInto(
            ActorProfileAsset profile,
            ActorLayerDefinition layer,
            int layerIndex,
            ActorAnimationDefinition animation,
            int animationIndex,
            IVocabularyRegistry animationNames,
            List<ValidationMessage> messages)
        {
            if (animation.animationKey == 0u)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.P2,
                    profile,
                    "Layer '" + LayerLabel(layer, layerIndex) + "' entry " + animationIndex +
                    " has no animation name (animationKey is 0)."));
                return;
            }
            if (animationNames != null && !animationNames.ContainsId(animation.animationKey))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.P2,
                    profile,
                    "Layer '" + LayerLabel(layer, layerIndex) + "' entry " + animationIndex +
                    " names animation id " + animation.animationKey +
                    ", which no longer exists in the animation name registry."));
            }
        }

        private static void ValidateFillPatternInto(
            ActorProfileAsset profile,
            ActorLayerDefinition layer,
            int layerIndex,
            ActorAnimationDefinition animation,
            int animationIndex,
            List<ValidationMessage> messages)
        {
            if (!animation.hasDirections)
            {
                if (animation.clip == null)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.P4,
                        profile,
                        "Layer '" + LayerLabel(layer, layerIndex) + "' entry " + animationIndex +
                        " is non-directional but names no clip."));
                }
                return;
            }

            AnimationDirections effectiveDirections;
            bool hasValidFillPattern = animation.directionSlots != null &&
                                       animation.directionSlots.TryGetEffectiveDirections(out effectiveDirections);
            if (!hasValidFillPattern)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.P4,
                    profile,
                    "Layer '" + LayerLabel(layer, layerIndex) + "' entry " + animationIndex +
                    " is directional but its filled slots do not form one of the five valid coverage patterns."));
            }
        }

        private static void ValidateClipRegistrationInto(
            ActorProfileAsset profile,
            ActorLayerDefinition layer,
            int layerIndex,
            ActorAnimationDefinition animation,
            int animationIndex,
            HashSet<ClipAsset> registeredClips,
            List<ValidationMessage> messages)
        {
            if (!animation.hasDirections)
            {
                ReportIfUnregistered(profile, layer, layerIndex, animationIndex, animation.clip, "clip", registeredClips, messages);
                return;
            }
            if (animation.directionSlots == null)
            {
                return;
            }
            ReportIfUnregistered(profile, layer, layerIndex, animationIndex, animation.directionSlots.southEast, "southEast slot", registeredClips, messages);
            ReportIfUnregistered(profile, layer, layerIndex, animationIndex, animation.directionSlots.northEast, "northEast slot", registeredClips, messages);
            ReportIfUnregistered(profile, layer, layerIndex, animationIndex, animation.directionSlots.south, "south slot", registeredClips, messages);
            ReportIfUnregistered(profile, layer, layerIndex, animationIndex, animation.directionSlots.north, "north slot", registeredClips, messages);
            ReportIfUnregistered(profile, layer, layerIndex, animationIndex, animation.directionSlots.east, "east slot", registeredClips, messages);
        }

        private static void ReportIfUnregistered(
            ActorProfileAsset profile,
            ActorLayerDefinition layer,
            int layerIndex,
            int animationIndex,
            ClipAsset clip,
            string slotLabel,
            HashSet<ClipAsset> registeredClips,
            List<ValidationMessage> messages)
        {
            if (clip == null || registeredClips.Contains(clip))
            {
                return;
            }
            messages.Add(new ValidationMessage(
                ValidationSeverity.Warning,
                ValidationCode.P5,
                profile,
                "Layer '" + LayerLabel(layer, layerIndex) + "' entry " + animationIndex + "'s " +
                slotLabel + " names clip '" + clip.name +
                "', which is not in any of this profile's clip sets; it would resolve to nothing at play."));
        }

        private static void ValidateRagdollBodiesExistInto(
            ActorProfileAsset profile,
            ActorLayerDefinition layer,
            int layerIndex,
            ActorAnimationDefinition animation,
            int animationIndex,
            int ragdollBodyCount,
            List<ValidationMessage> messages)
        {
            if (animation.ragdollTrigger == RagdollTrigger.None || ragdollBodyCount > 0)
            {
                return;
            }
            messages.Add(new ValidationMessage(
                ValidationSeverity.Warning,
                ValidationCode.P6,
                profile,
                "Layer '" + LayerLabel(layer, layerIndex) + "' entry " + animationIndex +
                " sets a ragdoll trigger, but the rig declares no ragdoll bodies to trigger."));
        }

        private static void ValidateStartingAnimationInto(
            ActorProfileAsset profile,
            ActorLayerDefinition layer,
            int layerIndex,
            Dictionary<uint, int> layerIndexByAnimationKey,
            List<ValidationMessage> messages)
        {
            if (layer.startingAnimationKey == 0u)
            {
                if (layer.defaultActive)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Warning,
                        ValidationCode.P7,
                        profile,
                        "Layer '" + LayerLabel(layer, layerIndex) +
                        "' is defaultActive but names no startingAnimationKey."));
                }
                return;
            }

            int owningLayerIndex;
            if (layerIndexByAnimationKey.TryGetValue(layer.startingAnimationKey, out owningLayerIndex) &&
                owningLayerIndex != layerIndex)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.P7,
                    profile,
                    "Layer '" + LayerLabel(layer, layerIndex) + "'s startingAnimationKey names an " +
                    "entry that belongs to layer '" +
                    LayerLabel(profile.layers[owningLayerIndex], owningLayerIndex) + "' instead."));
            }
        }

        private static HashSet<ClipAsset> CollectRegisteredClips(IReadOnlyList<ClipSetAsset> clipSets)
        {
            HashSet<ClipAsset> registeredClips = new HashSet<ClipAsset>();
            if (clipSets == null)
            {
                return registeredClips;
            }
            for (int setIndex = 0; setIndex < clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip != null)
                    {
                        registeredClips.Add(clip);
                    }
                }
            }
            return registeredClips;
        }

        private static string LayerLabel(ActorLayerDefinition layer, int layerIndex)
        {
            if (layer == null || string.IsNullOrEmpty(layer.displayName))
            {
                return "Layer " + layerIndex;
            }
            return layer.displayName;
        }
    }
}
