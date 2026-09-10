// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The clip set and rig one editor window is working on, shared by every tab; each setter raises its event only when the value actually changes.</summary>
    public sealed class ActiveAssetSelection
    {
        public ClipSetAsset ClipSet { get; private set; }
        public RigAsset Rig { get; private set; }

        public event Action<ClipSetAsset> ClipSetChanged;
        public event Action<RigAsset> RigChanged;

        public void SetClipSet(ClipSetAsset clipSet)
        {
            // ReferenceEquals, not ==, so a destroyed-but-not-null asset still counts as a change.
            if (ReferenceEquals(clipSet, ClipSet))
            {
                return;
            }

            ClipSet = clipSet;
            ClipSetChanged?.Invoke(clipSet);
        }

        public void SetRig(RigAsset rig)
        {
            if (ReferenceEquals(rig, Rig))
            {
                return;
            }

            Rig = rig;
            RigChanged?.Invoke(rig);
        }
    }
}
