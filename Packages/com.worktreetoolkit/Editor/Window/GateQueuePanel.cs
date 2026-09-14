using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    // Renders the current gate queue (queue/*.json) as a foldout list. Read-only: it never
    // mutates the GateRequestStore, it only lists what is already on disk.
    public sealed class GateQueuePanel : VisualElement
    {
        private readonly Foldout queueFoldout;

        public GateQueuePanel()
        {
            this.AddToClassList("gate-queue");

            this.queueFoldout = new Foldout
            {
                text = "Gate queue (0)",
                value = false
            };
            this.Add(this.queueFoldout);
        }

        public void Refresh(GateRequestStore requestStore)
        {
            this.queueFoldout.Clear();

            if (requestStore == null)
            {
                this.queueFoldout.text = "Gate queue (0)";
                this.queueFoldout.Add(new Label("queue unavailable"));
                return;
            }

            List<GateRequestDto> queuedRequests;
            try
            {
                queuedRequests = requestStore.ListQueuedRequests();
            }
            catch (Exception queueReadException) when (queueReadException is IOException || queueReadException is UnauthorizedAccessException)
            {
                this.queueFoldout.text = "Gate queue (0)";
                this.queueFoldout.Add(new Label("queue unavailable: " + queueReadException.Message));
                return;
            }

            this.queueFoldout.text = "Gate queue (" + queuedRequests.Count.ToString(CultureInfo.InvariantCulture) + ")";

            if (queuedRequests.Count == 0)
            {
                this.queueFoldout.Add(new Label("empty"));
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (GateRequestDto queuedRequest in queuedRequests)
            {
                this.queueFoldout.Add(BuildRow(queuedRequest, nowUtc));
            }
        }

        private static VisualElement BuildRow(GateRequestDto queuedRequest, DateTime nowUtc)
        {
            VisualElement rowElement = new VisualElement();
            rowElement.AddToClassList("gate-queue__row");
            rowElement.style.flexDirection = FlexDirection.Row;

            Label worktreeIdLabel = new Label(queuedRequest.worktreeId);
            Label phaseLabel = new Label(queuedRequest.phase);
            phaseLabel.AddToClassList("gate-queue__phase");
            Label ageLabel = new Label(FormatAge(queuedRequest.createdUtc, nowUtc));
            Label shortCommitLabel = new Label(FormatShortCommit(queuedRequest.commitSha));

            rowElement.Add(worktreeIdLabel);
            rowElement.Add(phaseLabel);
            rowElement.Add(ageLabel);
            rowElement.Add(shortCommitLabel);
            return rowElement;
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

        private static string FormatShortCommit(string commitSha)
        {
            if (string.IsNullOrEmpty(commitSha))
            {
                return string.Empty;
            }

            return commitSha.Length <= 8 ? commitSha : commitSha.Substring(0, 8);
        }
    }
}
