using Unity.Mathematics;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The editor mirror of the runtime VAT frame rule, kept separate because the runtime
    /// one is private inside a Burst system.
    /// </summary>
    public static class VatPreviewFrameResolver
    {
        public static bool TryResolveGlobalFrame(ref ClipBlob clip, int targetIndex, float normalizedTime, out float globalFrame)
        {
            globalFrame = 0f;

            int frameStart = clip.vatFrameStart;
            int frameCount = clip.vatFrameCount;
            float fps = clip.vatFps;

            // Runtime's target-first rule: a matching target range wins over the untargeted range.
            for (int rangeIndex = 0; rangeIndex < clip.vatTargetRanges.Length; rangeIndex++)
            {
                ref VatTrackRangeBlob targetRange = ref clip.vatTargetRanges[rangeIndex];
                if (targetRange.targetIndex == targetIndex)
                {
                    frameStart = targetRange.frameStart;
                    frameCount = targetRange.frameCount;
                    fps = targetRange.fps;
                    break;
                }
            }

            if (frameStart < 0 || frameCount <= 0)
            {
                return false;
            }

            // The editor playhead is already a mapped normalized time, so no ClipSampler.MapTime / loop mode here.
            float mappedTime = normalizedTime * clip.duration;
            float localFrame = math.clamp(mappedTime * fps, 0f, frameCount - 1);
            globalFrame = frameStart + localFrame;
            return true;
        }

        public static float GlobalFrameForRange(in VatClipRange range, float timeSeconds)
        {
            return range.frameStart + math.clamp(timeSeconds * range.fps, 0f, math.max(0, range.frameCount - 1));
        }
    }
}
