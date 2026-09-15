// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Writes a ready-to-edit host ISystem that reads animation events and switches over their routes. Host code, never package code.</summary>
    public static class AnimEventConsumerStubBuilder
    {
        public const string FolderPrefsKey = "DotsAnimationToolkit.Events.StubFolder";

        // STUB (A93-T1): T6 replaces every body below.
        public static string SystemTypeName(string systemName)
        {
            return string.Empty;
        }

        public static string BuildSource(string systemName, IReadOnlyList<AnimEventRouteKind> kindsUsed)
        {
            return string.Empty;
        }

        public static List<AnimEventRouteKind> CollectKindsUsed(AnimEventRoutingAsset routingAsset)
        {
            return new List<AnimEventRouteKind>();
        }

        public static string WriteToFolder(string folderPath, string systemName, IReadOnlyList<AnimEventRouteKind> kindsUsed)
        {
            return string.Empty;
        }
    }
}
