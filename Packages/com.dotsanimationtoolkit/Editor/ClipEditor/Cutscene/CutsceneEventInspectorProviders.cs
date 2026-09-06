// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A host's editor for one event key's payload (amendment A65 §3.1). A cue's
    /// <c>intParam</c> is a dialogue sequence id in the game that authored it and a raw number
    /// everywhere else; only the host can turn it into something an author can pick.
    /// </summary>
    public interface ICutsceneEventInspectorProvider
    {
        /// <summary>
        /// Fills <paramref name="container"/> with fields bound to <paramref name="markerProperty"/>'s
        /// <c>intParam</c>/<c>floatParam</c> when this provider owns <paramref name="eventKey"/>.
        /// </summary>
        /// <returns>False to leave the default int/float fields in place.</returns>
        bool TryBuildInspector(uint eventKey, SerializedProperty markerProperty, VisualElement container);
    }

    /// <summary>
    /// The host seam for event payload editors, registered from an
    /// <c>[InitializeOnLoadMethod]</c> the way <c>DirectionSetsPanel.SetContextProvider</c> is.
    /// </summary>
    /// <remarks>
    /// A list rather than one provider: a host with several typed events registers one editor per
    /// family, and the package cannot know which of them owns a given key. The first provider that
    /// claims the key wins, so registration order is the tie-break — a host registering two
    /// providers for one key has a bug of its own to fix.
    /// </remarks>
    public static class CutsceneEventInspectorProviders
    {
        private static readonly List<ICutsceneEventInspectorProvider> providers =
            new List<ICutsceneEventInspectorProvider>();

        /// <summary>Registers a payload editor. Registering the same instance twice is a no-op.</summary>
        public static void Register(ICutsceneEventInspectorProvider provider)
        {
            if (provider != null && !providers.Contains(provider))
            {
                providers.Add(provider);
            }
        }

        /// <summary>Removes a payload editor. Safe to call for one that was never registered.</summary>
        public static void Unregister(ICutsceneEventInspectorProvider provider)
        {
            providers.Remove(provider);
        }

        /// <summary>Lets the first provider that owns <paramref name="eventKey"/> build the payload fields.</summary>
        internal static bool TryBuild(
            uint eventKey, SerializedProperty markerProperty, VisualElement container)
        {
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                if (providers[providerIndex].TryBuildInspector(eventKey, markerProperty, container))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
