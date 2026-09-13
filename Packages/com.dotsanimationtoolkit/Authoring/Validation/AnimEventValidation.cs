// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The event-marker rule table shared by clip and cutscene validation, so both agree on what
    /// key, window, and payload combinations are legal.
    /// </summary>
    public static class AnimEventValidation
    {
        public static void ValidateMarkers(
            IReadOnlyList<(uint key, int intParam, float floatParam, float windowSeconds)> markers,
            Func<uint, bool> registryContainsKey,
            Func<uint, IReadOnlyList<string>> valueNamesForKey,
            UnityEngine.Object owner,
            string ownerLabel,
            List<ValidationMessage> output)
        {
            if (markers == null)
            {
                return;
            }

            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                (uint key, int intParam, float floatParam, float windowSeconds) marker = markers[markerIndex];
                ValidateMarker(
                    markerIndex,
                    marker.key,
                    marker.intParam,
                    marker.windowSeconds,
                    registryContainsKey,
                    valueNamesForKey,
                    owner,
                    ownerLabel,
                    output);
            }
        }

        // A window longer than the clip is not reported: on a looping clip that just means "open for
        // the whole loop", and on a Once clip the layer going inactive already resolves it.
        public static void ValidateMarker(
            int markerIndex,
            uint key,
            int intParam,
            float windowSeconds,
            Func<uint, bool> registryContainsKey,
            Func<uint, IReadOnlyList<string>> valueNamesForKey,
            UnityEngine.Object owner,
            string ownerLabel,
            List<ValidationMessage> output)
        {
            bool isReservedKey = key < (uint)ReservedEventKeys.FirstUserKey;
            if (isReservedKey)
            {
                output.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V09,
                    owner,
                    "Event " + markerIndex + " of " + ownerLabel + " uses key " + key +
                    "; keys below " + (uint)ReservedEventKeys.FirstUserKey +
                    " are reserved by the package."));
            }

            if (windowSeconds < 0f)
            {
                output.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V19,
                    owner,
                    "Event " + markerIndex + " of " + ownerLabel + " has a window of " +
                    windowSeconds + " seconds; a window cannot be negative. The bake clamps " +
                    "it to 0, which makes the event pulse-only."));
            }
            else if (windowSeconds > 0f && !AnimEventMaskKeys.IsMaskable(key))
            {
                output.Add(new ValidationMessage(
                    ValidationSeverity.Warning,
                    ValidationCode.V20,
                    owner,
                    "Event " + markerIndex + " of " + ownerLabel + " authors a " + windowSeconds +
                    "s window on key " + key + ", which is outside the maskable range " +
                    AnimEventMaskKeys.FirstMaskKey + "–" + AnimEventMaskKeys.LastMaskKey +
                    ". The event still fires, but no AnimEventMask bit exists for it, so the " +
                    "window can never be observed."));
            }

            if (!isReservedKey && registryContainsKey != null && !registryContainsKey(key))
            {
                output.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.V41,
                    owner,
                    "Event " + markerIndex + " of " + ownerLabel + " uses key 0x" +
                    key.ToString("X8") + ", which is not in the event name registry — the " +
                    "name was deleted or never existed."));
            }

            if (valueNamesForKey != null)
            {
                IReadOnlyList<string> names = valueNamesForKey(key);
                if (names != null && names.Count > 0 && (intParam < 0 || intParam >= names.Count))
                {
                    output.Add(new ValidationMessage(
                        ValidationSeverity.Warning,
                        ValidationCode.V42,
                        owner,
                        "Event " + markerIndex + " of " + ownerLabel + " stores intParam " +
                        intParam + ", outside the " + names.Count + " named values its event " +
                        "declares. It still fires with " + intParam + "."));
                }
            }
        }

        // Null for a null registry, which is what keeps V41/V42 silent when no registry is in hand.
        public static Func<uint, bool> RegistryContainsKey(AnimEventKeyRegistry registry)
        {
            if (registry == null)
            {
                return null;
            }
            return registry.ContainsKey;
        }

        public static Func<uint, IReadOnlyList<string>> ValueNamesForKey(AnimEventKeyRegistry registry)
        {
            if (registry == null)
            {
                return null;
            }
            return eventKey =>
            {
                if (registry.entries == null)
                {
                    return null;
                }
                for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
                {
                    AnimEventKeyEntry entry = registry.entries[entryIndex];
                    if (entry != null && entry.eventKey == eventKey)
                    {
                        return entry.intParamValueNames;
                    }
                }
                return null;
            };
        }
    }
}
