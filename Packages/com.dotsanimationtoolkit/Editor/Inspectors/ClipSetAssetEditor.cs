// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="ClipSetAsset"/>: a clip roster with per-clip validation
    /// status from a single <see cref="ClipValidation.ValidateBind"/> call, an entry point for
    /// minting new clips, and a generator that turns clip names into compile-time id constants.
    /// </summary>
    [CustomEditor(typeof(ClipSetAsset))]
    public sealed class ClipSetAssetEditor : UnityEditor.Editor
    {
        private static readonly Color ErrorColor = new Color(0.90f, 0.35f, 0.32f);
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private const string LogPrefix = "[DOTS Animation Toolkit] ";

        private const string GeneratedClassNameSuffix = "ClipIds";
        private const string FallbackSetNameBase = "ClipSet";
        private const string FallbackClipNamePrefix = "Clip";

        private SerializedProperty vatTexturesProperty;
        private SerializedProperty clipsProperty;

        private VisualElement summaryContainer;
        private VisualElement rosterContainer;

        // The one authoritative call's output, kept for the lifetime of one refresh so the summary
        // and the roster read the same pass rather than each triggering their own.
        private readonly List<ValidationMessage> currentMessages = new List<ValidationMessage>();
        private readonly Dictionary<ClipAsset, List<ValidationMessage>> messagesByClip =
            new Dictionary<ClipAsset, List<ValidationMessage>>();

        // Which clip rows are expanded, keyed by the clip's own identity (UnityEngine.Object equality
        // is instance-based - the same assumption ClipValidation.ValidateBind and ClipRegistryBuilder
        // already make of a HashSet<ClipAsset>). Kept across RebuildRoster so revalidating after an
        // edit does not collapse a row the user just opened to read.
        private readonly HashSet<ClipAsset> expandedClips = new HashSet<ClipAsset>();

        /// <summary>
        /// Builds the inspector: identity, the validation summary, the bound rig/VAT fields, and the
        /// clip roster with its two authoring actions.
        /// </summary>
        /// <returns>The root of the inspector's visual tree.</returns>
        public override VisualElement CreateInspectorGUI()
        {
            vatTexturesProperty = serializedObject.FindProperty("vatTextures");
            clipsProperty = serializedObject.FindProperty("clips");

            VisualElement inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            inspectorRoot.Add(BuildSectionHeading("Clip Set"));
            inspectorRoot.Add(BuildIdentityBadge());

            summaryContainer = new VisualElement();
            summaryContainer.style.marginBottom = 6f;
            inspectorRoot.Add(summaryContainer);

            if (vatTexturesProperty != null)
            {
                inspectorRoot.Add(new PropertyField(vatTexturesProperty, "Vat Textures"));
            }

            inspectorRoot.Add(BuildSectionHeading("Clips"));
            rosterContainer = new VisualElement();
            inspectorRoot.Add(rosterContainer);

            Button newClipButton = new Button(CreateNewClipInSet) { text = "New Clip in Set" };
            newClipButton.style.marginTop = 6f;
            inspectorRoot.Add(newClipButton);

            Button generateConstantsButton =
                new Button(GenerateClipIdConstants) { text = "Generate Clip Id Constants" };
            generateConstantsButton.style.marginTop = 2f;
            inspectorRoot.Add(generateConstantsButton);

            // Backstop for edits this inspector did not itself trigger: Undo/Redo, a drag onto the
            // Vat Textures field, or another window (the clip editor) editing a clip asset.
            inspectorRoot.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            inspectorRoot.Bind(serializedObject);

            RefreshValidation();
            return inspectorRoot;
        }

        // -----------------------------------------------------------------------------------
        // Static chrome.
        // -----------------------------------------------------------------------------------

        private static Label BuildSectionHeading(string headingText)
        {
            Label heading = new Label(headingText);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        private Label BuildIdentityBadge()
        {
            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            string identityText = clipSetAsset != null
                ? "Clip set stable id  0x" + clipSetAsset.StableId.ToString("X16")
                : "Clip set stable id  (unavailable)";
            Label badge = new Label(identityText);
            badge.selection.isSelectable = true;
            badge.style.marginBottom = 4f;
            badge.style.opacity = 0.7f;
            return badge;
        }

        // -----------------------------------------------------------------------------------
        // Validation: one call, two renderings.
        // -----------------------------------------------------------------------------------

        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            RefreshValidation();
        }

        // Walks every clip, track and key in the set, so this must run once per meaningful event
        // (built, tracked edit, button handler) and never from a layout or repaint callback.
        private void RefreshValidation()
        {
            currentMessages.Clear();
            messagesByClip.Clear();

            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset != null)
            {
                // Unbound: a set names no rig, so the binding rules have nothing to judge against
                // and stay silent. What is left is everything a set can answer alone — clip-local
                // rules, id uniqueness across the set, and VAT coverage.
                List<ValidationMessage> validationMessages =
                    ClipValidation.ValidateBind(null, new ClipSetAsset[] { clipSetAsset });
                for (int messageIndex = 0; messageIndex < validationMessages.Count; messageIndex++)
                {
                    ValidationMessage message = validationMessages[messageIndex];
                    currentMessages.Add(message);

                    ClipAsset clipContext = message.assetContext as ClipAsset;
                    if (clipContext == null)
                    {
                        continue;
                    }
                    List<ValidationMessage> clipMessages;
                    if (!messagesByClip.TryGetValue(clipContext, out clipMessages))
                    {
                        clipMessages = new List<ValidationMessage>();
                        messagesByClip.Add(clipContext, clipMessages);
                    }
                    clipMessages.Add(message);
                }
            }

            RebuildSummary();
            RebuildRoster();
        }

        /// <summary>
        /// Renders the total counts and the exact bakeability statement
        /// (<see cref="ClipRegistryBuilder.Build"/> throws on any error-severity finding), plus
        /// whatever findings belong to no single clip.
        /// </summary>
        private void RebuildSummary()
        {
            summaryContainer.Clear();

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
            bool isBakeable = errorCount == 0;

            Label countsLabel = new Label(
                errorCount.ToString() + " error(s), " + warningCount.ToString() + " warning(s)");
            countsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            countsLabel.style.color = errorCount > 0
                ? ErrorColor
                : (warningCount > 0 ? WarningColor : CleanColor);
            summaryContainer.Add(countsLabel);

            Label bakeableLabel = new Label(isBakeable
                ? "Bakeable - ClipRegistryBuilder.Build will succeed."
                : "NOT bakeable - ClipRegistryBuilder.Build throws ClipValidationException until " +
                  "every error above is fixed. Warnings do not block the bake.");
            bakeableLabel.style.color = isBakeable ? CleanColor : ErrorColor;
            bakeableLabel.style.whiteSpace = WhiteSpace.Normal;
            summaryContainer.Add(bakeableLabel);

            VisualElement unattributedList = BuildUnattributedFindingsList();
            if (unattributedList != null)
            {
                summaryContainer.Add(unattributedList);
            }
        }

        /// <summary>
        /// Findings whose <see cref="ValidationMessage.assetContext"/> is not one of the set's own
        /// clips - rig-level findings, the set itself, or the VAT texture set. Without this block
        /// those findings would count toward the totals above but never appear anywhere clickable,
        /// which is exactly the kind of "the bake fails and nothing in the UI says why" gap this
        /// inspector exists to close.
        /// </summary>
        /// <returns>Null when there is nothing to show.</returns>
        private VisualElement BuildUnattributedFindingsList()
        {
            List<ValidationMessage> unattributedMessages = new List<ValidationMessage>();
            for (int messageIndex = 0; messageIndex < currentMessages.Count; messageIndex++)
            {
                ValidationMessage message = currentMessages[messageIndex];
                if (!(message.assetContext is ClipAsset))
                {
                    unattributedMessages.Add(message);
                }
            }
            if (unattributedMessages.Count == 0)
            {
                return null;
            }

            VisualElement section = new VisualElement();
            section.style.marginTop = 4f;

            Label heading = new Label("Other findings (rig, set, or VAT texture set):");
            heading.style.opacity = 0.8f;
            section.Add(heading);

            for (int messageIndex = 0; messageIndex < unattributedMessages.Count; messageIndex++)
            {
                section.Add(BuildMessageButton(unattributedMessages[messageIndex]));
            }
            return section;
        }

        private Button BuildMessageButton(ValidationMessage message)
        {
            Button messageButton = new Button(() => SelectAndPing(message.assetContext))
            {
                text = message.code.ToString() + "  " + message.text
            };
            messageButton.style.color =
                message.severity == ValidationSeverity.Error ? ErrorColor : WarningColor;
            messageButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            messageButton.style.whiteSpace = WhiteSpace.Normal;
            return messageButton;
        }

        // A null context is left alone — a finding about a missing reference legitimately has no
        // asset to point at, so doing nothing is correct, not a bug.
        private static void SelectAndPing(Object contextAsset)
        {
            if (contextAsset == null)
            {
                return;
            }
            Selection.activeObject = contextAsset;
            EditorGUIUtility.PingObject(contextAsset);
        }

        // -----------------------------------------------------------------------------------
        // Clip roster.
        // -----------------------------------------------------------------------------------

        // A full rebuild, not an incremental diff: each row's SerializedProperty handle points at
        // an array index, and inserting or removing a clip re-points every handle after that site.
        private void RebuildRoster()
        {
            rosterContainer.Clear();

            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset == null || clipsProperty == null)
            {
                return;
            }

            List<ClipAsset> clips = clipSetAsset.clips;
            int clipCount = clips != null ? clips.Count : 0;
            if (clipCount == 0)
            {
                Label emptyLabel = new Label("No clips in this set yet. Use New Clip in Set below.");
                emptyLabel.style.opacity = 0.7f;
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                rosterContainer.Add(emptyLabel);
                return;
            }

            for (int clipIndex = 0; clipIndex < clipCount; clipIndex++)
            {
                rosterContainer.Add(BuildClipRow(clipIndex, clips[clipIndex]));
            }

            // The rows were created after the root was bound, so they carry no bindings yet.
            rosterContainer.Bind(serializedObject);
        }

        private VisualElement BuildClipRow(int clipIndex, ClipAsset clip)
        {
            VisualElement rowContainer = new VisualElement();
            rowContainer.style.marginTop = 4f;
            rowContainer.style.paddingLeft = 6f;
            rowContainer.style.paddingRight = 6f;
            rowContainer.style.paddingTop = 3f;
            rowContainer.style.paddingBottom = 4f;
            rowContainer.style.borderLeftWidth = 2f;
            rowContainer.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.5f, 0.6f));

            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;

            SerializedProperty clipElementProperty = clipsProperty.GetArrayElementAtIndex(clipIndex);
            PropertyField clipField = new PropertyField(clipElementProperty, "Clip");
            clipField.style.flexGrow = 1f;
            headerRow.Add(clipField);

            if (clip == null)
            {
                Label emptySlotLabel = new Label("(empty slot)");
                emptySlotLabel.style.opacity = 0.6f;
                emptySlotLabel.style.marginLeft = 6f;
                headerRow.Add(emptySlotLabel);
                rowContainer.Add(headerRow);
                rowContainer.Add(BuildRemoveButton(clipIndex));
                return rowContainer;
            }

            Label idLabel = new Label("id 0x" + clip.stableId.ToString("X16"));
            idLabel.selection.isSelectable = true;
            idLabel.style.opacity = 0.7f;
            idLabel.style.marginLeft = 6f;
            idLabel.tooltip = "Stable clip id, the key ClipRegistryBuilder bakes into the registry's " +
                "binary-searchable sortedClipIds array.";
            headerRow.Add(idLabel);

            List<ValidationMessage> clipMessages;
            messagesByClip.TryGetValue(clip, out clipMessages);
            int clipErrorCount = 0;
            int clipWarningCount = 0;
            if (clipMessages != null)
            {
                for (int messageIndex = 0; messageIndex < clipMessages.Count; messageIndex++)
                {
                    if (clipMessages[messageIndex].severity == ValidationSeverity.Error)
                    {
                        clipErrorCount++;
                    }
                    else
                    {
                        clipWarningCount++;
                    }
                }
            }

            VisualElement messageList = new VisualElement();
            messageList.style.marginLeft = 8f;
            messageList.style.marginTop = 2f;
            messageList.style.display =
                expandedClips.Contains(clip) ? DisplayStyle.Flex : DisplayStyle.None;
            if (clipMessages != null && clipMessages.Count > 0)
            {
                for (int messageIndex = 0; messageIndex < clipMessages.Count; messageIndex++)
                {
                    messageList.Add(BuildMessageButton(clipMessages[messageIndex]));
                }
            }
            else
            {
                messageList.Add(new Label("Nothing to report."));
            }

            // Clicking the status button both pings the clip asset (spec: "clicking a row pings the
            // asset") and toggles the expanded message list (spec: "expanding a row lists that
            // clip's messages") - one click does both rather than splitting them across two controls
            // a user has to discover separately.
            Button statusButton = new Button(() =>
            {
                SelectAndPing(clip);
                if (!expandedClips.Add(clip))
                {
                    expandedClips.Remove(clip);
                }
                messageList.style.display =
                    expandedClips.Contains(clip) ? DisplayStyle.Flex : DisplayStyle.None;
            })
            {
                text = BuildStatusText(clipErrorCount, clipWarningCount)
            };
            statusButton.style.marginLeft = 6f;
            statusButton.style.color = clipErrorCount > 0
                ? ErrorColor
                : (clipWarningCount > 0 ? WarningColor : CleanColor);
            headerRow.Add(statusButton);

            rowContainer.Add(headerRow);
            rowContainer.Add(messageList);
            rowContainer.Add(BuildRemoveButton(clipIndex));
            return rowContainer;
        }

        private static string BuildStatusText(int errorCount, int warningCount)
        {
            if (errorCount > 0)
            {
                return errorCount.ToString() + " err  " + warningCount.ToString() + " warn";
            }
            if (warningCount > 0)
            {
                return warningCount.ToString() + " warn";
            }
            return "Clean";
        }

        private Button BuildRemoveButton(int clipIndex)
        {
            Button removeButton = new Button(() => RemoveClipAt(clipIndex)) { text = "Remove From Set" };
            removeButton.style.marginTop = 3f;
            return removeButton;
        }

        /// <summary>
        /// Removes one entry from <see cref="ClipSetAsset.clips"/>, leaving the clip asset itself on
        /// disk - this only un-registers it from the set.
        /// </summary>
        private void RemoveClipAt(int clipIndex)
        {
            if (clipsProperty == null || clipIndex < 0 || clipIndex >= clipsProperty.arraySize)
            {
                return;
            }

            serializedObject.Update();
            SerializedProperty elementProperty = clipsProperty.GetArrayElementAtIndex(clipIndex);
            // DeleteArrayElementAtIndex on a non-null object reference only nulls it on the first
            // call; a second call at the same index actually removes the now-empty slot.
            bool wasNonNullReference = elementProperty.objectReferenceValue != null;
            clipsProperty.DeleteArrayElementAtIndex(clipIndex);

            if (wasNonNullReference
                && clipIndex < clipsProperty.arraySize
                && clipsProperty.GetArrayElementAtIndex(clipIndex).objectReferenceValue == null)
            {
                clipsProperty.DeleteArrayElementAtIndex(clipIndex);
            }

            serializedObject.ApplyModifiedProperties();
            RefreshValidation();
        }

        // -----------------------------------------------------------------------------------
        // New Clip in Set.
        // -----------------------------------------------------------------------------------

        /// <summary>Creates a fresh <see cref="ClipAsset"/> in the set, then selects and pings it.</summary>
        private void CreateNewClipInSet()
        {
            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset == null)
            {
                return;
            }

            ClipAsset newClip = ClipAssetUtility.CreateClipInSet(clipSetAsset);
            if (newClip == null)
            {
                return;
            }

            // The utility appended through its own SerializedObject, so this one is a version behind.
            serializedObject.Update();

            Selection.activeObject = newClip;
            EditorGUIUtility.PingObject(newClip);

            RefreshValidation();
        }

        // -----------------------------------------------------------------------------------
        // Generate Clip Id Constants.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Writes a C# file of <c>public const ulong</c> clip id constants so game code can write
        /// <c>MyClipIds.Walk</c> instead of a bare 64-bit literal.
        /// </summary>
        private void GenerateClipIdConstants()
        {
            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset == null)
            {
                return;
            }

            string defaultClassName = BuildGeneratedClassName(clipSetAsset.name);
            // A save dialog, not a fixed location: this package cannot know which assembly in the
            // host project should own the generated constants.
            string chosenFilePath = EditorUtility.SaveFilePanel(
                "Generate Clip Id Constants", null, defaultClassName,
                ConstantsGenerator.GeneratedFileExtension);
            if (string.IsNullOrEmpty(chosenFilePath))
            {
                return;
            }

            string generatedSource = BuildClipIdConstantsSource(clipSetAsset, defaultClassName);
            File.WriteAllText(chosenFilePath, generatedSource);

            // The file was written with a plain File API, bypassing the AssetDatabase entirely, so a
            // Refresh is what makes Unity notice it exists (the same reasoning several Entities
            // package routines apply after writing project files outside AssetDatabase.CreateAsset).
            AssetDatabase.Refresh();

            Debug.Log(
                LogPrefix + "Wrote clip id constants for '" + clipSetAsset.name + "' to '" +
                chosenFilePath + "'.",
                clipSetAsset);
        }

        private static string BuildGeneratedClassName(string clipSetName)
        {
            string sanitizedSetName = ConstantsGenerator.SanitizeIdentifier(clipSetName);
            if (string.IsNullOrEmpty(sanitizedSetName))
            {
                sanitizedSetName = FallbackSetNameBase;
            }
            return ConstantsGenerator.EscapeReservedKeyword(sanitizedSetName + GeneratedClassNameSuffix);
        }

        // Builds the generated source: a header, then one public const ulong per clip. Name
        // collisions and reserved words are resolved the same deterministic way ConstantsGenerator
        // uses for a vocabulary — see that type for the sanitize/dedupe/escape rules.
        private static string BuildClipIdConstantsSource(ClipSetAsset clipSetAsset, string className)
        {
            StringBuilder source = new StringBuilder();
            source.Append("// <auto-generated>\n");
            source.Append("// Generated by the DOTS Animation Toolkit clip set inspector.\n");
            source.Append(
                "// Source clip set: '" + ConstantsGenerator.EscapeXmlDocText(clipSetAsset.name)
                + "'.\n");
            source.Append("// Regenerating this file overwrites it; do not hand-edit it.\n");
            source.Append("// </auto-generated>\n\n");

            source.Append("public static class " + className + "\n");
            source.Append("{\n");

            Dictionary<string, int> usedNameCounts = new Dictionary<string, int>();
            List<ClipAsset> clips = clipSetAsset.clips;
            int clipCount = clips != null ? clips.Count : 0;
            for (int clipIndex = 0; clipIndex < clipCount; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                string baseIdentifierName = ConstantsGenerator.SanitizeIdentifier(clip.name);
                if (string.IsNullOrEmpty(baseIdentifierName))
                {
                    baseIdentifierName = FallbackClipNamePrefix + (clipIndex + 1).ToString();
                }

                string uniqueIdentifierName =
                    ConstantsGenerator.MakeUniqueName(baseIdentifierName, usedNameCounts);
                uniqueIdentifierName = ConstantsGenerator.EscapeReservedKeyword(uniqueIdentifierName);

                source.Append(
                    "    /// <summary>Clip '" + ConstantsGenerator.EscapeXmlDocText(clip.name)
                    + "'.</summary>\n");
                source.Append(
                    "    public const ulong " + uniqueIdentifierName + " = 0x" +
                    clip.stableId.ToString("X16") + "UL;\n");
            }

            source.Append("}\n");
            return source.ToString();
        }
    }
}
