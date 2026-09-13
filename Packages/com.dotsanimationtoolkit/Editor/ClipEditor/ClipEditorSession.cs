// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The clip, key and playhead one Clip Editor window is on, shared by its panes; every setter raises its event on every call, not only on a change, because a re-select is how the window resets the playhead and rebuilds.</summary>
    public sealed class ClipEditorSession
    {
        public ClipAsset SelectedClip { get; private set; }
        public KeyAddress SelectedKey { get; private set; }
        public float PlayheadNormalized { get; private set; }

        public event Action<ClipAsset> SelectedClipChanged;
        public event Action<KeyAddress> SelectedKeyChanged;
        public event Action<float> PlayheadChanged;

        // "Something structural changed; panes re-query." Raised by a pane after it created,
        // deleted or renamed a clip; the window answers with the preview and the badge.
        public event Action RebuildRequested;

        public void SetSelectedClip(ClipAsset clip)
        {
            SelectedClip = clip;
            SelectedClipChanged?.Invoke(clip);
        }

        public void SetSelectedKey(KeyAddress address)
        {
            SelectedKey = address;
            SelectedKeyChanged?.Invoke(address);
        }

        public void SetPlayhead(float normalizedTime)
        {
            PlayheadNormalized = normalizedTime;
            PlayheadChanged?.Invoke(normalizedTime);
        }

        public void RequestRebuild()
        {
            RebuildRequested?.Invoke();
        }
    }
}
