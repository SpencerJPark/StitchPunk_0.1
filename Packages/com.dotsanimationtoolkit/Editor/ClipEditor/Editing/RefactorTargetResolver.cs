// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which markers, ragdoll triggers and tracks a refactor operation would touch inside one asset. Pure: no AssetDatabase.</summary>
    public static class RefactorTargetResolver
    {
        public static List<int> FindEventMarkerIndices(ClipAsset clip, uint fromKey)
        {
            List<int> markerIndices = new List<int>();
            if (fromKey == 0u || clip == null || clip.events == null)
            {
                return markerIndices;
            }

            for (int markerIndex = 0; markerIndex < clip.events.Count; markerIndex++)
            {
                if (clip.events[markerIndex].eventKey == fromKey)
                {
                    markerIndices.Add(markerIndex);
                }
            }

            return markerIndices;
        }

        public static List<int> FindCutsceneEventMarkerIndices(CutsceneAsset cutscene, uint fromKey)
        {
            List<int> markerIndices = new List<int>();
            if (fromKey == 0u || cutscene == null || cutscene.events == null)
            {
                return markerIndices;
            }

            for (int markerIndex = 0; markerIndex < cutscene.events.Count; markerIndex++)
            {
                CutsceneEventMarker eventMarker = cutscene.events[markerIndex];
                if (eventMarker != null && eventMarker.eventKey == fromKey)
                {
                    markerIndices.Add(markerIndex);
                }
            }

            return markerIndices;
        }

        public static List<ActorAnimationDefinition> FindRagdollEventDefinitions(ActorProfileAsset profile, uint fromKey)
        {
            List<ActorAnimationDefinition> ragdollDefinitions = new List<ActorAnimationDefinition>();
            if (fromKey == 0u || profile == null || profile.layers == null)
            {
                return ragdollDefinitions;
            }

            foreach (ActorLayerDefinition layerDefinition in profile.layers)
            {
                if (layerDefinition == null || layerDefinition.animations == null)
                {
                    continue;
                }

                foreach (ActorAnimationDefinition animationDefinition in layerDefinition.animations)
                {
                    if (animationDefinition != null &&
                        animationDefinition.ragdollTrigger != RagdollTrigger.None &&
                        animationDefinition.ragdollAtEventKey == fromKey)
                    {
                        ragdollDefinitions.Add(animationDefinition);
                    }
                }
            }

            return ragdollDefinitions;
        }

        public static List<int> FindTransformTrackIndices(ClipAsset clip, uint fromTagId)
        {
            List<int> trackIndices = new List<int>();
            if (clip == null || clip.transformTracks == null)
            {
                return trackIndices;
            }

            for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
            {
                TransformTrack transformTrack = clip.transformTracks[trackIndex];
                if (transformTrack != null && transformTrack.tagId != 0u && transformTrack.tagId == fromTagId)
                {
                    trackIndices.Add(trackIndex);
                }
            }

            return trackIndices;
        }

        public static List<int> FindSpriteTrackIndices(ClipAsset clip, uint fromTagId)
        {
            List<int> trackIndices = new List<int>();
            if (clip == null || clip.spriteTracks == null)
            {
                return trackIndices;
            }

            for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
            {
                SpriteTrack spriteTrack = clip.spriteTracks[trackIndex];
                if (spriteTrack != null && spriteTrack.tagId != 0u && spriteTrack.tagId == fromTagId)
                {
                    trackIndices.Add(trackIndex);
                }
            }

            return trackIndices;
        }

        public static List<CutsceneKeyedTrack> FindCutscenePartTracks(CutsceneAsset cutscene, uint fromTagId)
        {
            List<CutsceneKeyedTrack> keyedTracks = new List<CutsceneKeyedTrack>();
            if (cutscene == null || cutscene.slots == null)
            {
                return keyedTracks;
            }

            foreach (CutsceneSlot cutsceneSlot in cutscene.slots)
            {
                if (cutsceneSlot == null || cutsceneSlot.partTracks == null)
                {
                    continue;
                }

                foreach (CutsceneKeyedTrack keyedTrack in cutsceneSlot.partTracks)
                {
                    if (keyedTrack != null && keyedTrack.tagId != 0u && keyedTrack.tagId == fromTagId)
                    {
                        keyedTracks.Add(keyedTrack);
                    }
                }
            }

            return keyedTracks;
        }

        public static bool PayloadSchemasMatch(AnimEventKeyEntry firstEntry, AnimEventKeyEntry secondEntry)
        {
            string firstIntParamLabel = firstEntry == null ? string.Empty : firstEntry.intParamLabel;
            string secondIntParamLabel = secondEntry == null ? string.Empty : secondEntry.intParamLabel;
            if ((firstIntParamLabel ?? string.Empty) != (secondIntParamLabel ?? string.Empty))
            {
                return false;
            }

            string firstFloatParamLabel = firstEntry == null ? string.Empty : firstEntry.floatParamLabel;
            string secondFloatParamLabel = secondEntry == null ? string.Empty : secondEntry.floatParamLabel;
            if ((firstFloatParamLabel ?? string.Empty) != (secondFloatParamLabel ?? string.Empty))
            {
                return false;
            }

            string firstFloatParamUnit = firstEntry == null ? string.Empty : firstEntry.floatParamUnit;
            string secondFloatParamUnit = secondEntry == null ? string.Empty : secondEntry.floatParamUnit;
            if ((firstFloatParamUnit ?? string.Empty) != (secondFloatParamUnit ?? string.Empty))
            {
                return false;
            }

            List<string> firstIntParamValueNames = firstEntry == null ? null : firstEntry.intParamValueNames;
            List<string> secondIntParamValueNames = secondEntry == null ? null : secondEntry.intParamValueNames;
            int firstValueNameCount = firstIntParamValueNames == null ? 0 : firstIntParamValueNames.Count;
            int secondValueNameCount = secondIntParamValueNames == null ? 0 : secondIntParamValueNames.Count;
            if (firstValueNameCount != secondValueNameCount)
            {
                return false;
            }

            for (int valueNameIndex = 0; valueNameIndex < firstValueNameCount; valueNameIndex++)
            {
                string firstValueName = firstIntParamValueNames[valueNameIndex] ?? string.Empty;
                string secondValueName = secondIntParamValueNames[valueNameIndex] ?? string.Empty;
                if (firstValueName != secondValueName)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
