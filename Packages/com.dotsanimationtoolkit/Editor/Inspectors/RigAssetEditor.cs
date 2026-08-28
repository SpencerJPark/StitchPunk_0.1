// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The custom inspector for <see cref="RigAsset"/> (architecture section 7.1). Its reason to
    /// exist is the socket list: sockets are the one part of a rig the default inspector cannot
    /// author safely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The bug this closes.</strong> In the default inspector a bone socket's
    /// <c>boneName</c> is a free-text field, and a rig-target socket's <c>targetId</c> is a raw
    /// unsigned integer. Both are unverifiable at the point of authoring, and both fail silently:
    /// the VAT bake logs one warning and then bakes a socket pinned to the actor origin, so the
    /// symptom the user actually sees is a sword hovering at a character's feet, hours later, in a
    /// different module. Replacing the two free-form fields with dropdowns built from real data —
    /// the rig's own targets, and the bones of a user-assigned source hierarchy — removes the whole
    /// class of typo rather than reporting it downstream.
    /// </para>
    /// <para>
    /// <strong>Everything that can be a bound <see cref="PropertyField"/> is one.</strong> Binding
    /// through <see cref="SerializedObject"/> buys Undo, dirtying, and prefab-override handling for
    /// free; hand-rolled fields that write the asset directly buy none of those, and the audit that
    /// preceded the package found exactly that defect in the host's own editors. Only the two
    /// dropdowns are hand-driven, because the value a user picks (a bone name, a target row) is not
    /// the value stored (a string, an id) — and even those write through
    /// <see cref="SerializedProperty"/> and <see cref="SerializedObject.ApplyModifiedProperties"/>
    /// so they land on the same Undo stack as everything else.
    /// </para>
    /// <para>
    /// UI Toolkit only, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>: this type
    /// overrides <see cref="UnityEditor.Editor.CreateInspectorGUI"/> and never the immediate-mode
    /// entry point.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(RigAsset))]
    public sealed class RigAssetEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Prefix of the per-asset editor preference that remembers the bone name source.
        /// </summary>
        /// <remarks>
        /// Keyed by the rig's asset GUID rather than its path, so moving or renaming the rig keeps
        /// the association. Prefixed with the full type name because
        /// <see cref="EditorPrefs"/> is a single global namespace shared with every other tool the
        /// user has installed.
        /// </remarks>
        private const string BoneNameSourcePreferenceKeyPrefix =
            "DotsAnimationToolkit.RigAssetEditor.boneNameSource.";

        private const string NoSelectionChoiceLabel = "(none)";
        private const string UnnamedTargetChoiceLabel = "(unnamed target)";

        private SerializedProperty targetsProperty;
        private SerializedProperty layersProperty;
        private SerializedProperty mirrorPairsProperty;
        private SerializedProperty socketsProperty;
        private SerializedProperty billboardRootsProperty;
        private SerializedProperty ragdollSettingsProperty;
        private SerializedProperty ragdollBodiesProperty;

        private VisualElement inspectorRoot;
        private VisualElement socketRowContainer;
        private VisualElement ragdollBadgeContainer;
        private ObjectField boneNameSourceField;

        private GameObject boneNameSource;

        // Choice label -> stored value, in the order the dropdown shows them. Two parallel lists
        // rather than a dictionary because the dropdown needs the labels as an ordered List<string>
        // anyway, and the lists are a handful of entries long.
        private readonly List<string> targetChoiceLabels = new List<string>();
        private readonly List<uint> targetChoiceIds = new List<uint>();
        private readonly List<string> boneChoiceLabels = new List<string>();
        private readonly HashSet<string> boneNameLookup = new HashSet<string>();

        private readonly List<SocketRowElements> socketRows = new List<SocketRowElements>();

        // -----------------------------------------------------------------------------------
        // Target tags (Phase E target-tags spec §4.2, E2).
        // -----------------------------------------------------------------------------------

        private const string NoTagChoiceLabel = "(none)";

        private VisualElement targetTagRowContainer;
        private VisualElement targetTagBadgeContainer;

        private readonly List<TargetTagRowElements> targetTagRows = new List<TargetTagRowElements>();
        private int builtTargetTagCount = -1;

        // Rebuild triggers. Rebuilding the rows on every serialized change would steal focus from
        // whatever text field the user is typing in, so the tracked callback only rebuilds when the
        // shape of the data changed — a socket added or removed, or a target renamed, re-identified,
        // added, or removed. Anything else just refreshes text and visibility in place.
        private int builtSocketCount;
        private string builtTargetSignature = string.Empty;

        /// <summary>
        /// Builds the inspector: the three ordinary rig lists, then the socket authoring section.
        /// </summary>
        /// <returns>The root of the inspector's visual tree.</returns>
        /// <remarks>
        /// <see cref="BindingExtensions.Bind(VisualElement, SerializedObject)"/> is called on the
        /// root explicitly rather than left to the hosting inspector element. Binding an already
        /// bound tree is idempotent, whereas an unbound tree renders every
        /// <see cref="PropertyField"/> empty — a failure mode that looks like a broken asset rather
        /// than a broken editor, and is worth one redundant call to make impossible.
        /// </remarks>
        public override VisualElement CreateInspectorGUI()
        {
            targetsProperty = serializedObject.FindProperty("targets");
            layersProperty = serializedObject.FindProperty("layers");
            mirrorPairsProperty = serializedObject.FindProperty("mirrorPairs");
            socketsProperty = serializedObject.FindProperty("sockets");
            billboardRootsProperty = serializedObject.FindProperty("billboardRoots");
            ragdollSettingsProperty = serializedObject.FindProperty("ragdollSettings");
            ragdollBodiesProperty = serializedObject.FindProperty("ragdollBodies");

            inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            inspectorRoot.Add(BuildSectionHeading("Rig"));
            inspectorRoot.Add(BuildIdentityBadge());

            if (targetsProperty != null)
            {
                inspectorRoot.Add(new PropertyField(targetsProperty, "Targets"));
            }

            inspectorRoot.Add(BuildTargetTagSection());

            if (layersProperty != null)
            {
                inspectorRoot.Add(new PropertyField(layersProperty, "Layers"));
            }
            if (mirrorPairsProperty != null)
            {
                inspectorRoot.Add(new PropertyField(mirrorPairsProperty, "Mirror Pairs"));
            }

            inspectorRoot.Add(BuildSocketSection());
            inspectorRoot.Add(BuildBillboardSection());
            inspectorRoot.Add(BuildRagdollSection());

            // One tracked callback for the whole asset rather than one per field: warnings are
            // cross-row (a duplicate display name involves two sockets) and cross-list (a rig-target
            // socket depends on the targets list), so there is no field whose change is guaranteed
            // to be local.
            inspectorRoot.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            inspectorRoot.Bind(serializedObject);
            return inspectorRoot;
        }

        // -----------------------------------------------------------------------------------
        // Static chrome.
        // -----------------------------------------------------------------------------------

        private static Label BuildSectionHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        /// <summary>
        /// The rig's own stable id, drawn read-only.
        /// </summary>
        /// <remarks>
        /// Selectable on purpose: the id is only useful if it can be copied out into a bug report or
        /// a comparison against a baked registry, and a plain label cannot be copied.
        /// </remarks>
        private Label BuildIdentityBadge()
        {
            RigAsset rig = target as RigAsset;
            string identityText = rig != null
                ? "Rig stable id  0x" + rig.StableId.ToString("X16")
                : "Rig stable id  (unavailable)";
            Label badge = new Label(identityText);
            badge.selection.isSelectable = true;
            badge.style.marginBottom = 4f;
            badge.style.opacity = 0.7f;
            return badge;
        }

        // -----------------------------------------------------------------------------------
        // Target tags (Phase E target-tags spec §4.2, E2). "Map the rig, then tag the parts."
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// One row per target: its name, and a button showing its current tag that opens the
        /// searchable <see cref="TargetTagPicker"/> — this rig's tag column (spec §4.2.1, §4.2).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A separate hand-built section rather than folded into the Targets
        /// <see cref="PropertyField"/> above.</strong> <see cref="RigTargetDefinition.tagId"/> is
        /// marked <c>[HideInInspector]</c> specifically so the default array drawer above can never
        /// render it as a raw <c>uint</c> field — the one thing spec §4.2.1 rules out is a tag ever
        /// being typed anywhere but the registry. This section is the column that field's default
        /// rendering would otherwise have been; it exists only to host the picker.
        /// </para>
        /// <para>
        /// Rows are rebuilt only when the target count changes, mirroring
        /// <see cref="RebuildSocketRows"/>'s reasoning: each row caches a
        /// <see cref="SerializedProperty"/> handle into the targets array, and inserting or removing
        /// a target re-points every handle after the edit site.
        /// </para>
        /// </remarks>
        private VisualElement BuildTargetTagSection()
        {
            VisualElement section = new VisualElement();
            section.Add(BuildSectionHeading("Target Tags"));

            Label explanation = new Label(
                "A tag says what a target is FOR, so a clip can be shared with any other rig that "
                + "tags a part the same way. Tags are always picked from the registry below, never "
                + "typed here - use the picker's 'Edit...' button, beside its search field, to add "
                + "one on the spot.");
            explanation.style.whiteSpace = WhiteSpace.Normal;
            explanation.style.opacity = 0.7f;
            explanation.style.marginBottom = 4f;
            section.Add(explanation);

            targetTagRowContainer = new VisualElement();
            section.Add(targetTagRowContainer);

            targetTagBadgeContainer = new VisualElement();
            targetTagBadgeContainer.style.marginTop = 4f;
            section.Add(targetTagBadgeContainer);

            RebuildTargetTagRows();
            RefreshTargetTagBadges();
            return section;
        }

        private void RebuildTargetTagRows()
        {
            targetTagRows.Clear();
            targetTagRowContainer.Clear();

            if (targetsProperty == null)
            {
                builtTargetTagCount = 0;
                return;
            }

            builtTargetTagCount = targetsProperty.arraySize;

            if (builtTargetTagCount == 0)
            {
                Label emptyNote = new Label("No targets yet. Add one above before tagging it.");
                emptyNote.style.whiteSpace = WhiteSpace.Normal;
                emptyNote.style.opacity = 0.7f;
                targetTagRowContainer.Add(emptyNote);
                return;
            }

            for (int targetIndex = 0; targetIndex < builtTargetTagCount; targetIndex++)
            {
                TargetTagRowElements row = BuildTargetTagRow(targetIndex);
                targetTagRows.Add(row);
                targetTagRowContainer.Add(row.container);
            }
        }

        private TargetTagRowElements BuildTargetTagRow(int targetIndex)
        {
            SerializedProperty targetProperty = targetsProperty.GetArrayElementAtIndex(targetIndex);

            TargetTagRowElements row = new TargetTagRowElements();
            row.targetIndex = targetIndex;
            row.displayNameProperty = targetProperty.FindPropertyRelative("displayName");
            row.tagIdProperty = targetProperty.FindPropertyRelative("tagId");

            row.container = new VisualElement();
            row.container.style.flexDirection = FlexDirection.Row;
            row.container.style.alignItems = Align.Center;
            row.container.style.marginTop = 2f;

            row.nameLabel = new Label(DescribeTargetRowName(row));
            row.nameLabel.style.flexGrow = 1f;
            row.container.Add(row.nameLabel);

            row.tagButton = new Button(() => OpenTargetTagPicker(row)) { text = DescribeTagButtonText(row) };
            row.tagButton.style.minWidth = 140f;
            row.container.Add(row.tagButton);

            return row;
        }

        private static string DescribeTargetRowName(TargetTagRowElements row)
        {
            string displayName = row.displayNameProperty != null ? row.displayNameProperty.stringValue : string.Empty;
            return string.IsNullOrEmpty(displayName) ? UnnamedTargetChoiceLabel : displayName;
        }

        private string DescribeTagButtonText(TargetTagRowElements row)
        {
            uint tagIdValue = row.tagIdProperty != null ? row.tagIdProperty.uintValue : 0u;
            if (tagIdValue == 0u)
            {
                return "Tag: " + NoTagChoiceLabel;
            }
            TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
            string tagName = tagRegistry != null ? tagRegistry.FindName(tagIdValue) : null;
            return tagName != null
                ? "Tag: " + tagName
                : "Tag: (unresolved 0x" + tagIdValue.ToString("X8") + ")";
        }

        /// <summary>
        /// Opens the searchable tag picker anchored to <paramref name="row"/>'s button, the one
        /// surface allowed to write <see cref="RigTargetDefinition.tagId"/> (spec §4.2.1).
        /// </summary>
        private void OpenTargetTagPicker(TargetTagRowElements row)
        {
            TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
            VocabularyPicker.Open(
                inspectorRoot,
                row.tagButton,
                tagRegistry,
                tagRegistry,
                VocabularyPickerConfig.ForTargetTags(tagRegistry),
                chosenTagId =>
                {
                    serializedObject.Update();
                    row.tagIdProperty.uintValue = chosenTagId;
                    serializedObject.ApplyModifiedProperties();
                    row.tagButton.text = DescribeTagButtonText(row);
                    RefreshTargetTagBadges();
                },
                () =>
                {
                    // The registry changed underneath every row (a tag renamed or newly created via
                    // "Edit tags..." / "Create tag..."), not just this one's — every button's label
                    // is re-derived rather than just this row's.
                    RefreshAllTargetTagButtons();
                });
        }

        private void RefreshAllTargetTagButtons()
        {
            for (int rowIndex = 0; rowIndex < targetTagRows.Count; rowIndex++)
            {
                TargetTagRowElements row = targetTagRows[rowIndex];
                row.nameLabel.text = DescribeTargetRowName(row);
                row.tagButton.text = DescribeTagButtonText(row);
            }
            RefreshTargetTagBadges();
        }

        /// <summary>
        /// Re-runs <see cref="ClipValidation.ValidateRig"/> and redraws one <see cref="HelpBox"/> per
        /// T1 finding (V34) — the rule a rig's own target list can violate on its own, without
        /// needing a track or a set to be involved.
        /// </summary>
        private void RefreshTargetTagBadges()
        {
            if (targetTagBadgeContainer == null)
            {
                return;
            }
            targetTagBadgeContainer.Clear();

            RigAsset rig = target as RigAsset;
            if (rig == null)
            {
                return;
            }

            List<ValidationMessage> messages = ClipValidation.ValidateRig(rig);
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                if (message.code != ValidationCode.V34)
                {
                    continue;
                }
                HelpBox badge = new HelpBox(message.text, HelpBoxMessageType.Error);
                badge.style.marginTop = 2f;
                targetTagBadgeContainer.Add(badge);
            }
        }

        /// <summary>
        /// The visual elements and serialized handles of one target tag row.
        /// </summary>
        /// <remarks>
        /// Same discipline as <see cref="SocketRowElements"/>: the handle is only valid while the
        /// targets array's shape is unchanged, so <see cref="RebuildTargetTagRows"/> discards every
        /// row whenever a target is inserted or removed.
        /// </remarks>
        private sealed class TargetTagRowElements
        {
            public int targetIndex;
            public SerializedProperty displayNameProperty;
            public SerializedProperty tagIdProperty;
            public VisualElement container;
            public Label nameLabel;
            public Button tagButton;
        }

        // -----------------------------------------------------------------------------------
        // Billboard section (amendment A44).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The rig's billboard roots, drawn as a plain list with the explanation a reader needs to
        /// know what marking one actually does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deliberately a <see cref="PropertyField"/> over the list rather than the hand-built rows
        /// the socket section uses. Sockets earn that treatment because they carry cross-row
        /// warnings and a mode-dependent target picker; a billboard root is a flat block of values
        /// whose one cross-cutting rule - two roots on one node - is already reported by validation
        /// rule V22 wherever the rig is validated. Hand-drawing it would add a second place for that
        /// rule to be stated slightly differently.
        /// </para>
        /// <para>
        /// The most useful place to <em>create</em> one is the Clip Editor's hierarchy, where the
        /// node being marked is in front of the author and the address is filled in for them. This
        /// section is for tuning what a root does once it exists.
        /// </para>
        /// </remarks>
        private VisualElement BuildBillboardSection()
        {
            VisualElement section = new VisualElement();
            section.Add(BuildSectionHeading("Billboarding"));

            Label explanation = new Label(
                "A billboard root turns to face the viewer, and every node beneath it inherits that "
                + "turn unless it declares a root of its own. Marking a node is usually easier from "
                + "the Clip Editor's hierarchy, which fills in the address for you.\n\n"
                + "Billboarding is applied after the animation pose, so at full blend weight it "
                + "replaces a node's animated rotation outright.");
            explanation.style.whiteSpace = WhiteSpace.Normal;
            explanation.style.opacity = 0.7f;
            explanation.style.marginBottom = 4f;
            section.Add(explanation);

            if (billboardRootsProperty != null)
            {
                section.Add(new PropertyField(billboardRootsProperty, "Billboard Roots"));
            }
            return section;
        }

        // -----------------------------------------------------------------------------------
        // Ragdoll section (Phase D, amendment A50).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The rig's ragdoll settings and bodies, plus a badge for every ragdoll validation finding
        /// (V26–V32).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Same shape as <see cref="BuildBillboardSection"/> and for the same reason: a ragdoll body
        /// is a flat block of values with no cross-row UI of its own at this phase, so a plain
        /// <see cref="PropertyField"/> over the list is enough. The one thing this section adds that
        /// the billboard one does not is the badge list — billboarding's only cross-cutting rules
        /// (V21–V23) are rare enough in practice that the default inspector's own error icons on a
        /// broken <see cref="PropertyField"/> row were judged sufficient; a ragdoll body carries
        /// seven rules including one, V31, that is rig-wide rather than per-row and so has no row of
        /// its own to badge.
        /// </para>
        /// <para>
        /// <strong>Placing and dragging boxes in the viewport, and the "Fix addresses" reconciler,
        /// are out of scope here.</strong> Both are Phase D6 and D-later work respectively (spec
        /// §8.3, §8.7); this section only renders what a rig asset alone can show.
        /// </para>
        /// </remarks>
        private VisualElement BuildRagdollSection()
        {
            VisualElement section = new VisualElement();
            section.Add(BuildSectionHeading("Ragdoll"));

            Label explanation = new Label(
                "A ragdoll body gives a node a box collider that falls and collides once the "
                + "ragdoll is dropped. Space, gravity scale and solver tuning are rig-wide - half a "
                + "ragdoll on a plane and half free in space is not a supported configuration. A rig "
                + "with no bodies bakes no ragdoll components at all.");
            explanation.style.whiteSpace = WhiteSpace.Normal;
            explanation.style.opacity = 0.7f;
            explanation.style.marginBottom = 4f;
            section.Add(explanation);

            if (ragdollSettingsProperty != null)
            {
                section.Add(new PropertyField(ragdollSettingsProperty, "Ragdoll Settings"));
            }
            if (ragdollBodiesProperty != null)
            {
                section.Add(new PropertyField(ragdollBodiesProperty, "Ragdoll Bodies"));
            }

            ragdollBadgeContainer = new VisualElement();
            ragdollBadgeContainer.style.marginTop = 4f;
            section.Add(ragdollBadgeContainer);

            RefreshRagdollBadges();
            return section;
        }

        /// <summary>
        /// Re-runs <see cref="ClipValidation.ValidateRig"/> and redraws one <see cref="HelpBox"/> per
        /// ragdoll finding (V26–V32). A full rig validation rather than a hand-rolled subset, so this
        /// badge list can never drift from what the bake actually enforces.
        /// </summary>
        private void RefreshRagdollBadges()
        {
            if (ragdollBadgeContainer == null)
            {
                return;
            }
            ragdollBadgeContainer.Clear();

            RigAsset rig = target as RigAsset;
            if (rig == null)
            {
                return;
            }

            List<ValidationMessage> messages = ClipValidation.ValidateRig(rig);
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                if (!IsRagdollValidationCode(message.code))
                {
                    continue;
                }
                HelpBoxMessageType boxType = message.severity == ValidationSeverity.Error
                    ? HelpBoxMessageType.Error
                    : HelpBoxMessageType.Warning;
                HelpBox badge = new HelpBox(message.text, boxType);
                badge.style.marginTop = 2f;
                ragdollBadgeContainer.Add(badge);
            }
        }

        private static bool IsRagdollValidationCode(ValidationCode code)
        {
            return code >= ValidationCode.V26 && code <= ValidationCode.V32;
        }

        // -----------------------------------------------------------------------------------
        // Socket section.
        // -----------------------------------------------------------------------------------

        private VisualElement BuildSocketSection()
        {
            VisualElement section = new VisualElement();
            section.Add(BuildSectionHeading("Sockets"));

            boneNameSource = LoadStoredBoneNameSource();

            boneNameSourceField = new ObjectField("Bone Name Source")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = boneNameSource,
                tooltip = "The VAT source hierarchy whose bone names the Bone dropdowns list. "
                    + "Editor convenience only - it is never written into the rig asset."
            };
            boneNameSourceField.RegisterValueChangedCallback(OnBoneNameSourceChanged);
            section.Add(boneNameSourceField);

            Label boneNameSourceNote = new Label(
                "Remembered per rig in editor preferences, not stored in the asset.");
            boneNameSourceNote.style.opacity = 0.7f;
            boneNameSourceNote.style.marginBottom = 4f;
            boneNameSourceNote.style.whiteSpace = WhiteSpace.Normal;
            section.Add(boneNameSourceNote);

            socketRowContainer = new VisualElement();
            section.Add(socketRowContainer);

            Button addSocketButton = new Button(AddSocket) { text = "Add Socket" };
            addSocketButton.style.marginTop = 6f;
            section.Add(addSocketButton);

            RefreshBoneNameChoices();
            RebuildSocketRows();
            return section;
        }

        /// <summary>
        /// Tears down and rebuilds every socket row from the current serialized array.
        /// </summary>
        /// <remarks>
        /// A full rebuild rather than an incremental diff. Every row caches
        /// <see cref="SerializedProperty"/> handles into the array, and inserting or deleting an
        /// element re-points every handle after the edit site — so a partial update would leave
        /// surviving rows editing their neighbours' data. That bug is silent and destructive, and
        /// rebuilding a list of a few sockets costs nothing.
        /// </remarks>
        private void RebuildSocketRows()
        {
            socketRows.Clear();
            socketRowContainer.Clear();

            RefreshTargetChoices();

            if (socketsProperty == null)
            {
                builtSocketCount = 0;
                return;
            }

            builtSocketCount = socketsProperty.arraySize;
            builtTargetSignature = BuildTargetSignature();

            if (builtSocketCount == 0)
            {
                Label emptyNote = new Label(
                    "No sockets. A rig without sockets bakes no socket blob and its actors carry no "
                    + "socket component.");
                emptyNote.style.whiteSpace = WhiteSpace.Normal;
                emptyNote.style.opacity = 0.7f;
                socketRowContainer.Add(emptyNote);
                return;
            }

            for (int socketIndex = 0; socketIndex < builtSocketCount; socketIndex++)
            {
                SocketRowElements row = BuildSocketRow(socketIndex);
                socketRows.Add(row);
                socketRowContainer.Add(row.container);
            }

            // The rows were created after the root was bound, so they carry no bindings yet.
            socketRowContainer.Bind(serializedObject);
            RefreshAllRows();
        }

        private SocketRowElements BuildSocketRow(int socketIndex)
        {
            SerializedProperty socketProperty = socketsProperty.GetArrayElementAtIndex(socketIndex);

            SocketRowElements row = new SocketRowElements();
            row.socketIndex = socketIndex;
            row.displayNameProperty = socketProperty.FindPropertyRelative("displayName");
            row.stableIdProperty = socketProperty.FindPropertyRelative("stableId");
            row.modeProperty = socketProperty.FindPropertyRelative("mode");
            row.targetIdProperty = socketProperty.FindPropertyRelative("targetId");
            row.boneNameProperty = socketProperty.FindPropertyRelative("boneName");

            row.container = new VisualElement();
            row.container.style.marginTop = 6f;
            row.container.style.paddingLeft = 6f;
            row.container.style.paddingRight = 6f;
            row.container.style.paddingTop = 4f;
            row.container.style.paddingBottom = 6f;
            row.container.style.borderLeftWidth = 2f;
            row.container.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.5f, 0.6f));

            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;

            PropertyField displayNameField = new PropertyField(row.displayNameProperty, "Display Name");
            displayNameField.style.flexGrow = 1f;
            headerRow.Add(displayNameField);

            row.identityBadge = new Label(BuildSocketIdentityText(row.stableIdProperty));
            row.identityBadge.selection.isSelectable = true;
            row.identityBadge.style.opacity = 0.7f;
            row.identityBadge.style.marginLeft = 6f;
            row.identityBadge.tooltip =
                "The socket's stable id. Paste this into a Socket Attachment component's Socket Id "
                + "field to make something ride this socket.";
            headerRow.Add(row.identityBadge);

            row.container.Add(headerRow);

            PropertyField modeField = new PropertyField(row.modeProperty, "Mode");
            modeField.RegisterValueChangeCallback(changeEvent => RefreshRow(row));
            row.container.Add(modeField);

            row.container.Add(BuildRigTargetSection(row));
            row.container.Add(BuildBoneSection(row));

            row.container.Add(new PropertyField(
                socketProperty.FindPropertyRelative("layerIndex"), "Layer Index"));
            row.container.Add(new PropertyField(
                socketProperty.FindPropertyRelative("localPosition"), "Local Position"));
            row.container.Add(new PropertyField(
                socketProperty.FindPropertyRelative("localEulerAngles"), "Local Euler Angles"));

            row.warningBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            row.warningBox.style.marginTop = 4f;
            row.container.Add(row.warningBox);

            int capturedIndex = socketIndex;
            Button removeButton = new Button(() => RemoveSocket(capturedIndex)) { text = "Remove Socket" };
            removeButton.style.marginTop = 4f;
            row.container.Add(removeButton);

            return row;
        }

        private VisualElement BuildRigTargetSection(SocketRowElements row)
        {
            row.rigTargetSection = new VisualElement();
            row.rigTargetDropdown = new DropdownField("Rig Target");
            row.rigTargetDropdown.tooltip =
                "The rig target this socket follows. Stored as the target's stable id, so renaming "
                + "or reordering targets cannot re-point the socket.";
            row.rigTargetDropdown.RegisterValueChangedCallback(
                changeEvent => OnRigTargetChoiceChanged(row, changeEvent.newValue));
            row.rigTargetSection.Add(row.rigTargetDropdown);
            return row.rigTargetSection;
        }

        private VisualElement BuildBoneSection(SocketRowElements row)
        {
            row.boneSection = new VisualElement();

            row.boneDropdown = new DropdownField("Bone");
            row.boneDropdown.tooltip =
                "A bone of the assigned source hierarchy. Matched by exact name at bake time, the "
                + "same way the VAT baker resolves it.";
            row.boneDropdown.RegisterValueChangedCallback(
                changeEvent => OnBoneChoiceChanged(row, changeEvent.newValue));
            row.boneSection.Add(row.boneDropdown);

            // The typed fallback stays a bound PropertyField rather than a hand-rolled text field:
            // it is the path a user takes when no source prefab is available, which is exactly when
            // they most need Undo to work.
            row.boneNameFallbackField = new PropertyField(row.boneNameProperty, "Bone Name (unverified)");
            row.boneSection.Add(row.boneNameFallbackField);

            return row.boneSection;
        }

        private static string BuildSocketIdentityText(SerializedProperty stableIdProperty)
        {
            if (stableIdProperty == null)
            {
                return "id (unavailable)";
            }
            return "id 0x" + stableIdProperty.uintValue.ToString("X8");
        }

        // -----------------------------------------------------------------------------------
        // Add and remove.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Appends a socket row and gives it a fresh stable id.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Why the id is minted here rather than left to the asset.</strong>
        /// <c>RigAsset.EnsureStableIds</c> is <c>internal</c> to the Authoring assembly, so this
        /// assembly cannot call it; the obvious workaround is to insert the element, apply, and let
        /// the asset's own <c>OnValidate</c> mint the id. That route is real —
        /// <c>OnValidate</c> exists on <see cref="RigAsset"/> and does call it — but it is not
        /// sufficient here, because <see cref="SerializedProperty.InsertArrayElementAtIndex"/>
        /// duplicates the neighbouring element's values, and <c>EnsureStableIds</c> is idempotent:
        /// it only fills ids that are still 0. A duplicated non-zero id would therefore be left
        /// alone, and the rig would ship two sockets sharing one identity — every attachment aimed
        /// at either would resolve to whichever the registry sorted first.
        /// </para>
        /// <para>
        /// So every field of the new row is written explicitly, including a fresh id from the
        /// public <c>StableIdUtility</c> the asset itself uses. <c>OnValidate</c> still runs on
        /// apply and still acts as the backstop for rows created any other way; this path simply
        /// never leaves it anything to do.
        /// </para>
        /// </remarks>
        private void AddSocket()
        {
            if (socketsProperty == null)
            {
                return;
            }

            serializedObject.Update();

            int newSocketIndex = socketsProperty.arraySize;
            socketsProperty.InsertArrayElementAtIndex(newSocketIndex);
            SerializedProperty newSocket = socketsProperty.GetArrayElementAtIndex(newSocketIndex);

            newSocket.FindPropertyRelative("displayName").stringValue =
                "Socket " + (newSocketIndex + 1).ToString();
            newSocket.FindPropertyRelative("stableId").uintValue = StableIdUtility.NewTargetStableId();
            newSocket.FindPropertyRelative("mode").enumValueIndex = (int)SocketAttachMode.RigTarget;
            newSocket.FindPropertyRelative("targetId").uintValue = 0u;
            newSocket.FindPropertyRelative("boneName").stringValue = string.Empty;
            newSocket.FindPropertyRelative("layerIndex").intValue = 0;
            newSocket.FindPropertyRelative("localPosition").vector3Value = Vector3.zero;
            newSocket.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;

            serializedObject.ApplyModifiedProperties();
            RebuildSocketRows();
        }

        /// <summary>
        /// Deletes one socket row.
        /// </summary>
        /// <remarks>
        /// A single <see cref="SerializedProperty.DeleteArrayElementAtIndex"/> is enough here.
        /// The familiar "first delete only nulls the entry" quirk applies to arrays of
        /// <see cref="UnityEngine.Object"/> references; <c>SocketDefinition</c> is a plain
        /// serializable class stored by value, so the element is removed outright.
        /// </remarks>
        private void RemoveSocket(int socketIndex)
        {
            if (socketsProperty == null || socketIndex < 0 || socketIndex >= socketsProperty.arraySize)
            {
                return;
            }

            serializedObject.Update();
            socketsProperty.DeleteArrayElementAtIndex(socketIndex);
            serializedObject.ApplyModifiedProperties();
            RebuildSocketRows();
        }

        // -----------------------------------------------------------------------------------
        // Bone name source (editor-only).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The editor preference key holding this rig's remembered bone name source.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Why this is not a field on <see cref="RigAsset"/>.</strong> The source hierarchy
        /// is an authoring aid — a place to read a list of names from — not rig data. Nothing in the
        /// bake, the blob, or the runtime reads it. Serialising it would make every rig carry a hard
        /// reference to a prefab it does not otherwise depend on, which drags that prefab and its
        /// meshes into any build or asset bundle the rig lands in, and makes deleting an obsolete
        /// source prefab break assets that never needed it. An editor preference keyed by the rig's
        /// GUID gives the same convenience and none of that.
        /// </para>
        /// </remarks>
        private string BuildBoneNameSourcePreferenceKey()
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(assetGuid))
            {
                return string.Empty;
            }
            return BoneNameSourcePreferenceKeyPrefix + assetGuid;
        }

        private GameObject LoadStoredBoneNameSource()
        {
            string preferenceKey = BuildBoneNameSourcePreferenceKey();
            if (string.IsNullOrEmpty(preferenceKey))
            {
                return null;
            }
            string storedGuid = EditorPrefs.GetString(preferenceKey, string.Empty);
            if (string.IsNullOrEmpty(storedGuid))
            {
                return null;
            }
            string storedPath = AssetDatabase.GUIDToAssetPath(storedGuid);
            if (string.IsNullOrEmpty(storedPath))
            {
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(storedPath);
        }

        private void StoreBoneNameSource()
        {
            string preferenceKey = BuildBoneNameSourcePreferenceKey();
            if (string.IsNullOrEmpty(preferenceKey))
            {
                return;
            }

            // Only an asset can be remembered: a scene object has no GUID to write down, and a
            // stale scene reference restored into a different scene would be worse than none.
            string sourcePath = boneNameSource != null
                ? AssetDatabase.GetAssetPath(boneNameSource)
                : string.Empty;
            string sourceGuid = string.IsNullOrEmpty(sourcePath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(sourcePath);

            if (string.IsNullOrEmpty(sourceGuid))
            {
                EditorPrefs.DeleteKey(preferenceKey);
                return;
            }
            EditorPrefs.SetString(preferenceKey, sourceGuid);
        }

        private void OnBoneNameSourceChanged(ChangeEvent<Object> changeEvent)
        {
            boneNameSource = changeEvent.newValue as GameObject;
            StoreBoneNameSource();
            RefreshBoneNameChoices();
            RebuildSocketRows();
        }

        /// <summary>
        /// Reads the bone names out of the assigned source hierarchy.
        /// </summary>
        /// <remarks>
        /// Inactive children are included, and duplicates collapse to their first occurrence, so the
        /// list matches exactly what <c>VatTextureBaker</c> will resolve against at bake time: it
        /// walks <c>GetComponentsInChildren&lt;Transform&gt;(true)</c> and takes the first exact name
        /// match. Offering a name the baker would not find, or hiding one it would, would make this
        /// dropdown a second source of truth — which is the failure it was written to prevent.
        /// </remarks>
        private void RefreshBoneNameChoices()
        {
            boneChoiceLabels.Clear();
            boneNameLookup.Clear();

            if (boneNameSource == null)
            {
                return;
            }

            Transform[] hierarchy = boneNameSource.GetComponentsInChildren<Transform>(true);
            for (int boneIndex = 0; boneIndex < hierarchy.Length; boneIndex++)
            {
                string boneName = hierarchy[boneIndex].name;
                if (string.IsNullOrEmpty(boneName) || boneNameLookup.Contains(boneName))
                {
                    continue;
                }
                boneNameLookup.Add(boneName);
                boneChoiceLabels.Add(boneName);
            }
        }

        // -----------------------------------------------------------------------------------
        // Target choices.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the shared rig-target dropdown choices from the rig's own target list.
        /// </summary>
        /// <remarks>
        /// Labels are forced unique — an unnamed target gets a placeholder and a repeated name gets
        /// its row index appended. The dropdown selects by string, so two identical labels would
        /// make the second target unreachable; uniqueness is cheaper than teaching every call site
        /// to select by index.
        /// </remarks>
        private void RefreshTargetChoices()
        {
            targetChoiceLabels.Clear();
            targetChoiceIds.Clear();

            targetChoiceLabels.Add(NoSelectionChoiceLabel);
            targetChoiceIds.Add(0u);

            if (targetsProperty == null)
            {
                return;
            }

            HashSet<string> usedLabels = new HashSet<string>();
            usedLabels.Add(NoSelectionChoiceLabel);

            for (int targetIndex = 0; targetIndex < targetsProperty.arraySize; targetIndex++)
            {
                SerializedProperty targetProperty = targetsProperty.GetArrayElementAtIndex(targetIndex);
                if (targetProperty == null)
                {
                    continue;
                }
                SerializedProperty targetDisplayNameProperty =
                    targetProperty.FindPropertyRelative("displayName");
                SerializedProperty targetStableIdProperty =
                    targetProperty.FindPropertyRelative("stableId");
                if (targetStableIdProperty == null)
                {
                    continue;
                }

                string targetDisplayName = targetDisplayNameProperty != null
                    ? targetDisplayNameProperty.stringValue
                    : string.Empty;
                if (string.IsNullOrEmpty(targetDisplayName))
                {
                    targetDisplayName = UnnamedTargetChoiceLabel;
                }
                if (usedLabels.Contains(targetDisplayName))
                {
                    targetDisplayName = targetDisplayName + "  #" + targetIndex.ToString();
                }
                usedLabels.Add(targetDisplayName);

                targetChoiceLabels.Add(targetDisplayName);
                targetChoiceIds.Add(targetStableIdProperty.uintValue);
            }
        }

        /// <summary>
        /// A cheap fingerprint of the targets list, used to decide when the dropdowns went stale.
        /// </summary>
        private string BuildTargetSignature()
        {
            if (targetsProperty == null)
            {
                return string.Empty;
            }
            System.Text.StringBuilder signature = new System.Text.StringBuilder();
            for (int targetIndex = 0; targetIndex < targetsProperty.arraySize; targetIndex++)
            {
                SerializedProperty targetProperty = targetsProperty.GetArrayElementAtIndex(targetIndex);
                if (targetProperty == null)
                {
                    continue;
                }
                SerializedProperty targetDisplayNameProperty =
                    targetProperty.FindPropertyRelative("displayName");
                SerializedProperty targetStableIdProperty =
                    targetProperty.FindPropertyRelative("stableId");
                signature.Append(targetDisplayNameProperty != null
                    ? targetDisplayNameProperty.stringValue
                    : string.Empty);
                signature.Append('|');
                signature.Append(targetStableIdProperty != null
                    ? targetStableIdProperty.uintValue.ToString("X8")
                    : "00000000");
                signature.Append(';');
            }
            return signature.ToString();
        }

        // -----------------------------------------------------------------------------------
        // Dropdown writes.
        // -----------------------------------------------------------------------------------

        private void OnRigTargetChoiceChanged(SocketRowElements row, string chosenLabel)
        {
            if (row.isRefreshing || row.targetIdProperty == null)
            {
                return;
            }

            int chosenIndex = row.rigTargetLabels.IndexOf(chosenLabel);
            if (chosenIndex < 0 || chosenIndex >= row.rigTargetIds.Count)
            {
                return;
            }

            serializedObject.Update();
            row.targetIdProperty.uintValue = row.rigTargetIds[chosenIndex];
            serializedObject.ApplyModifiedProperties();
            RefreshAllRows();
        }

        private void OnBoneChoiceChanged(SocketRowElements row, string chosenLabel)
        {
            if (row.isRefreshing || row.boneNameProperty == null)
            {
                return;
            }

            string chosenBoneName = chosenLabel == NoSelectionChoiceLabel ? string.Empty : chosenLabel;
            if (!string.IsNullOrEmpty(chosenBoneName) && !boneNameLookup.Contains(chosenBoneName))
            {
                // The only unmatched entry a bone dropdown ever offers is the synthetic row that
                // echoes the socket's current, unresolved name. Re-selecting it must not overwrite
                // the stored name with the decorated label.
                return;
            }

            serializedObject.Update();
            row.boneNameProperty.stringValue = chosenBoneName;
            serializedObject.ApplyModifiedProperties();
            RefreshAllRows();
        }

        // -----------------------------------------------------------------------------------
        // Refresh.
        // -----------------------------------------------------------------------------------

        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            // Unconditional and ahead of the socket-only early return below: ragdoll findings are
            // cross-row (V28, V31) and cross-list (V26 depends on targets), so there is no cheap way
            // to tell whether this change touched them, and re-validating a handful of rows costs
            // nothing worth guarding.
            RefreshRagdollBadges();

            // Same unconditional placement as the ragdoll badge refresh above, and ahead of the
            // socket-only early return below: a target's tag can change from Undo/Redo or from the
            // "Edit tags..." window without this rig's own targets array ever resizing, so there is
            // no cheap way to tell whether a repaint is needed without just doing the cheap part of
            // it (a handful of label/button text updates) every time.
            if (targetsProperty != null && targetsProperty.arraySize != builtTargetTagCount)
            {
                RebuildTargetTagRows();
                RefreshTargetTagBadges();
            }
            else
            {
                RefreshAllTargetTagButtons();
            }

            if (socketsProperty == null)
            {
                return;
            }

            bool socketCountChanged = socketsProperty.arraySize != builtSocketCount;
            bool targetsChanged = BuildTargetSignature() != builtTargetSignature;
            if (socketCountChanged || targetsChanged)
            {
                RebuildSocketRows();
                return;
            }
            RefreshAllRows();
        }

        private void RefreshAllRows()
        {
            for (int rowIndex = 0; rowIndex < socketRows.Count; rowIndex++)
            {
                RefreshRow(socketRows[rowIndex]);
            }
        }

        /// <summary>
        /// Re-derives one row's mode-dependent visibility, dropdown contents, and warning text.
        /// </summary>
        /// <remarks>
        /// The mode-dependent halves are shown and hidden through <c>display</c>, not disabled: a
        /// greyed-out Bone Name under a rig-target socket still reads as a field that matters, and
        /// the whole point of the mode switch is that only one of the two bindings exists at a time.
        /// </remarks>
        private void RefreshRow(SocketRowElements row)
        {
            if (row == null || row.modeProperty == null)
            {
                return;
            }

            row.isRefreshing = true;
            try
            {
                // SocketAttachMode is a contiguous enum starting at 0, so the serialized enum index
                // and the enum value are the same number.
                bool isBoneMode = row.modeProperty.enumValueIndex == (int)SocketAttachMode.Bone;

                row.rigTargetSection.style.display =
                    isBoneMode ? DisplayStyle.None : DisplayStyle.Flex;
                row.boneSection.style.display =
                    isBoneMode ? DisplayStyle.Flex : DisplayStyle.None;

                bool hasBoneNameSource = boneChoiceLabels.Count > 0;
                row.boneDropdown.style.display =
                    isBoneMode && hasBoneNameSource ? DisplayStyle.Flex : DisplayStyle.None;
                row.boneNameFallbackField.style.display =
                    isBoneMode && !hasBoneNameSource ? DisplayStyle.Flex : DisplayStyle.None;

                row.identityBadge.text = BuildSocketIdentityText(row.stableIdProperty);

                RefreshRigTargetDropdown(row);
                RefreshBoneDropdown(row);
                RefreshRowWarnings(row, isBoneMode, hasBoneNameSource);
            }
            finally
            {
                row.isRefreshing = false;
            }
        }

        private void RefreshRigTargetDropdown(SocketRowElements row)
        {
            row.rigTargetLabels.Clear();
            row.rigTargetIds.Clear();
            for (int choiceIndex = 0; choiceIndex < targetChoiceLabels.Count; choiceIndex++)
            {
                row.rigTargetLabels.Add(targetChoiceLabels[choiceIndex]);
                row.rigTargetIds.Add(targetChoiceIds[choiceIndex]);
            }

            uint storedTargetId = row.targetIdProperty != null ? row.targetIdProperty.uintValue : 0u;
            int selectedIndex = row.rigTargetIds.IndexOf(storedTargetId);
            if (selectedIndex < 0)
            {
                // The stored id belongs to no target on this rig. Rather than silently snapping the
                // dropdown to "(none)" — which would destroy the evidence of the mismatch on the
                // next save — the offending id is shown as its own entry and called out below.
                row.rigTargetLabels.Add("(missing target 0x" + storedTargetId.ToString("X8") + ")");
                row.rigTargetIds.Add(storedTargetId);
                selectedIndex = row.rigTargetLabels.Count - 1;
            }

            row.rigTargetDropdown.choices = row.rigTargetLabels;
            row.rigTargetDropdown.SetValueWithoutNotify(row.rigTargetLabels[selectedIndex]);
        }

        private void RefreshBoneDropdown(SocketRowElements row)
        {
            List<string> choices = new List<string>();
            choices.Add(NoSelectionChoiceLabel);
            for (int boneIndex = 0; boneIndex < boneChoiceLabels.Count; boneIndex++)
            {
                choices.Add(boneChoiceLabels[boneIndex]);
            }

            string storedBoneName = row.boneNameProperty != null
                ? row.boneNameProperty.stringValue
                : string.Empty;

            string selectedLabel = NoSelectionChoiceLabel;
            if (!string.IsNullOrEmpty(storedBoneName))
            {
                if (boneNameLookup.Contains(storedBoneName))
                {
                    selectedLabel = storedBoneName;
                }
                else
                {
                    selectedLabel = storedBoneName;
                    choices.Add(storedBoneName);
                }
            }

            row.boneDropdown.choices = choices;
            row.boneDropdown.SetValueWithoutNotify(selectedLabel);
        }

        /// <summary>
        /// Fills in the row's inline warning box.
        /// </summary>
        /// <remarks>
        /// Every condition here is non-fatal — the rig still bakes — which is precisely why it is
        /// worth surfacing at authoring time. A bone that resolves to nothing, or a target id that
        /// matches no target, produces a socket sitting at the actor origin; nothing throws, and the
        /// only signal downstream is one bake warning that is easy to scroll past.
        /// </remarks>
        private void RefreshRowWarnings(SocketRowElements row, bool isBoneMode, bool hasBoneNameSource)
        {
            List<string> messages = new List<string>();
            HelpBoxMessageType severity = HelpBoxMessageType.Info;

            if (isBoneMode)
            {
                string boneName = row.boneNameProperty != null
                    ? row.boneNameProperty.stringValue
                    : string.Empty;
                if (string.IsNullOrEmpty(boneName))
                {
                    messages.Add(
                        "No bone name. This socket will resolve to nothing and its attachment will "
                        + "sit at the actor origin.");
                    severity = HelpBoxMessageType.Warning;
                }
                else if (hasBoneNameSource && !boneNameLookup.Contains(boneName))
                {
                    messages.Add(
                        "Bone '" + boneName + "' is not in the assigned bone name source. The VAT "
                        + "bake will report it unresolved and the attachment will sit at the actor "
                        + "origin.");
                    severity = HelpBoxMessageType.Warning;
                }
                else if (!hasBoneNameSource)
                {
                    messages.Add(
                        "No bone name source assigned, so '" + boneName + "' is unverified. Assign "
                        + "the VAT source hierarchy above to pick from real bone names.");
                }
            }
            else
            {
                uint storedTargetId = row.targetIdProperty != null ? row.targetIdProperty.uintValue : 0u;
                if (storedTargetId == 0u)
                {
                    messages.Add(
                        "No rig target selected. This socket will resolve to nothing and its "
                        + "attachment will sit at the actor origin.");
                    severity = HelpBoxMessageType.Warning;
                }
                else if (!targetChoiceIds.Contains(storedTargetId))
                {
                    messages.Add(
                        "Target id 0x" + storedTargetId.ToString("X8") + " matches no target on this "
                        + "rig. Pick a target, or restore the missing one.");
                    severity = HelpBoxMessageType.Warning;
                }
            }

            if (HasDuplicateDisplayName(row))
            {
                messages.Add(
                    "Another socket on this rig shares this display name. Not fatal - identity is "
                    + "the stable id - but it makes the two impossible to tell apart here.");
                if (severity == HelpBoxMessageType.Info)
                {
                    severity = HelpBoxMessageType.Info;
                }
            }

            if (messages.Count == 0)
            {
                row.warningBox.style.display = DisplayStyle.None;
                return;
            }

            row.warningBox.style.display = DisplayStyle.Flex;
            row.warningBox.messageType = severity;
            row.warningBox.text = string.Join("\n\n", messages.ToArray());
        }

        private bool HasDuplicateDisplayName(SocketRowElements row)
        {
            if (socketsProperty == null || row.displayNameProperty == null)
            {
                return false;
            }
            string displayName = row.displayNameProperty.stringValue;
            if (string.IsNullOrEmpty(displayName))
            {
                return false;
            }

            int matchCount = 0;
            for (int socketIndex = 0; socketIndex < socketsProperty.arraySize; socketIndex++)
            {
                SerializedProperty otherSocket = socketsProperty.GetArrayElementAtIndex(socketIndex);
                if (otherSocket == null)
                {
                    continue;
                }
                SerializedProperty otherDisplayNameProperty =
                    otherSocket.FindPropertyRelative("displayName");
                if (otherDisplayNameProperty == null)
                {
                    continue;
                }
                if (otherDisplayNameProperty.stringValue == displayName)
                {
                    matchCount++;
                }
            }
            return matchCount > 1;
        }

        /// <summary>
        /// The visual elements and serialized handles of one socket row.
        /// </summary>
        /// <remarks>
        /// A row owns its <see cref="SerializedProperty"/> handles so refreshes never re-walk the
        /// array by path. The handles are only valid while the array's shape is unchanged, which is
        /// why <see cref="RigAssetEditor.RebuildSocketRows"/> discards every row whenever an element
        /// is inserted or deleted.
        /// </remarks>
        private sealed class SocketRowElements
        {
            public int socketIndex;

            public SerializedProperty displayNameProperty;
            public SerializedProperty stableIdProperty;
            public SerializedProperty modeProperty;
            public SerializedProperty targetIdProperty;
            public SerializedProperty boneNameProperty;

            public VisualElement container;
            public Label identityBadge;
            public VisualElement rigTargetSection;
            public VisualElement boneSection;
            public DropdownField rigTargetDropdown;
            public DropdownField boneDropdown;
            public PropertyField boneNameFallbackField;
            public HelpBox warningBox;

            public readonly List<string> rigTargetLabels = new List<string>();
            public readonly List<uint> rigTargetIds = new List<uint>();

            // Set while the row is being written to from code, so the dropdowns' change callbacks
            // can tell a user's click apart from the editor's own refresh and not write back a
            // value they just read.
            public bool isRefreshing;
        }
    }
}
