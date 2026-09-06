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
    /// behind them. Never decides what is valid itself — it renders whatever
    /// <see cref="ClipValidation"/> returns, and starts hidden until the summary button is pressed.
    /// </summary>
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

        // Parents the findings list into host, expected to be the viewport frame. Must be painted
        // after the 3D image and under the same parent, so the list appears over the preview rather
        // than floating across the whole window. A null host leaves the list unparented rather than throwing.
        public void AttachMessagePanel(VisualElement host)
        {
            if (host == null)
            {
                return;
            }
            host.Add(messagePanel);
        }

        // Revalidates clipSet and repaints the badge. Callers drive this on selection and after an
        // edit settles, never per repaint — a full set validation walks every key of every clip.
        /// <param name="clipSet">The set to validate. Null clears the badge.</param>
        public void Refresh(RigAsset rig, ClipSetAsset clipSet)
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

            // Validated as the bind the window is showing: this set against whichever rig is
            // loaded. With no rig loaded the binding rules cannot speak and stay quiet — an unbound
            // set is a legitimate state, not a fault.
            List<ValidationMessage> messages = ClipValidation.ValidateBind(
                rig,
                new ClipSetAsset[] { clipSet },
                tagRegistry: VocabularyRegistryProvider.TargetTags);

            // A project-wide fact ClipValidation cannot see on its own (Editor-only AssetDatabase
            // access), appended here rather than folded into the call above.
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

                // The rule code is shown, not just the prose: it is what a user can search the docs
                // for and what a bake log prints.
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
