// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The clip editor's validation indicator: error and warning counts, expanding to the messages
    /// behind them (architecture section 7.6b).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This element never decides what is valid.</strong> It renders whatever
    /// <see cref="ClipValidation"/> returns. Section 7.6 requires one source of truth surfaced
    /// three ways — inline in inspectors, here, and in bake failure text — so that an error seen in
    /// a build log and an error seen in this window are the same rule with the same code. A badge
    /// that ran its own checks would eventually disagree with the bake, and the bake is the one
    /// that matters.
    /// </para>
    /// <para>
    /// <strong>This is the window's only list of findings, and it is off until asked for.</strong>
    /// The findings used to reach the user twice: once here, and once as the raw
    /// <see cref="ClipValidationException"/> text that <c>ClipPreviewController</c> put in the
    /// viewport status line — a multi-line dump sitting directly above the 3D preview, squeezing it
    /// every time a set was mid-edit. Two renderings of one rule set is one too many: they wrap
    /// differently, order differently, and the one you cannot switch off is the one in the way. The
    /// status line now says a single sentence and points here; the list lives here alone, and the
    /// summary button is its switch.
    /// </para>
    /// <para>
    /// <strong>The list is not a child of this element.</strong> It hangs over the 3D viewport
    /// instead — see <see cref="AttachMessagePanel"/>. Left inside the top bar it had nowhere to go
    /// but down, out of a <c>Toolbar</c> that is one control tall, into a body painted after it.
    /// </para>
    /// <para>
    /// Messages carry an <c>assetContext</c>, so clicking one selects the offending asset. That is
    /// the whole navigation story for now: section 7.6 also wants a click to focus the offending
    /// key or track, but a <see cref="ValidationMessage"/> does not carry a key address, so honest
    /// asset-level navigation beats inventing a mapping from message text.
    /// </para>
    /// </remarks>
    public sealed class ValidationBadgeElement : VisualElement
    {
        private static readonly Color ErrorColor = new Color(0.90f, 0.35f, 0.32f);
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private readonly Button summaryButton;
        private readonly VisualElement messagePanel;
        private readonly Label messagePanelTitle;
        private readonly ScrollView messageList;
        private readonly List<ValidationMessage> currentMessages = new List<ValidationMessage>();

        private bool isExpanded;

        /// <summary>
        /// Layout comes from ClipEditorWindow.uss; only the severity colours stay in C#, because
        /// they are chosen per message from validation output rather than authored per element.
        /// </summary>
        public const string UssClassName = "clip-editor__validation-badge";

        private const string SummaryUssClassName = "clip-editor__validation-summary";
        private const string SummaryExpandedUssClassName = "clip-editor__validation-summary--expanded";
        private const string PanelUssClassName = "clip-editor__validation-overlay";
        private const string PanelTitleUssClassName = "clip-editor__validation-overlay-title";
        private const string MessageListUssClassName = "clip-editor__validation-messages";
        private const string MessageUssClassName = "clip-editor__validation-message";
        private const string HiddenUssClassName = "clip-editor--hidden";

        public ValidationBadgeElement()
        {
            AddToClassList(UssClassName);

            summaryButton = new Button(ToggleExpanded) { text = "—" };
            summaryButton.AddToClassList(SummaryUssClassName);
            summaryButton.tooltip =
                "Show or hide the validation findings for this clip set. The list appears over a "
                + "corner of the preview and starts hidden, so a set mid-edit does not spend its "
                + "errors on the space you are posing in.";
            Add(summaryButton);

            // Built here and parented elsewhere. Built here because the summary and the list are two
            // halves of one control, and splitting their state across two classes is how they would
            // drift apart; parented elsewhere because the top bar cannot hold it.
            messagePanel = new VisualElement();
            messagePanel.AddToClassList(PanelUssClassName);
            messagePanel.AddToClassList(HiddenUssClassName);

            messagePanelTitle = new Label("Validation");
            messagePanelTitle.AddToClassList(PanelTitleUssClassName);
            messagePanel.Add(messagePanelTitle);

            messageList = new ScrollView(ScrollViewMode.Vertical);
            messageList.AddToClassList(MessageListUssClassName);
            messagePanel.Add(messageList);
        }

        /// <summary>Whether the last validation found anything that blocks a bake.</summary>
        public bool HasErrors { get; private set; }

        /// <summary>
        /// Parents the findings list into <paramref name="host"/>, which is expected to be the
        /// viewport frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The host has to be painted after the 3D image and bounded by it.</strong> UI
        /// Toolkit paints siblings in order, so a panel that must appear over the preview has to
        /// come after it under the same parent — which is also what keeps the list inside the 3D
        /// area rather than floating across the whole window. The stylesheet anchors it to one
        /// corner and caps its size from there, so the scene stays visible around it.
        /// </para>
        /// <para>
        /// Called once, from the window's viewport binding. A null host leaves the list unparented
        /// and the toggle inert rather than throwing — the same failure mode as every other
        /// <c>Q</c> miss in this window, and guarded by the same layout test.
        /// </para>
        /// </remarks>
        public void AttachMessagePanel(VisualElement host)
        {
            if (host == null)
            {
                return;
            }
            host.Add(messagePanel);
        }

        /// <summary>
        /// Revalidates <paramref name="clipSet"/> and repaints the badge.
        /// </summary>
        /// <remarks>
        /// Callers drive this on selection and after an edit settles, never per repaint — a full
        /// set validation walks every key of every clip, which is fine occasionally and ruinous at
        /// sixty hertz.
        /// </remarks>
        /// <param name="clipSet">The set to validate. Null clears the badge.</param>
        /// <param name="tagRegistry">
        /// The project's target tag registry (Phase E target-tags spec §6.1), threaded into
        /// <see cref="ClipValidation.ValidateSet"/> so T2 (V35) can name a tag instead of showing its
        /// raw hex id and T3 (V36) can be judged at all. Optional — a null registry still shows T2,
        /// just without a name, and never shows T3 (see <see cref="ClipValidation.ValidateClip"/>'s
        /// remarks). This is also where T4 (V37) enters: §6.1 requires it in this badge, not only
        /// the bake console, since it is expected to be the most common finding this feature
        /// produces.
        /// </param>
        public void Refresh(ClipSetAsset clipSet, TargetTagRegistry tagRegistry = null)
        {
            currentMessages.Clear();
            HasErrors = false;

            if (clipSet == null)
            {
                summaryButton.text = "No clip set";
                summaryButton.style.color = CleanColor;
                messagePanelTitle.text = "Validation";
                RebuildMessageList();
                return;
            }

            // Validation throws only on a null set, which is already handled; anything else it finds
            // is data it returns rather than an exception, so no guard is needed here.
            List<ValidationMessage> messages = ClipValidation.ValidateSet(
                clipSet, tagRegistry: tagRegistry);

            // T4 (V37) is a project-wide fact ClipValidation cannot see on its own (Editor-only
            // AssetDatabase access) — appended here rather than folded into the call above, mirroring
            // how ClipRegistryDeterminismTests keeps bake-side and Editor-side concerns apart.
            if (clipSet.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }
                    messages.AddRange(SharedClipBindingUtility.ValidateSharedClipBinding(clip));
                }
            }

            int errorCount = 0;
            int warningCount = 0;
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                currentMessages.Add(message);
                if (message.severity == ValidationSeverity.Error)
                {
                    errorCount++;
                }
                else
                {
                    warningCount++;
                }
            }

            HasErrors = errorCount > 0;

            if (errorCount == 0 && warningCount == 0)
            {
                summaryButton.text = "Valid";
                summaryButton.style.color = CleanColor;
            }
            else
            {
                summaryButton.text = errorCount.ToString() + " err  " + warningCount.ToString() + " warn";
                summaryButton.style.color = errorCount > 0 ? ErrorColor : WarningColor;
            }

            // The panel repeats the counts because it is read on its own, over the preview, with the
            // button it belongs to at the far end of a different row.
            messagePanelTitle.text = "Validation — " + summaryButton.text;

            RebuildMessageList();
        }

        private void ToggleExpanded()
        {
            isExpanded = !isExpanded;
            messagePanel.EnableInClassList(HiddenUssClassName, !isExpanded);
            summaryButton.EnableInClassList(SummaryExpandedUssClassName, isExpanded);
        }

        private void RebuildMessageList()
        {
            messageList.Clear();

            if (currentMessages.Count == 0)
            {
                messageList.Add(new Label("Nothing to report."));
                return;
            }

            for (int messageIndex = 0; messageIndex < currentMessages.Count; messageIndex++)
            {
                ValidationMessage message = currentMessages[messageIndex];

                // The rule code is shown, not just the prose. A code is what a user can search the
                // docs for and what a bake log prints, so hiding it would break the link between
                // the three places section 7.6 surfaces the same finding.
                Button messageButton = new Button(() => SelectContext(message))
                {
                    text = message.code.ToString() + "  " + message.text
                };
                messageButton.AddToClassList(MessageUssClassName);
                messageButton.style.color =
                    message.severity == ValidationSeverity.Error ? ErrorColor : WarningColor;
                messageList.Add(messageButton);
            }
        }

        private static void SelectContext(ValidationMessage message)
        {
            // Null is legitimate: a finding about a missing reference has no asset to point at.
            if (message.assetContext == null)
            {
                return;
            }
            Selection.activeObject = message.assetContext;
            EditorGUIUtility.PingObject(message.assetContext);
        }
    }
}
