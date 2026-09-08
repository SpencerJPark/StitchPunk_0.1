// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The holds a cutscene derives without an authored <see cref="CutsceneHoldMarker"/>: an event
    /// marked <see cref="CutsceneEventMarker.holdUntilReleased"/> bakes as a hold whose id is the
    /// event's own registry name, and a mark marked <see cref="CutsceneMarkKey.waitUntilReached"/>
    /// bakes as a rendezvous hold at its own issue time. Shared by <see cref="CutsceneBlobBuilder"/>
    /// and the Cutscene Editor so both derive the same ids.
    /// </summary>
    internal static class CutsceneDerivedHolds
    {
        // The registry owner is editor-only and this assembly ships to players, so it arrives
        // through this seam rather than a direct reference. Lazy, not the registry itself: touching
        // it at domain load would read the settings file on every reload whether or not anything bakes.
        internal static Func<IVocabularyRegistry> EventNameRegistrySource { get; set; }

        /// <summary>One hold derived from a holding event or a wait-until-reached mark, in raw timeline seconds.</summary>
        internal struct DerivedHold
        {
            public float time;
            public string holdId;

            /// <summary>Index into <see cref="CutsceneAsset.events"/>, for the editor's ghost marker. -1 for a mark-derived hold.</summary>
            public int eventIndex;

            /// <summary>False when <see cref="holdId"/> is the <c>event:XXXXXXXX</c> fallback rather than a vocabulary name. Always true for a mark-derived hold.</summary>
            public bool nameResolved;

            /// <summary>Collection order across both sources, the deterministic tie-break for two derived holds sharing one instant (events collected before marks).</summary>
            public int sequence;
        }

        /// <summary>Whether <see cref="DerivedHold"/> came from a wait-until-reached mark rather than a holding event.</summary>
        internal static bool IsMarkDerived(in DerivedHold derivedHold)
        {
            return derivedHold.eventIndex < 0;
        }

        /// <summary>The hold id a wait-until-reached mark contributes: stable across slots sharing a name, unique per instant.</summary>
        internal static string MarkHoldId(string slotName, float time)
        {
            return "mark:" + slotName + "@" + time.ToString("0.###");
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

        /// <summary>
        /// Every derived hold — holding events first, then wait-until-reached marks — ascending by
        /// time and stable within one instant.
        /// </summary>
        internal static List<DerivedHold> Collect(CutsceneAsset cutscene)
        {
            List<DerivedHold> derivedHolds = new List<DerivedHold>();
            if (cutscene == null)
            {
                return derivedHolds;
            }

            int sequence = 0;
            if (cutscene.events != null)
            {
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
                        nameResolved = nameResolved,
                        sequence = sequence++
                    });
                }
            }

            if (cutscene.slots != null)
            {
                for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
                {
                    CutsceneSlot slot = cutscene.slots[slotIndex];
                    if (slot == null || slot.markKeys == null)
                    {
                        continue;
                    }
                    for (int markIndex = 0; markIndex < slot.markKeys.Count; markIndex++)
                    {
                        CutsceneMarkKey mark = slot.markKeys[markIndex];
                        if (!mark.waitUntilReached)
                        {
                            continue;
                        }
                        derivedHolds.Add(new DerivedHold
                        {
                            time = mark.time,
                            holdId = MarkHoldId(slot.name, mark.time),
                            eventIndex = -1,
                            nameResolved = true,
                            sequence = sequence++
                        });
                    }
                }
            }

            // Ties broken by collection order, not left to List.Sort's unstable ordering: several
            // derived holds at the same instant collapse into one boundary and the first one names
            // it, so "the first one" has to mean the same thing on every bake.
            derivedHolds.Sort((left, right) => left.time != right.time
                ? left.time.CompareTo(right.time)
                : left.sequence.CompareTo(right.sequence));
            return derivedHolds;
        }
    }
}
