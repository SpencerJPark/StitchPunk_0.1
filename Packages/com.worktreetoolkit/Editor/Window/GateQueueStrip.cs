using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    // Renders the current gate queue (queue/*.json) as a horizontal strip of chips, one per
    // queued request. Read-only: it never mutates the GateRequestStore, it only lists what is
    // already on disk.
    public sealed class GateQueueStrip : VisualElement
    {
        public GateQueueStrip()
        {
            this.AddToClassList("worktree-queue-strip");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexWrap = Wrap.Wrap;
        }

        public void Refresh(GateRequestStore requestStore)
        {
            this.Clear();

            if (requestStore == null)
            {
                this.Add(new Label("gate queue unavailable"));
                return;
            }

            List<GateRequestDto> queuedRequests;
            try
            {
                queuedRequests = requestStore.ListQueuedRequests();
            }
            catch (Exception queueReadException) when (queueReadException is IOException || queueReadException is UnauthorizedAccessException)
            {
                this.Add(new Label("gate queue unavailable: " + queueReadException.Message));
                return;
            }

            if (queuedRequests.Count == 0)
            {
                this.Add(new Label("gate queue empty"));
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (GateRequestDto queuedRequest in queuedRequests)
            {
                this.Add(BuildChip(queuedRequest, nowUtc));
            }
        }

        private static Label BuildChip(GateRequestDto queuedRequest, DateTime nowUtc)
        {
            string chipText = queuedRequest.worktreeId + " · " + queuedRequest.phase + " · " + FormatAge(queuedRequest.createdUtc, nowUtc);
            Label chipLabel = new Label(chipText);
            chipLabel.AddToClassList("worktree-queue-chip");

            if (IsActivePhase(queuedRequest.phase))
            {
                chipLabel.AddToClassList("worktree-queue-chip--active");
            }

            return chipLabel;
        }

        private static bool IsActivePhase(string phase)
        {
            return phase == GatePhases.Staging
                || phase == GatePhases.Compiling
                || phase == GatePhases.Testing
                || phase == GatePhases.Returning;
        }

        private static string FormatAge(string createdUtcText, DateTime nowUtc)
        {
            DateTime createdUtc;
            bool parsedCreatedUtc = DateTime.TryParse(
                createdUtcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out createdUtc);

            if (!parsedCreatedUtc)
            {
                return "unknown age";
            }

            TimeSpan age = nowUtc - createdUtc;
            if (age.TotalSeconds < 0.0)
            {
                age = TimeSpan.Zero;
            }

            if (age.TotalMinutes < 1.0)
            {
                return ((int)age.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s";
            }

            return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min";
        }
    }
}
