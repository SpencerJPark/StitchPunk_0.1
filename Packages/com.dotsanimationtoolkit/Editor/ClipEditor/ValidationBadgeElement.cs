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
        private static readonly Color ErrorColor = ToolkitPalette.Error;
        private static readonly Color WarningColor = ToolkitPalette.Warning;
        private static readonly Color CleanColor = ToolkitPalette.Clean;

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
                summaryButton.style.color = CleanColor; // colour from data
                messagePanelTitle.text = "Validation";
                RebuildMessageList();
                return;
            }

            // The stale-bake hash needs the editor-only resolver, which is why the authoring
            // assembly cannot recompute it itself.
            bool vatSourceHashRecomputed = rig != null && clipSet.vatTextures != null;
            ulong recomputedVatSourceHash = vatSourceHashRecomputed
                ? VatSourceHashResolver.ComputeSourceHash(clipSet, rig, clipSet.vatTextures.flavor)
                : 0UL;

            // Validated as the bind the window is showing: this set against whichever rig is
            // loaded. With no rig loaded the binding rules cannot speak and stay quiet — an unbound
            // set is a legitimate state, not a fault.
            List<ValidationMessage> messages = ClipValidation.ValidateBind(
                rig,
                new ClipSetAsset[] { clipSet },
                vatSourceHashRecomputed: vatSourceHashRecomputed,
                recomputedVatSourceHash: recomputedVatSourceHash,
                tagRegistry: VocabularyRegistryProvider.TargetTags,
                eventKeyRegistry: VocabularyRegistryProvider.AnimEventKeys);

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

            DescribeStaleVatBake(messages, clipSet, rig);
            ApplyMessages(messages);
        }

        // The authoring rule can only say the hash moved; the editor resolver can say what changed.
        internal static void DescribeStaleVatBake(
            List<ValidationMessage> messages,
            ClipSetAsset vatOwnerSet,
            RigAsset rig)
        {
            if (messages == null || vatOwnerSet == null || vatOwnerSet.vatTextures == null || rig == null)
            {
                return;
            }

            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                if (message.code != ValidationCode.V08)
                {
                    continue;
                }

                VatSourceHashResolver.Resolve(vatOwnerSet, rig, vatOwnerSet.vatTextures, out string reason);
                string text = "Stale VAT bake on set '" + vatOwnerSet.name + "' for rig '" + rig.name
                    + "'. " + reason + " Use Rebake on the Clip Sets tab, or the VAT Bake tab.";
                messages[messageIndex] = new ValidationMessage(
                    message.severity,
                    message.code,
                    message.assetContext,
                    text);
            }
        }

        /// <summary>
        /// Repaints the badge from an already-computed message list, for a caller whose validation
        /// spans more than one rule table (the Actor Editor combines <c>ActorProfileValidation</c>
        /// and <c>ClipValidation.ValidateBind</c>) rather than one clip set.
        /// </summary>
        /// <param name="emptyLabel">Shown instead of "Valid" when <paramref name="messages"/> is empty — e.g. "No profile".</param>
        public void RefreshFromMessages(List<ValidationMessage> messages, string emptyLabel = null)
        {
            List<ValidationMessage> effectiveMessages = messages ?? new List<ValidationMessage>();
            if (effectiveMessages.Count == 0 && !string.IsNullOrEmpty(emptyLabel))
            {
                currentMessages.Clear();
                HasErrors = false;
                summaryButton.text = emptyLabel;
                summaryButton.style.color = CleanColor; // colour from data
                messagePanelTitle.text = "Validation";
                RebuildMessageList();
                return;
            }
            ApplyMessages(effectiveMessages);
        }

        private void ApplyMessages(List<ValidationMessage> messages)
        {
            currentMessages.Clear();
            HasErrors = false;

            // V08 (stale VAT bake) leads the list: it silently plays old motion, so it must be seen
            // before the reader scrolls past it among unrelated findings.
            bool hasStaleVatBake = false;
            List<ValidationMessage> remainingMessages = new List<ValidationMessage>();
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                if (message.code == ValidationCode.V08)
                {
                    hasStaleVatBake = true;
                    currentMessages.Add(message);
                }
                else
                {
                    remainingMessages.Add(message);
                }
            }
            currentMessages.AddRange(remainingMessages);

            int errorCount = 0;
            int warningCount = 0;
            for (int messageIndex = 0; messageIndex < currentMessages.Count; messageIndex++)
            {
                if (currentMessages[messageIndex].severity == ValidationSeverity.Error)
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
                summaryButton.style.color = CleanColor; // colour from data
            }
            else
            {
                summaryButton.text = errorCount.ToString() + " err  " + warningCount.ToString() + " warn";
                summaryButton.style.color = errorCount > 0 ? ErrorColor : WarningColor; // colour from data
            }

            if (hasStaleVatBake)
            {
                summaryButton.text = "VAT stale · " + summaryButton.text;
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
                messageButton.style.color = message.severity == ValidationSeverity.Error ? ErrorColor : WarningColor; // colour from data
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
