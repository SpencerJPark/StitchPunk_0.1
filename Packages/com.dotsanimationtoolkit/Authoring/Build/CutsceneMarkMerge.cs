// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Folds a slot's move-to marks into its root lane (amendment A64, decision A64-D2): one Linear
    /// key per mark, at the instant the rehearsed walk arrives.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="CutsceneBlobBuilder"/> and the editor preview on purpose. The merged key
    /// is what makes the editor show the walk and what gives A62's boundary pass the arrival pose to
    /// bake at a rendezvous hold; if only one of the two merged, preview and playback would disagree
    /// about where an actor stands after every mark.
    /// </remarks>
    internal static class CutsceneMarkMerge
    {
        /// <summary>When the rehearsed walk to <paramref name="mark"/> arrives, in raw timeline seconds.</summary>
        public static float ArrivalTime(CutsceneMarkKey mark)
        {
            return mark.time + math.max(0f, mark.previewTravelSeconds);
        }

        /// <summary>
        /// The slot's authored root keys plus one merged arrival key per mark, ascending by time.
        /// Returns the authored list itself when the slot has no marks, so the common case allocates
        /// nothing.
        /// </summary>
        public static List<CutsceneTransformKey> BuildEffectiveRootKeys(CutsceneSlot slot)
        {
            if (slot == null)
            {
                return null;
            }
            if (slot.markKeys == null || slot.markKeys.Count == 0)
            {
                return slot.transformKeys;
            }

            List<CutsceneTransformKey> mergedKeys = slot.transformKeys != null
                ? new List<CutsceneTransformKey>(slot.transformKeys)
                : new List<CutsceneTransformKey>();

            for (int markIndex = 0; markIndex < slot.markKeys.Count; markIndex++)
            {
                CutsceneMarkKey mark = slot.markKeys[markIndex];
                float arrivalTime = ArrivalTime(mark);

                // Scale is sampled from the AUTHORED lane, never from the partly-merged list: a
                // merged key must not depend on which other mark happened to be folded in first.
                float3 sampledPosition;
                float3 sampledRotation;
                float3 sampledScale;
                if (!CutsceneKeySampler.TrySampleTransform(
                    slot.transformKeys, arrivalTime, out sampledPosition, out sampledRotation, out sampledScale))
                {
                    sampledScale = new float3(1f, 1f, 1f);
                }

                mergedKeys.Add(new CutsceneTransformKey
                {
                    time = arrivalTime,
                    position = mark.position,
                    rotation = new float3(0f, mark.facingDegrees, 0f),
                    scale = sampledScale,
                    interpolation = Interpolation.Linear
                });
            }

            mergedKeys.Sort((left, right) => left.time.CompareTo(right.time));
            return mergedKeys;
        }
    }
}
