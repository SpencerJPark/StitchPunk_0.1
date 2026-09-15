// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Every number the Stats tab shows for one poll of a world. The default value is the
    /// "no world" sample: worldAvailable false and every count zero.</summary>
    public struct ToolkitStatsSample
    {
        public bool worldAvailable;
        public string worldName;

        public int actorCount; // entities carrying a PlaybackLayer buffer
        public int layerCount; // PlaybackLayer lengths summed over every actor
        public int ragdollingActorCount;
        public int cutscenePlayerCount;

        public int lodLevel0Count;
        public int lodLevel1Count;
        public int lodLevel2Count;
        public int lodLevel3Count;
        public bool lodSampled; // true when the histogram was bucketed from a capped subset of actors

        public int eventsThisFrame;
        public int pendingEventActorCount;
        public int actorsWithOpenWindowsCount;

        public int vatBoundPartCount;
        public int vatDistinctTextureCount;
        public long vatTextureBytes;

        public bool timingsAvailable;
        public double toolkitGroupMilliseconds;
        public double bindingGroupMilliseconds;
        public double logicGroupMilliseconds;
        public double presentationGroupMilliseconds;
        public double ragdollGroupMilliseconds;
    }
}
