// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The New Rig creation flow (Phase D11): pick a prefab, choose which of its renderer-bearing
    /// nodes become rig targets, and mint a <see cref="RigAsset"/> from the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>An element hosted in a cover pane, following <c>VatBakePanel</c>'s shape.</strong>
    /// <see cref="ClipEditorWindow.ShowNewRigTab"/> shows and hides it the same way
    /// <c>ShowVatBakeTab</c> shows and hides that panel — an absolutely positioned pane over the
    /// dock, never a <c>display:none</c> swap, for the reason <c>.clip-editor__new-rig-pane</c>'s
    /// USS comment gives: a hidden <c>TwoPaneSplitView</c> is laid out at zero by zero and comes
    /// back collapsed with no handle to drag it open again.
    /// </para>
    /// <para>
    /// <strong>Decoupled from the window it lives in, like <c>VatBakePanel</c> is.</strong> This
    /// class never reaches for <c>ClipSetAsset.rig</c> itself; it only reports what it built and
    /// whether the caller asked to have it assigned. <see cref="ClipEditorWindow.OnNewRigCreated"/>
    /// does the actual assignment, through the same field the toolbar's own Rig picker uses — one
    /// place records the undo step and marks the clip set dirty, whichever the pick came from.
    /// </para>
    /// <para>
    /// <strong>No tag assignment here.</strong> Target tags are Phase E, and the registry they
    /// bind to does not exist yet (spec <c>Phase_E_TargetTags_Spec.md</c> §8). Every target this
    /// flow creates gets a freshly minted, unique stable id — correct under both the old scheme and
    /// the tag design, since sharing is meant to be carried by tags layered on afterwards, never by
    /// targets that already share an id.
    /// </para>
    /// </remarks>
    public sealed class NewRigPanel : VisualElement
    {
        /// <summary>One renderer-bearing node found while scanning the source prefab.</summary>
        private sealed class CandidateRow
        {
            public string DisplayName;
            public string SourceNodePath;
            public Toggle ToggleControl;
        }

        private ObjectField sourcePrefabField;
        private Label candidateSummaryLabel;
        private VisualElement candidateContainer;
        private Toggle assignToggle;
        private Label resultLabel;

        private readonly List<CandidateRow> candidateRows = new List<CandidateRow>();
        private ClipSetAsset offeredClipSet;

        /// <summary>Raised when the flow is dismissed, whether by Cancel or after a successful Create.</summary>
        public event Action Closed;

        /// <summary>
        /// Raised after a rig is created and saved. The second argument is whether the panel's own
        /// "assign to open clip set" toggle was checked at the time.
        /// </summary>
        public event Action<RigAsset, bool> RigCreated;

        public NewRigPanel()
        {
            // Written inline rather than through a stylesheet, matching VatBakePanel: this element
            // carries no stylesheet of its own, and a host's sheet has no reason to know the names
            // of rows built here.
            VisualElement root = this;
            root.style.flexGrow = 1f;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 8f;

            root.Add(BuildHeading("New Rig"));

            sourcePrefabField = new ObjectField("Source Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = "The prefab the new rig will preview from and the VAT bake will sample. "
                    + "Its hierarchy is scanned below for nodes to offer as rig targets."
            };
            sourcePrefabField.RegisterValueChangedCallback(changeEvent => RescanHierarchy());
            root.Add(sourcePrefabField);

            root.Add(BuildHeading("Targets"));

            candidateSummaryLabel = new Label(
                "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.");
            candidateSummaryLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(candidateSummaryLabel);

            ScrollView candidateScroll = new ScrollView();
            candidateScroll.style.flexGrow = 1f;
            candidateScroll.style.marginTop = 4f;
            candidateContainer = candidateScroll.contentContainer;
            root.Add(candidateScroll);

            root.Add(BuildHeading("Create"));

            assignToggle = new Toggle("Assign to open clip set (none open)");
            assignToggle.SetEnabled(false);
            root.Add(assignToggle);

            VisualElement buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 6f;

            Button createButton = new Button(Create) { text = "Create Rig" };
            createButton.style.height = 28f;
            createButton.style.flexGrow = 1f;
            buttonRow.Add(createButton);

            Button cancelButton = new Button(Cancel) { text = "Cancel" };
            cancelButton.style.height = 28f;
            cancelButton.style.marginLeft = 6f;
            buttonRow.Add(cancelButton);

            root.Add(buttonRow);

            resultLabel = new Label(string.Empty);
            resultLabel.style.whiteSpace = WhiteSpace.Normal;
            resultLabel.style.marginTop = 8f;
            resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(resultLabel);
        }

        /// <summary>
        /// Tells the panel which clip set is currently open, so the "assign to open clip set"
        /// toggle can offer to point it at whatever this flow creates.
        /// </summary>
        /// <remarks>
        /// Unlike <c>VatBakePanel.OfferClipSet</c>, this always overwrites — New Rig is a one-shot
        /// flow reopened fresh each time (<see cref="ClipEditorWindow.ShowNewRigTab"/> calls this
        /// on every show), not a settings panel a session revisits, so there is no held choice here
        /// that overwriting could destroy.
        /// </remarks>
        public void OfferClipSet(ClipSetAsset clipSet)
        {
            offeredClipSet = clipSet;
            bool hasClipSet = clipSet != null;
            assignToggle.SetEnabled(hasClipSet);
            assignToggle.value = hasClipSet;
            assignToggle.text = hasClipSet
                ? "Assign to open clip set \"" + clipSet.name + "\""
                : "Assign to open clip set (none open)";
        }

        private static Label BuildHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        /// <summary>
        /// Walks the assigned prefab's hierarchy for renderer-bearing nodes and offers each as a
        /// candidate target.
        /// </summary>
        /// <remarks>
        /// <c>Renderer</c> rather than a specific subtype, so a cutout part's <c>MeshRenderer</c>
        /// and a VAT source's <c>SkinnedMeshRenderer</c> are found the same way — this package draws
        /// every part kind through one of those two, never a <c>SpriteRenderer</c>.
        /// </remarks>
        private void RescanHierarchy()
        {
            candidateContainer.Clear();
            candidateRows.Clear();

            GameObject prefab = sourcePrefabField.value as GameObject;
            if (prefab == null)
            {
                candidateSummaryLabel.text =
                    "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.";
                return;
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Transform rendererTransform = renderer.transform;

                string nodePath = PrefabAuthoringBridge.GetHierarchyPath(rendererTransform, prefab.transform);
                if (string.IsNullOrEmpty(nodePath))
                {
                    // A renderer directly on the prefab root has no path distinct from
                    // RigTargetDefinition.sourceNodePath's own "unbound" convention (empty means
                    // not tied to a node). Skipped rather than emitted as a target nothing could
                    // tell apart from one with no binding at all.
                    continue;
                }

                // Pre-ticked when the renderer looks like something the author actually wants
                // shown — enabled and on an active node. A disabled renderer or an inactive helper
                // object (an alternate LOD, a debug visualization) is offered but left unticked,
                // rather than forcing every candidate on and making the list something to prune.
                bool preTicked = renderer.enabled && rendererTransform.gameObject.activeSelf;

                Toggle rowToggle = new Toggle(nodePath) { value = preTicked };
                rowToggle.tooltip = renderer.GetType().Name + " on \"" + rendererTransform.name + "\".";
                candidateContainer.Add(rowToggle);

                CandidateRow row = new CandidateRow
                {
                    DisplayName = rendererTransform.name,
                    SourceNodePath = nodePath,
                    ToggleControl = rowToggle
                };
                candidateRows.Add(row);
            }

            candidateSummaryLabel.text = candidateRows.Count.ToString()
                + " renderer-bearing node(s) found in \"" + prefab.name + "\".";
        }

        private void Create()
        {
            resultLabel.text = string.Empty;

            GameObject prefab = sourcePrefabField.value as GameObject;
            if (prefab == null)
            {
                ReportFailure("Assign a source prefab first.");
                return;
            }
            if (candidateRows.Count == 0)
            {
                ReportFailure("No renderer-bearing nodes were found in \"" + prefab.name + "\"'s hierarchy.");
                return;
            }

            List<RigTargetDefinition> selectedTargets = new List<RigTargetDefinition>();
            for (int rowIndex = 0; rowIndex < candidateRows.Count; rowIndex++)
            {
                CandidateRow row = candidateRows[rowIndex];
                if (row.ToggleControl == null || !row.ToggleControl.value)
                {
                    continue;
                }
                selectedTargets.Add(new RigTargetDefinition
                {
                    displayName = row.DisplayName,
                    sourceNodePath = row.SourceNodePath
                });
            }

            if (selectedTargets.Count == 0)
            {
                ReportFailure("Tick at least one node to become a rig target.");
                return;
            }

            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Create Rig", prefab.name + "Rig", "asset", "Choose where to save the new rig.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            RigAsset newRig = RigAssetUtility.CreateRig(assetPath, prefab, selectedTargets);
            if (newRig == null)
            {
                ReportFailure("Could not create the rig asset at \"" + assetPath + "\".");
                return;
            }

            resultLabel.style.color = new StyleColor(new Color(0.6f, 0.9f, 0.6f));
            resultLabel.text = "Created \"" + newRig.name + "\" with " + selectedTargets.Count.ToString()
                + " target(s).";
            EditorGUIUtility.PingObject(newRig);

            bool assignToClipSet = offeredClipSet != null && assignToggle.value;
            if (RigCreated != null)
            {
                RigCreated(newRig, assignToClipSet);
            }
            if (Closed != null)
            {
                Closed();
            }
        }

        private void Cancel()
        {
            if (Closed != null)
            {
                Closed();
            }
        }

        private void ReportFailure(string message)
        {
            resultLabel.style.color = new StyleColor(new Color(0.95f, 0.55f, 0.55f));
            resultLabel.text = message;
            Debug.LogWarning("[DOTS Animation Toolkit] New Rig: " + message);
        }
    }
}
