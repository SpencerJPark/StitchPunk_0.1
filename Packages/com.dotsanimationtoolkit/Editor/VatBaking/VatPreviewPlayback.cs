using UnityEngine;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Pure-logic playback clock for the VAT bake preview viewport; mirrors VatMaterialSystem's global-frame resolution formula.</summary>
    public sealed class VatPreviewPlayback
    {
        private VatClipRange range;
        private bool hasRange;
        private float time;

        public bool HasRange => hasRange;

        public float Time
        {
            get => time;
            set => time = Mathf.Clamp(value, 0f, Duration);
        }

        public bool Loop { get; set; }

        public float Duration => hasRange && range.fps > 0f ? range.frameCount / range.fps : 0f;

        public float GlobalFrame => hasRange
            ? range.frameStart + Mathf.Clamp(Time * range.fps, 0f, range.frameCount - 1)
            : 0f;

        public int LocalFrameIndex => hasRange
            ? (int)Mathf.Clamp(Time * range.fps, 0f, range.frameCount - 1)
            : 0;

        public void SetRange(VatClipRange range)
        {
            this.range = range;
            hasRange = true;
            Time = 0f;
        }

        public void ClearRange()
        {
            range = default(VatClipRange);
            hasRange = false;
            time = 0f;
        }

        public bool Advance(float deltaSeconds)
        {
            if (!hasRange)
            {
                return false;
            }

            float nextTime = Time + deltaSeconds;

            if (Loop)
            {
                time = WrapIntoRange(nextTime);
                return true;
            }

            if (nextTime >= Duration)
            {
                Time = Duration;
                return false;
            }

            Time = nextTime;
            return true;
        }

        public void StepFrames(int frameDelta)
        {
            if (!hasRange)
            {
                return;
            }

            float delta = frameDelta / range.fps;
            float nextTime = Time + delta;

            if (Loop)
            {
                time = WrapIntoRange(nextTime);
                return;
            }

            Time = nextTime;
        }

        public void JumpToStart()
        {
            Time = 0f;
        }

        public void JumpToEnd()
        {
            Time = Duration;
        }

        private float WrapIntoRange(float value)
        {
            float duration = Duration;
            if (duration <= 0f)
            {
                return 0f;
            }

            return value - duration * Mathf.Floor(value / duration);
        }
    }
}
