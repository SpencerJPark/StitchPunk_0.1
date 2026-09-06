// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The holds a cutscene's <em>events</em> imply (amendment A65 §3.1, decision A65-D1): an event
    /// marked <see cref="CutsceneEventMarker.holdUntilReleased"/> is baked as a hold whose id is the
    /// event's own registry name, so a dialogue cue is one marker rather than an event plus a hold
    /// with a hand-matched id.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="CutsceneBlobBuilder"/> and the Cutscene Editor for the same reason
    /// <see cref="CutsceneMarkMerge"/> is: if the bake and the transport derived the id separately
    /// they could name the same hold differently, and the editor's Continue would rehearse a release
    /// the host could never send.
    /// </remarks>
    internal static class CutsceneDerivedHolds
    {
        /// <summary>
        /// The project event vocabulary, supplied by the Editor assembly.
        /// </summary>
        /// <remarks>
        /// <c>Authoring/</c> ships to players and may not name <c>UnityEditor</c> (Conformance_C),
        /// and <c>VocabularyRegistryProvider</c> — which owns the <c>ProjectSettings/</c> file — is
        /// editor-only, so the registry arrives through this seam the way
        /// <c>DirectionSetsPanel.SetContextProvider</c> takes its host context. A lazy accessor
        /// rather than the registry itself: registration happens at domain load, and touching the
        /// provider there would read the settings file on every reload whether or not anything
        /// bakes.
        /// </remarks>
        internal static Func<IVocabularyRegistry> EventNameRegistrySource { get; set; }

        /// <summary>One hold derived from a holding event, in raw timeline seconds.</summary>
        internal struct DerivedHold
        {
            public float time;
            public string holdId;

            /// <summary>Index into <see cref="CutsceneAsset.events"/>, for the editor's ghost marker.</summary>
            public int eventIndex;

            /// <summary>False when <see cref="holdId"/> is the <c>event:XXXXXXXX</c> fallback rather than a vocabulary name.</summary>
            public bool nameResolved;
        }

        /// <summary>
        /// The hold id a holding event contributes: its registry name, or a stable
        /// <c>event:XXXXXXXX</c> fallback when no vocabulary names the key.
        /// </summary>
        /// <returns>False when the name could not be resolved, so a bake can warn about it once.</returns>
        internal static bool TryResolveHoldId(uint eventKey, out string holdId)
        {
            Func<IVocabularyRegistry> registrySource = EventNameRegistrySource;
            IVocabularyRegistry registry = registrySource != null ? registrySource() : null;
            string name = registry != null ? registry.FindName(eventKey) : null;
            if (!string.IsNullOrEmpty(name))
            {
                holdId = name;
                return true;
            }
            holdId = "event:" + eventKey.ToString("X8");
            return false;
        }

        /// <summary>Every holding event's derived hold, ascending by time and stable within one instant.</summary>
        internal static List<DerivedHold> Collect(CutsceneAsset cutscene)
        {
            List<DerivedHold> derivedHolds = new List<DerivedHold>();
            if (cutscene == null || cutscene.events == null)
            {
                return derivedHolds;
            }

            for (int eventIndex = 0; eventIndex < cutscene.events.Count; eventIndex++)
            {
                CutsceneEventMarker eventMarker = cutscene.events[eventIndex];
                if (eventMarker == null || !eventMarker.holdUntilReleased)
                {
                    continue;
                }
                string holdId;
                bool nameResolved = TryResolveHoldId(eventMarker.eventKey, out holdId);
                derivedHolds.Add(new DerivedHold
                {
                    time = eventMarker.time,
                    holdId = holdId,
                    eventIndex = eventIndex,
                    nameResolved = nameResolved
                });
            }

            // Ties broken by authored order, not left to List.Sort's unstable ordering: two holding
            // events at the same instant collapse into one boundary and the first one names it, so
            // "the first one" has to mean the same thing on every bake.
            derivedHolds.Sort((left, right) => left.time != right.time
                ? left.time.CompareTo(right.time)
                : left.eventIndex.CompareTo(right.eventIndex));
            return derivedHolds;
        }
    }
}
