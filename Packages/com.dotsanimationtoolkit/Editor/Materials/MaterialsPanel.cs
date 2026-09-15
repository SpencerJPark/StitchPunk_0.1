// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab: every material the shared rig's prefab uses, checked against the shader contract, with Create for a target.</summary>
    public sealed class MaterialsPanel : VisualElement, IDisposable
    {
        private readonly List<RigMaterialUsage> usages = new List<RigMaterialUsage>();

        public RigAsset BoundRig { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }
        public Material LastCreatedMaterial { get; private set; }

        public IReadOnlyList<RigMaterialUsage> Usages
        {
            get { return usages; }
        }

        public MaterialsPanel()
        {
            style.flexGrow = 1f;
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
        }

        public void SetRig(RigAsset rig)
        {
            BoundRig = rig;
        }

        public void SetClipSet(ClipSetAsset clipSet)
        {
            BoundClipSet = clipSet;
        }

        public void Refresh()
        {
        }

        public bool CreateForTarget(RigTargetDefinition target, out string failureMessage)
        {
            failureMessage = string.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
