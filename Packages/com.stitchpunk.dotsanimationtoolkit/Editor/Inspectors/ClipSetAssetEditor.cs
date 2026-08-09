// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The custom inspector for <see cref="ClipSetAsset"/> (architecture section 7.1): a clip
    /// roster with per-clip validation status, an entry point for minting new clips into the set,
    /// and a generator that turns clip names into compile-time constants so game code never
    /// carries a magic <c>ulong</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This inspector never decides what is valid — same discipline as
    /// <see cref="ValidationBadgeElement"/>.</strong> Every finding shown here, in the top summary
    /// and in a clip row's expanded list, comes from a single <see cref="ClipValidation.ValidateSet"/>
    /// call; nothing here re-implements a rule. The roster's contribution is purely presentational:
    /// it buckets the one authoritative message list by <see cref="ValidationMessage.assetContext"/>
    /// so a clip's own row shows only what is about that clip, while rig-, set- and VAT-texture-set
    /// scoped findings (V05's set-level half, V06, V07, V08, V13) surface in an "Other findings"
    /// block instead of being silently dropped because no clip row exists to hold them.
    /// </para>
    /// <para>
    /// <strong>Validation runs once per meaningful event, never per repaint.</strong>
    /// <see cref="ClipValidation.ValidateSet"/> walks the rig, every clip, every track and every key
    /// in the set; at sixty repaints a second that cost is invisible on a five-clip demo set and
    /// crippling on a real roster. <see cref="RefreshValidation"/> is therefore called exactly from
    /// three places: once when the inspector is built, once from
    /// <see cref="VisualElement.TrackSerializedObjectValue"/> so external edits (Undo/Redo, a drag
    /// onto the Rig field, another window's edit to a clip) are picked up, and once explicitly at the
    /// end of each button handler in this file — mirroring <c>RigAssetEditor.AddSocket</c>, which
    /// does not wait for the tracked callback to notice its own edit before rebuilding. Nothing here
    /// calls it from a layout, geometry, or paint callback.
    /// </para>
    /// <para>
    /// UI Toolkit only, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>: this type overrides
    /// <see cref="UnityEditor.Editor.CreateInspectorGUI"/> and never the immediate-mode entry point.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(ClipSetAsset))]
    public sealed class ClipSetAssetEditor : UnityEditor.Editor
    {
        private static readonly Color ErrorColor = new Color(0.90f, 0.35f, 0.32f);
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private const string LogPrefix = "[DOTS Animation Toolkit] ";

        private const string NewClipAssetBaseName = "NewClip";
        private const string AssetExtension = ".asset";
        private const string NewClipUndoActionName = "Create Clip In Set";

        private const string GeneratedClassNameSuffix = "ClipIds";
        private const string FallbackSetNameBase = "ClipSet";
        private const string FallbackClipNamePrefix = "Clip";
        private const string GeneratedFileExtension = "cs";

        // The 77 reserved C# keywords. Contextual keywords (var, async, await, yield, partial,
        // dynamic, nameof, where, when, ...) are legal identifiers as-is and are deliberately absent.
        private static readonly HashSet<string> ReservedCSharpKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        private SerializedProperty rigProperty;
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
        // is instance-based - the same assumption ClipValidation.ValidateSet and ClipRegistryBuilder
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
            rigProperty = serializedObject.FindProperty("rig");
            vatTexturesProperty = serializedObject.FindProperty("vatTextures");
            clipsProperty = serializedObject.FindProperty("clips");

            VisualElement inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            inspectorRoot.Add(BuildSectionHeading("Clip Set"));
            inspectorRoot.Add(BuildIdentityBadge());

            summaryContainer = new VisualElement();
            summaryContainer.style.marginBottom = 6f;
            inspectorRoot.Add(summaryContainer);

            if (rigProperty != null)
            {
                inspectorRoot.Add(new PropertyField(rigProperty, "Rig"));
            }
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
            // Rig or Vat Textures field, or another window (the clip editor) editing a clip asset.
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

        /// <summary>
        /// Runs <see cref="ClipValidation.ValidateSet"/> once, buckets the result by clip, and
        /// repaints both the summary and the roster from that one pass.
        /// </summary>
        private void RefreshValidation()
        {
            currentMessages.Clear();
            messagesByClip.Clear();

            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset != null)
            {
                List<ValidationMessage> validationMessages = ClipValidation.ValidateSet(clipSetAsset);
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

        /// <summary>Selects and pings a finding's context asset; a null context is left alone.</summary>
        /// <remarks>
        /// Mirrors <c>ValidationBadgeElement.SelectContext</c> exactly: a finding about a missing
        /// reference legitimately has no asset to point at, so doing nothing is correct, not a bug.
        /// </remarks>
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

        /// <summary>
        /// Tears down and rebuilds every clip row. A full rebuild rather than an incremental diff,
        /// for the same reason <c>RigAssetEditor.RebuildSocketRows</c> gives: every row's
        /// <see cref="SerializedProperty"/> handle points at an array index, and inserting or
        /// removing a clip re-points every handle after the edit site, so a partial update would
        /// leave surviving rows editing their neighbour's slot. A set's clip count is small enough
        /// that rebuilding costs nothing next to the validation pass that already ran.
        /// </summary>
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
        /// <remarks>
        /// <see cref="SerializedProperty.DeleteArrayElementAtIndex"/> on an array of
        /// <see cref="Object"/> references only nulls a non-null element on its first call; a second
        /// call at the same index removes the now-null slot. <c>RigAssetEditor.RemoveSocket</c>'s own
        /// remarks document this exact quirk for a by-value array and note it does not apply there -
        /// it very much applies here, since <see cref="ClipSetAsset.clips"/> is a
        /// <c>List&lt;ClipAsset&gt;</c> of object references. Skipping the second call would leave a
        /// null entry behind instead of shortening the list, which is silently wrong: the roster would
        /// show one fewer clip than <c>clipsProperty.arraySize</c> actually holds.
        /// </remarks>
        private void RemoveClipAt(int clipIndex)
        {
            if (clipsProperty == null || clipIndex < 0 || clipIndex >= clipsProperty.arraySize)
            {
                return;
            }

            serializedObject.Update();
            SerializedProperty elementProperty = clipsProperty.GetArrayElementAtIndex(clipIndex);
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

        /// <summary>
        /// Creates a fresh <see cref="ClipAsset"/> beside the set on disk, assigns the set's rig, and
        /// appends it to <see cref="ClipSetAsset.clips"/> as one undo step.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The physical asset write (<see cref="AssetDatabase.CreateAsset"/>) is not itself something
        /// Ctrl+Z reverses - <c>MirrorClipUtility.CreateMirroredCopy</c> makes the same choice and
        /// does not attempt to wrap it in an undo group either. What genuinely is undoable, and what
        /// this wraps in one named <see cref="Undo"/> group, is the append to
        /// <see cref="ClipSetAsset.clips"/>: driven entirely through
        /// <see cref="SerializedProperty"/> and <see cref="SerializedObject.ApplyModifiedProperties"/>,
        /// which - per <c>RigAssetEditor.AddSocket</c>'s own precedent - already records one Undo
        /// entry per <c>ApplyModifiedProperties</c> call with no explicit <c>Undo.RecordObject</c>
        /// needed. The group name makes that one entry read as "Create Clip In Set" in the Undo
        /// History window instead of a generic property-change label.
        /// </para>
        /// <para>
        /// The new clip mints its own stable id via the internal
        /// <see cref="ClipAsset.EnsureStableIds"/> (this assembly is granted access; see
        /// <c>Authoring/AssemblyInfo.cs</c>) rather than relying solely on the asset's own
        /// <c>Awake</c>/<c>OnValidate</c> callbacks, for the same reason
        /// <c>MirrorClipUtility.CreateMirroredCopy</c> calls it explicitly: it is idempotent, costs
        /// nothing, and makes the guarantee local to this method.
        /// </para>
        /// </remarks>
        private void CreateNewClipInSet()
        {
            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset == null)
            {
                return;
            }

            string setAssetPath = AssetDatabase.GetAssetPath(clipSetAsset);
            if (string.IsNullOrEmpty(setAssetPath))
            {
                Debug.LogError(
                    LogPrefix + "Clip set '" + clipSetAsset.name + "' is not saved as an asset yet, " +
                    "so a new clip has nowhere to be written. Save the set first.",
                    clipSetAsset);
                return;
            }

            string containingFolderPath = ExtractContainingFolderPath(setAssetPath);
            if (string.IsNullOrEmpty(containingFolderPath))
            {
                Debug.LogError(
                    LogPrefix + "Could not resolve the folder containing clip set '" +
                    clipSetAsset.name + "' from its asset path '" + setAssetPath + "'.",
                    clipSetAsset);
                return;
            }

            string candidateAssetPath = containingFolderPath + "/" + NewClipAssetBaseName + AssetExtension;
            string uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(candidateAssetPath);

            ClipAsset newClip = ScriptableObject.CreateInstance<ClipAsset>();
            newClip.rig = clipSetAsset.rig;
            newClip.EnsureStableIds();
            newClip.name = ExtractAssetName(uniqueAssetPath);

            AssetDatabase.CreateAsset(newClip, uniqueAssetPath);
            AssetDatabase.SaveAssets();

            // The minted id is on disk now, so the "not yet persisted" report is discharged - the
            // same ordering CreateMirroredCopy uses and for the same reason.
            newClip.MarkStableIdPersisted();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(NewClipUndoActionName);

            serializedObject.Update();
            int newElementIndex = clipsProperty.arraySize;
            clipsProperty.InsertArrayElementAtIndex(newElementIndex);
            clipsProperty.GetArrayElementAtIndex(newElementIndex).objectReferenceValue = newClip;
            serializedObject.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeObject = newClip;
            EditorGUIUtility.PingObject(newClip);

            RefreshValidation();
        }

        private static string ExtractContainingFolderPath(string assetPath)
        {
            int lastSeparatorIndex = assetPath.LastIndexOf('/');
            return lastSeparatorIndex > 0 ? assetPath.Substring(0, lastSeparatorIndex) : string.Empty;
        }

        /// <summary>Returns the file name of <paramref name="assetPath"/> without its extension.</summary>
        private static string ExtractAssetName(string assetPath)
        {
            int lastSeparatorIndex = assetPath.LastIndexOf('/');
            string fileName = lastSeparatorIndex >= 0
                ? assetPath.Substring(lastSeparatorIndex + 1)
                : assetPath;
            int extensionIndex = fileName.LastIndexOf('.');
            return extensionIndex > 0 ? fileName.Substring(0, extensionIndex) : fileName;
        }

        // -----------------------------------------------------------------------------------
        // Generate Clip Id Constants.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Writes a C# file of <c>public const ulong</c> clip id constants so game code can write
        /// <c>MyClipIds.Walk</c> instead of a bare 64-bit literal.
        /// </summary>
        /// <remarks>
        /// The write target is chosen through <see cref="EditorUtility.SaveFilePanel"/> rather than a
        /// fixed location, because this package cannot know - and must not guess - which assembly in
        /// the host project is meant to own the generated constants. An empty return means the user
        /// cancelled the dialog, which is the documented contract of that API.
        /// </remarks>
        private void GenerateClipIdConstants()
        {
            ClipSetAsset clipSetAsset = target as ClipSetAsset;
            if (clipSetAsset == null)
            {
                return;
            }

            string defaultClassName = BuildGeneratedClassName(clipSetAsset.name);
            string chosenFilePath = EditorUtility.SaveFilePanel(
                "Generate Clip Id Constants", null, defaultClassName, GeneratedFileExtension);
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
            string sanitizedSetName = SanitizeIdentifier(clipSetName);
            if (string.IsNullOrEmpty(sanitizedSetName))
            {
                sanitizedSetName = FallbackSetNameBase;
            }
            return EscapeReservedKeyword(sanitizedSetName + GeneratedClassNameSuffix);
        }

        /// <summary>
        /// Builds the full generated source text: a generated-file header, then one
        /// <c>public const ulong</c> per clip.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Every edge case in the clip name is resolved deterministically, not hopefully.</strong>
        /// A name is first reduced to <c>[A-Za-z0-9_]</c> by <see cref="SanitizeIdentifier"/>
        /// (punctuation and spaces become underscores, non-ASCII collapses to underscores too - always
        /// a legal identifier character set, never a guess). A name that sanitizes to nothing (empty,
        /// or entirely punctuation) falls back to <c>Clip&lt;position&gt;</c>, keyed by the clip's
        /// authoring-order position so it can never collide with itself. A name that starts with a
        /// digit is prefixed with an underscore inside <see cref="SanitizeIdentifier"/>. Two clips
        /// whose names sanitize to the same identifier - whether they were literal duplicates or only
        /// became identical after punctuation was stripped - are disambiguated by
        /// <see cref="MakeUniqueName"/> with a deterministic <c>_2</c>, <c>_3</c>, ... suffix, always
        /// checked against every name already emitted so a suffix can never itself collide. A name
        /// that happens to be a reserved word (a clip called "class") is escaped with the verbatim
        /// identifier prefix <c>@</c>, which is always legal C# regardless of which keyword it is.
        /// </para>
        /// <para>
        /// The clip's original, un-sanitized name still appears as the constant's XML doc summary, so
        /// the mapping from constant back to authored name survives even when they differ - but that
        /// text is scrubbed of line breaks and XML-significant characters first
        /// (<see cref="EscapeXmlDocText"/>), because an embedded newline would split a single
        /// <c>///</c> line comment into a second, uncommented line of raw text - which is invalid C#,
        /// not merely ugly. Doc comments are emitted as <c>///</c> lines rather than a <c>/* */</c>
        /// block specifically so a clip name containing the literal text <c>*/</c> can never
        /// prematurely close the comment.
        /// </para>
        /// </remarks>
        private static string BuildClipIdConstantsSource(ClipSetAsset clipSetAsset, string className)
        {
            StringBuilder source = new StringBuilder();
            source.Append("// <auto-generated>\n");
            source.Append("// Generated by the DOTS Animation Toolkit clip set inspector.\n");
            source.Append("// Source clip set: '" + EscapeXmlDocText(clipSetAsset.name) + "'.\n");
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

                string baseIdentifierName = SanitizeIdentifier(clip.name);
                if (string.IsNullOrEmpty(baseIdentifierName))
                {
                    baseIdentifierName = FallbackClipNamePrefix + (clipIndex + 1).ToString();
                }

                string uniqueIdentifierName = MakeUniqueName(baseIdentifierName, usedNameCounts);
                uniqueIdentifierName = EscapeReservedKeyword(uniqueIdentifierName);

                source.Append("    /// <summary>Clip '" + EscapeXmlDocText(clip.name) + "'.</summary>\n");
                source.Append(
                    "    public const ulong " + uniqueIdentifierName + " = 0x" +
                    clip.stableId.ToString("X16") + "UL;\n");
            }

            source.Append("}\n");
            return source.ToString();
        }

        /// <summary>
        /// Reduces <paramref name="rawName"/> to a legal C# identifier's character set. Every
        /// character outside <c>[A-Za-z0-9_]</c> becomes an underscore, and a leading digit is
        /// prefixed with one - both deterministic, both always producing a syntactically legal
        /// identifier (or an empty string when nothing survives, left for the caller to fall back on).
        /// </summary>
        private static string SanitizeIdentifier(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return string.Empty;
            }

            StringBuilder sanitized = new StringBuilder(rawName.Length);
            for (int charIndex = 0; charIndex < rawName.Length; charIndex++)
            {
                char currentCharacter = rawName[charIndex];
                bool isLegalIdentifierCharacter =
                    (currentCharacter >= 'a' && currentCharacter <= 'z')
                    || (currentCharacter >= 'A' && currentCharacter <= 'Z')
                    || (currentCharacter >= '0' && currentCharacter <= '9')
                    || currentCharacter == '_';
                sanitized.Append(isLegalIdentifierCharacter ? currentCharacter : '_');
            }

            if (sanitized.Length == 0)
            {
                return string.Empty;
            }
            if (sanitized[0] >= '0' && sanitized[0] <= '9')
            {
                sanitized.Insert(0, '_');
            }
            return sanitized.ToString();
        }

        /// <summary>
        /// Returns <paramref name="baseName"/> unchanged the first time it is seen; every later call
        /// with the same base gets a <c>_2</c>, <c>_3</c>, ... suffix, incrementing past any suffix
        /// that a distinct clip already produced, so the returned name is always unused so far.
        /// </summary>
        private static string MakeUniqueName(string baseName, Dictionary<string, int> usedNameCounts)
        {
            if (!usedNameCounts.ContainsKey(baseName))
            {
                usedNameCounts.Add(baseName, 0);
                return baseName;
            }

            int suffix = usedNameCounts[baseName];
            string candidateName;
            do
            {
                suffix++;
                candidateName = baseName + "_" + suffix.ToString();
            }
            while (usedNameCounts.ContainsKey(candidateName));

            usedNameCounts[baseName] = suffix;
            usedNameCounts.Add(candidateName, 0);
            return candidateName;
        }

        private static string EscapeReservedKeyword(string identifierName)
        {
            return ReservedCSharpKeywords.Contains(identifierName) ? "@" + identifierName : identifierName;
        }

        /// <summary>
        /// Makes raw clip-name text safe to sit inside a single <c>///</c> XML doc comment line: line
        /// breaks are folded to spaces (an embedded newline would otherwise end the comment mid-name
        /// and leave the rest as bare, invalid code), and the three XML-significant characters are
        /// entity-escaped.
        /// </summary>
        private static string EscapeXmlDocText(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
            {
                return string.Empty;
            }
            string singleLineText = rawText.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            return singleLineText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
