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
            return new List<int>();
        }

        public static List<int> FindCutsceneEventMarkerIndices(CutsceneAsset cutscene, uint fromKey)
        {
            return new List<int>();
        }

        public static List<ActorAnimationDefinition> FindRagdollEventDefinitions(ActorProfileAsset profile, uint fromKey)
        {
            return new List<ActorAnimationDefinition>();
        }

        public static List<int> FindTransformTrackIndices(ClipAsset clip, uint fromTagId)
        {
            return new List<int>();
        }

        public static List<int> FindSpriteTrackIndices(ClipAsset clip, uint fromTagId)
        {
            return new List<int>();
        }

        public static List<CutsceneKeyedTrack> FindCutscenePartTracks(CutsceneAsset cutscene, uint fromTagId)
        {
            return new List<CutsceneKeyedTrack>();
        }

        public static bool PayloadSchemasMatch(AnimEventKeyEntry firstEntry, AnimEventKeyEntry secondEntry)
        {
            return true;
        }
    }
}
