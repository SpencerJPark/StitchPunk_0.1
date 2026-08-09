// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
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
            "StitchPunk.AnimationToolkit.RigAssetEditor.boneNameSource.";

        private const string NoSelectionChoiceLabel = "(none)";
        private const string UnnamedTargetChoiceLabel = "(unnamed target)";

        private SerializedProperty targetsProperty;
        private SerializedProperty layersProperty;
        private SerializedProperty mirrorPairsProperty;
        private SerializedProperty socketsProperty;

        private VisualElement inspectorRoot;
        private VisualElement socketRowContainer;
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

            inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            inspectorRoot.Add(BuildSectionHeading("Rig"));
            inspectorRoot.Add(BuildIdentityBadge());

            if (targetsProperty != null)
            {
                inspectorRoot.Add(new PropertyField(targetsProperty, "Targets"));
            }
            if (layersProperty != null)
            {
                inspectorRoot.Add(new PropertyField(layersProperty, "Layers"));
            }
            if (mirrorPairsProperty != null)
            {
                inspectorRoot.Add(new PropertyField(mirrorPairsProperty, "Mirror Pairs"));
            }

            inspectorRoot.Add(BuildSocketSection());

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
