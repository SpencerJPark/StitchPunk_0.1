// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public sealed class RigMaterialUsage
    {
        public Material Material;
        public readonly List<RigTargetDefinition> Targets = new List<RigTargetDefinition>();

        // Renderer paths below the prefab root that use this material but match no rig target's sourceNodePath.
        public readonly List<string> UnmappedNodePaths = new List<string>();
    }

    /// <summary>Lists every material a rig's source prefab renders with, and which rig targets use each one.</summary>
    public static class RigMaterialResolver
    {
        public static List<RigMaterialUsage> Resolve(RigAsset rig)
        {
            return new List<RigMaterialUsage>();
        }

        public static void CollectSheetBindingWarnings(
            RigMaterialUsage usage, ClipSetAsset clipSet, List<ValidationMessage> output)
        {
        }
    }
}
