// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
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
        private readonly VisualElement messageList;
        private readonly List<ValidationMessage> currentMessages = new List<ValidationMessage>();

        private bool isExpanded;

        public ValidationBadgeElement()
        {
            style.flexDirection = FlexDirection.Column;

            summaryButton = new Button(ToggleExpanded) { text = "—" };
            summaryButton.style.marginLeft = 4f;
            summaryButton.style.marginRight = 4f;
            Add(summaryButton);

            messageList = new VisualElement();
            messageList.style.display = DisplayStyle.None;
            messageList.style.marginLeft = 8f;
            messageList.style.maxHeight = 220f;
            Add(messageList);
        }

        /// <summary>Whether the last validation found anything that blocks a bake.</summary>
        public bool HasErrors { get; private set; }

        /// <summary>
        /// Revalidates <paramref name="clipSet"/> and repaints the badge.
        /// </summary>
        /// <remarks>
        /// Callers drive this on selection and after an edit settles, never per repaint — a full
        /// set validation walks every key of every clip, which is fine occasionally and ruinous at
        /// sixty hertz.
        /// </remarks>
        public void Refresh(ClipSetAsset clipSet)
        {
            currentMessages.Clear();
            HasErrors = false;

            if (clipSet == null)
            {
                summaryButton.text = "No clip set";
                summaryButton.style.color = CleanColor;
                RebuildMessageList();
                return;
            }

            // Validation throws only on a null set, which is already handled; anything else it finds
            // is data it returns rather than an exception, so no guard is needed here.
            List<ValidationMessage> messages = ClipValidation.ValidateSet(clipSet);
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

            RebuildMessageList();
        }

        private void ToggleExpanded()
        {
            isExpanded = !isExpanded;
            messageList.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
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
                messageButton.style.color =
                    message.severity == ValidationSeverity.Error ? ErrorColor : WarningColor;
                messageButton.style.unityTextAlign = TextAnchor.MiddleLeft;
                messageButton.style.whiteSpace = WhiteSpace.Normal;
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
