// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// VAT bake UI: the shared shell over <see cref="VatTextureBaker"/>, used by both the
    /// standalone <see cref="VatBakeWindow"/> and the Clip Editor's VAT Bake tab.
    /// </summary>
    public sealed class VatBakePanel : VisualElement, System.IDisposable
    {
        private ObjectField clipSetField;
        private Label resolvedSourceLabel;
        private VatFreshnessBadgeElement freshnessBadge;
        private List<VatBakeSource> resolvedSources;
        private EnumField flavorField;
        private ObjectField rigField;
        private FloatField sampleRateField;
        private Toggle fullPrecisionField;
        private TextField outputFolderField;
        private Label summaryLabel;
        private ScrollView logView;
        private VatPreviewElement preview;
        private ObjectField previewSetField;
        private ActiveAssetSelection selection;
        private ulong lastImportedSourcesKey;

        /// <summary>The preview's own transport, so a host window can route Space/Home/End/arrow keys to it.</summary>
        public ITransportTarget TransportTarget
        {
            get { return preview; }
        }

        /// <summary>Releases the preview's render utility and its copy of the source hierarchy.</summary>
        public void Dispose()
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection.RigChanged -= OnSharedRigChanged;
            }
            VatSourceImportWatcher.AssetsImported -= OnSourcesImported;
            preview?.Dispose();
        }

        public VatBakePanel()
        {
            style.flexGrow = 1f;

            VisualElement assetBar = ToolkitChrome.MakeAssetBar("vat-bake-asset-bar");
            Add(assetBar);

            assetBar.Add(ToolkitChrome.MakeAssetBarLabel("Clip Set"));

            clipSetField = new ObjectField
            {
                name = "vat-bake-clip-set-field",
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                tooltip = "Clips whose ClipAsset names a VAT source clip are baked. Others are skipped."
            };
            clipSetField.AddToClassList("toolkit-asset-bar__field");
            clipSetField.RegisterValueChangedCallback(changeEvent =>
            {
                ClipSetAsset newClipSet = changeEvent.newValue as ClipSetAsset;
                if (selection != null)
                {
                    selection.SetClipSet(newClipSet);
                }
                else
                {
                    OnSharedClipSetChanged(newClipSet);
                }
            });
            assetBar.Add(clipSetField);

            assetBar.Add(ToolkitChrome.MakeAssetBarLabel("Rig"));

            // The bake needs the rig twice over: to read the socket rows it samples, and to stamp
            // sourceRigKey so a later bind cannot pair these textures with another character's mesh.
            rigField = new ObjectField
            {
                name = "vat-bake-rig-field",
                objectType = typeof(RigAsset),
                allowSceneObjects = false,
                tooltip = "The rig these textures are baked for. Socket rows come from it, and it " +
                    "is stamped into the texture set so the wrong rig cannot bind them."
            };
            rigField.AddToClassList("toolkit-asset-bar__field");
            rigField.RegisterValueChangedCallback(changeEvent =>
            {
                RigAsset newRig = changeEvent.newValue as RigAsset;
                if (selection != null)
                {
                    selection.SetRig(newRig);
                }
                else
                {
                    OnSharedRigChanged(newRig);
                }
            });
            assetBar.Add(rigField);

            assetBar.Add(ToolkitChrome.MakeAssetBarSpacer());

            // Not a field: which meshes a bake covers is a fact about the rig, not a fourth thing to keep in
            // step with it. This line is the receipt — what the rig resolved to, or why it did not.
            resolvedSourceLabel = new Label(string.Empty);
            resolvedSourceLabel.name = "vat-resolved-source-label";
            resolvedSourceLabel.AddToClassList("toolkit-hint");
            resolvedSourceLabel.RegisterCallback<ClickEvent>(clickEvent => PingSourcePrefab());
            resolvedSourceLabel.style.flexGrow = 1;
            resolvedSourceLabel.style.flexShrink = 1;

            freshnessBadge = new VatFreshnessBadgeElement();
            freshnessBadge.style.marginLeft = 6;
            freshnessBadge.style.display = DisplayStyle.None;

            VisualElement resolvedSourceRow = new VisualElement();
            resolvedSourceRow.name = "vat-resolved-source-row";
            resolvedSourceRow.style.flexDirection = FlexDirection.Row;
            resolvedSourceRow.style.alignItems = Align.FlexStart;
            resolvedSourceRow.Add(resolvedSourceLabel);
            resolvedSourceRow.Add(freshnessBadge);
            assetBar.Add(resolvedSourceRow);

            VatSourceImportWatcher.AssetsImported += OnSourcesImported;

            // The form is the fixed pane; its divider is remembered across a tab hide/show.
            CoverPaneSplitView splitView = new CoverPaneSplitView("VatBake.Form", 0, 420f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            Add(splitView);

            VisualElement formColumn = new VisualElement { name = "vat-bake-form-column" };
            formColumn.AddToClassList("toolkit-column");
            formColumn.style.minWidth = 320f;
            splitView.Add(formColumn);

            VisualElement root = formColumn;
            root.Add(ToolkitChrome.MakePaneHeader("Bake", out _, out _));

            VisualElement settingsCardBody;
            root.Add(ToolkitChrome.MakeCard("vat-bake-settings-card", "Settings", out settingsCardBody, out _));

            flavorField = new EnumField(VatFlavor.BoneMatrix);
            settingsCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Flavor",
                flavorField,
                "Bone matrices are small and exact for skinned rigs. "
                    + "Vertex positions reproduce anything, including cloth and blendshapes."));

            sampleRateField = new FloatField { value = 30f };
            settingsCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Fallback Samples / Second",
                sampleRateField,
                "Used only for a clip that carries no frame rate of its own. Every clip in a set "
                    + "bakes at its own FPS — the field in the Clip Editor's transport bar — so a set "
                    + "can hold a 12fps clip beside a 60fps one and each keeps its own row count."));

            fullPrecisionField = new Toggle();
            settingsCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Full Precision (RGBAFloat)",
                fullPrecisionField,
                "Doubles memory. Needed for rigs much larger than a couple of metres, where half "
                    + "precision quantisation becomes visible as stepping."));

            VisualElement outputCardBody;
            root.Add(ToolkitChrome.MakeCard("vat-bake-output-card", "Output", out outputCardBody, out _));

            // Left empty on purpose: a package must not hardcode a host's project folders, since
            // that would be wrong in every project organised differently.
            outputFolderField = new TextField { value = string.Empty };
            outputCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Output Folder", outputFolderField, "Leave empty to write beside the clip set."));

            previewSetField = new ObjectField
            {
                objectType = typeof(VatTextureSetAsset),
                allowSceneObjects = false
            };
            previewSetField.name = "vat-preview-set-field";
            previewSetField.RegisterValueChangedCallback(OnPreviewSetFieldChanged);
            outputCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Preview Set",
                previewSetField,
                "The baked set shown in the preview on the right. Filled automatically after a bake, or pick one by hand."));

            root.Add(ToolkitChrome.MakeHeading("Bake"));

            Button bakeButton = ToolkitChrome.MakePrimaryAction(
                Bake, "d_PreTextureRGB", "Bake every VAT-bound clip in the set to textures.", "Bake");
            bakeButton.style.marginTop = 4f;
            root.Add(bakeButton);

            logView = new ScrollView();
            logView.style.flexGrow = 1f;
            logView.style.marginTop = 4f;
            root.Add(logView);

            root.Add(ToolkitChrome.MakeStatusRow(out summaryLabel, out _, true));

            VisualElement previewPane = new VisualElement { name = "vat-bake-preview-pane" };
            previewPane.AddToClassList("toolkit-column");
            previewPane.AddToClassList("toolkit-column--flush");
            previewPane.style.minWidth = 320f;
            splitView.Add(previewPane);

            VisualElement previewHeader = new VisualElement();
            previewHeader.AddToClassList("toolkit-pane-header");
            Label previewTitle = new Label("Preview");
            previewTitle.AddToClassList("toolkit-pane-title");
            previewHeader.Add(previewTitle);
            previewPane.Add(previewHeader);

            preview = new VatPreviewElement();
            preview.style.flexGrow = 1f;
            previewPane.Add(preview);
        }

        // Follows a shared clip-set/rig pick across every host that binds the same selection.
        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection.RigChanged -= OnSharedRigChanged;
            }
            selection = sharedSelection;
            if (selection != null)
            {
                selection.ClipSetChanged += OnSharedClipSetChanged;
                selection.RigChanged += OnSharedRigChanged;
            }
            OnSharedClipSetChanged(selection?.ClipSet);
            OnSharedRigChanged(selection?.Rig);
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            clipSetField.SetValueWithoutNotify(clipSet);
            RefreshPreview();
            RefreshResolvedSources(true);
            RefreshFreshnessBadge();
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            rigField.SetValueWithoutNotify(rig);
            RefreshResolvedSources(true);
            RefreshFreshnessBadge();
        }

        // A save or import can change what the resolved-sources line and freshness badge report
        // without changing selection, so this is the cheap path that skips selection's own refresh.
        private void OnSourcesImported()
        {
            ClipSetAsset importedClipSet = clipSetField.value as ClipSetAsset;
            RigAsset importedRig = rigField.value as RigAsset;
            VatTextureSetAsset importedTextures = importedClipSet != null ? importedClipSet.vatTextures : null;
            ulong importedKey = VatSourceHashResolver.ComputeSourceHash(importedClipSet, importedRig, (VatFlavor)flavorField.value)
                ^ (importedTextures != null ? importedTextures.sourceHash : 0UL);
            if (importedKey == lastImportedSourcesKey)
            {
                return;
            }
            lastImportedSourcesKey = importedKey;

            // The preview copies the whole source hierarchy on every Show, so an import rebuilds
            // it only when the subject mesh actually changed.
            SkinnedMeshRenderer previousRenderer = FirstResolvedRenderer();
            RefreshResolvedSources(false);
            if (FirstResolvedRenderer() != previousRenderer)
            {
                RefreshPreview();
            }
            RefreshFreshnessBadge();
        }

        private void RefreshFreshnessBadge()
        {
            if (freshnessBadge == null)
            {
                return;
            }

            ClipSetAsset badgeClipSet = clipSetField.value as ClipSetAsset;
            RigAsset badgeRig = rigField.value as RigAsset;
            if (badgeClipSet == null || badgeRig == null ||
                (badgeClipSet.vatTextures == null && !VatSourceHashResolver.HasVatBoundClips(badgeClipSet)))
            {
                freshnessBadge.style.display = DisplayStyle.None;
                return;
            }

            string reason;
            VatBakeFreshness freshness = VatSourceHashResolver.Resolve(badgeClipSet, badgeRig, badgeClipSet.vatTextures, out reason);
            freshnessBadge.style.display = DisplayStyle.Flex;
            freshnessBadge.Refresh(freshness, reason);
        }

        private void Bake()
        {
            logView.Clear();

            ClipSetAsset clipSet = clipSetField.value as ClipSetAsset;
            RigAsset rig = rigField.value as RigAsset;

            if (clipSet == null)
            {
                ReportFailure("Assign a Clip Set.");
                return;
            }
            if (rig == null)
            {
                ReportFailure("Assign the Rig these textures are baked for.");
                return;
            }

            // Resolved fresh rather than trusting resolvedSources: a rig edited in the Rigs tab
            // between the last refresh and this click must not bake a stale set of parts.
            List<VatBakeSource> sources;
            string resolveFailureMessage;
            if (!VatBakeSourceResolver.TryResolve(rig, out sources, out resolveFailureMessage))
            {
                ReportFailure(resolveFailureMessage);
                return;
            }

            VatBakePlan bakePlan = VatBakeClipBuilder.Build(clipSet, sources);
            if (!bakePlan.HasAnythingToBake)
            {
                ReportFailure(
                    "No clip in '" + clipSet.name + "' names a VAT source for any part of '" + rig.name
                    + "'. Set vatSource.sourceClip on the ClipAssets you want baked, or add a "
                    + "vatTracks entry naming a target and a source clip for a target-scoped VAT "
                    + "part, or author bone tracks in the Clip Editor — any of those marks a clip "
                    + "as VAT-bound.");
                return;
            }

            for (int skippedIndex = 0; skippedIndex < bakePlan.SkippedPartNames.Count; skippedIndex++)
            {
                Debug.LogWarning(
                    "'" + bakePlan.SkippedPartNames[skippedIndex] + "': no clip in '" + clipSet.name
                    + "' animates this VAT part. It will not be baked.");
            }
            for (int unknownIndex = 0; unknownIndex < bakePlan.UnknownTrackTargets.Count; unknownIndex++)
            {
                Debug.LogWarning(
                    "A vatTrack in '" + clipSet.name + "' names target 0x" + bakePlan.UnknownTrackTargets[unknownIndex]
                    + ", which '" + rig.name + "' does not resolve to a VAT part.");
            }

            GameObject instanceRoot;
            string instanceFailureMessage;
            if (!VatBakeSourceResolver.TryCreateBakeInstance(rig, out instanceRoot, out instanceFailureMessage))
            {
                ReportFailure(instanceFailureMessage);
                return;
            }

            VatFlavor bakeFlavor = (VatFlavor)flavorField.value;
            List<VatBakePartResult> partResults = new List<VatBakePartResult>();

            try
            {
                for (int planIndex = 0; planIndex < bakePlan.Sources.Count; planIndex++)
                {
                    VatBakeSourcePlan sourcePlan = bakePlan.Sources[planIndex];
                    SkinnedMeshRenderer instanceRenderer =
                        VatBakeSourceResolver.FindInInstance(instanceRoot, sourcePlan.Source.SourceNodePath);
                    if (instanceRenderer == null)
                    {
                        ReportFailure(
                            "Could not find '" + sourcePlan.Source.DisplayName + "' in the posed instance of '"
                            + rig.sourcePrefab.name + "'.");
                        return;
                    }

                    VatBakeInput bakeInput = new VatBakeInput
                    {
                        skinnedMeshRenderer = instanceRenderer,
                        flavor = bakeFlavor,
                        samplesPerSecond = sampleRateField.value,
                        useFullPrecision = fullPrecisionField.value,
                        clips = sourcePlan.Clips,
                        // VatTextureBaker samples sockets inside Bake itself, so a second or third
                        // call carrying the same list would write N copies of every socket track.
                        sockets = planIndex == 0 ? CollectBoneSockets(rig) : new List<VatBakeSocket>()
                    };

                    VatBakeResult bakeResult;
                    if (!VatTextureBaker.Bake(bakeInput, out bakeResult))
                    {
                        ReportFailure("'" + sourcePlan.Source.DisplayName + "': " + bakeResult.message);
                        return;
                    }

                    // Same reasoning as the socket warning below: the textures are valid, but every
                    // listed bone stayed at its rest pose, which presents as an animation that
                    // simply does not play rather than as an error anyone would go looking for.
                    if (bakeResult.unresolvedBoneTrackNames != null && bakeResult.unresolvedBoneTrackNames.Count > 0)
                    {
                        Debug.LogWarning(
                            "'" + sourcePlan.Source.DisplayName + "': VAT bake could not resolve "
                            + bakeResult.unresolvedBoneTrackNames.Count.ToString()
                            + " authored bone track name(s) in the source hierarchy: "
                            + string.Join(", ", bakeResult.unresolvedBoneTrackNames)
                            + ". Those bones baked at rest. Check the names on the clip's bone tracks "
                            + "against the rig's source prefab.");
                    }

                    // Surfaced as a warning, not a failure: the textures are valid and usable, but
                    // every listed socket would sit at the actor origin, which is not something to
                    // discover later by watching a sword hover at a character's feet.
                    if (bakeResult.unresolvedSocketBones != null && bakeResult.unresolvedSocketBones.Count > 0)
                    {
                        Debug.LogWarning(
                            "'" + sourcePlan.Source.DisplayName + "': VAT bake could not resolve "
                            + bakeResult.unresolvedSocketBones.Count.ToString()
                            + " socket bone(s) in the source hierarchy: "
                            + string.Join(", ", bakeResult.unresolvedSocketBones)
                            + ". Check the bone names on the rig's socket rows.");
                    }

                    partResults.Add(new VatBakePartResult { Source = sourcePlan.Source, Result = bakeResult });
                }
            }
            finally
            {
                // Torn down only once, after the last Bake call: each call's own finally stops
                // AnimationMode, and pulling the hierarchy out from under a live sampling session
                // strands the Editor in AnimationMode with no way back but a domain reload.
                Object.DestroyImmediate(instanceRoot);
            }

            string outputFolder = ResolveOutputFolder(clipSet);
            string setPath = VatTextureSetBuilder.WriteSet(clipSet, rig, bakeFlavor, outputFolder, partResults);
            ReportSuccess(partResults, setPath);

            VatTextureSetAsset bakedSet = AssetDatabase.LoadAssetAtPath<VatTextureSetAsset>(setPath);
            previewSetField.SetValueWithoutNotify(bakedSet);
            RefreshPreview();
            RefreshFreshnessBadge();
        }

        private void OnPreviewSetFieldChanged(ChangeEvent<Object> changeEvent)
        {
            RefreshPreview();
        }

        // One path for every field the preview reads, so assigning a Rig puts the subject on
        // screen at rest without waiting for a bake.
        private void RefreshPreview()
        {
            if (preview == null)
            {
                return;
            }
            preview.Show(
                previewSetField.value as VatTextureSetAsset,
                clipSetField.value as ClipSetAsset,
                rigField.value as RigAsset,
                FirstResolvedRenderer());
        }

        private SkinnedMeshRenderer FirstResolvedRenderer()
        {
            return resolvedSources != null && resolvedSources.Count > 0
                ? resolvedSources[0].PrefabRenderer
                : null;
        }

        // Not called from Bake, which resolves fresh so a rig edited elsewhere between a refresh
        // and the button press cannot bake a stale set of parts. This is only the on-screen receipt.
        private void RefreshResolvedSources(bool rebuildPreview)
        {
            RigAsset rig = rigField.value as RigAsset;
            List<VatBakeSource> sources;
            string failureMessage;
            if (!VatBakeSourceResolver.TryResolve(rig, out sources, out failureMessage))
            {
                resolvedSources = null;
                resolvedSourceLabel.text = failureMessage;
                resolvedSourceLabel.EnableInClassList("toolkit-text--warning", true);
                if (rebuildPreview)
                {
                    RefreshPreview();
                }
                return;
            }

            resolvedSources = sources;
            resolvedSourceLabel.EnableInClassList("toolkit-text--warning", false);

            if (sources.Count == 1)
            {
                VatBakeSource onlySource = sources[0];
                int boneCount = onlySource.PrefabRenderer.bones == null ? 0 : onlySource.PrefabRenderer.bones.Length;
                resolvedSourceLabel.text = rig.sourcePrefab.name + " ▸ " + onlySource.DisplayName
                    + " · " + boneCount.ToString() + " bones";
                if (rebuildPreview)
                {
                    RefreshPreview();
                }
                return;
            }

            ClipSetAsset clipSet = clipSetField.value as ClipSetAsset;
            if (clipSet == null)
            {
                List<string> allPartNames = new List<string>();
                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    allPartNames.Add(sources[sourceIndex].DisplayName);
                }
                resolvedSourceLabel.text = "resolves " + sources.Count.ToString() + " VAT parts · "
                    + string.Join(", ", allPartNames);
                if (rebuildPreview)
                {
                    RefreshPreview();
                }
                return;
            }

            VatBakePlan bakePlan = VatBakeClipBuilder.Build(clipSet, sources);
            List<string> bakedPartNames = new List<string>();
            for (int sourceIndex = 0; sourceIndex < bakePlan.Sources.Count; sourceIndex++)
            {
                bakedPartNames.Add(bakePlan.Sources[sourceIndex].Source.DisplayName);
            }

            // No trailing separator when nothing bakes: every part is named on its own line below,
            // and "baking 0 of 2 VAT parts · " reads as a list that failed to render.
            string headline = "baking " + bakePlan.Sources.Count.ToString() + " of " + sources.Count.ToString()
                + " VAT parts";
            if (bakedPartNames.Count > 0)
            {
                headline = headline + " · " + string.Join(", ", bakedPartNames);
            }
            if (bakePlan.SkippedPartNames.Count == 0)
            {
                resolvedSourceLabel.text = headline;
            }
            else
            {
                List<string> skippedLines = new List<string>();
                for (int skippedIndex = 0; skippedIndex < bakePlan.SkippedPartNames.Count; skippedIndex++)
                {
                    skippedLines.Add(bakePlan.SkippedPartNames[skippedIndex] + " — no clip in this set animates it");
                }
                resolvedSourceLabel.text = headline + "\n" + string.Join("\n", skippedLines);
            }

            if (rebuildPreview)
            {
                RefreshPreview();
            }
        }

        private void PingSourcePrefab()
        {
            RigAsset rig = rigField.value as RigAsset;
            if (rig != null && rig.sourcePrefab != null)
            {
                EditorGUIUtility.PingObject(rig.sourcePrefab);
            }
        }

        private void ReportSuccess(List<VatBakePartResult> partResults, string setPath)
        {
            int totalClipRangeCount = 0;
            for (int partIndex = 0; partIndex < partResults.Count; partIndex++)
            {
                List<VatClipRange> partRanges = partResults[partIndex].Result.clipRanges;
                totalClipRangeCount += partRanges == null ? 0 : partRanges.Count;
            }

            ToolkitChrome.SetStatus(
                summaryLabel,
                "Baked " + partResults.Count.ToString() + " VAT part(s), "
                    + totalClipRangeCount.ToString() + " clip range(s).",
                ToolkitStatusTone.Neutral);

            StringBuilder detail = new StringBuilder();
            detail.AppendLine("Texture set: " + setPath);
            detail.AppendLine();
            for (int partIndex = 0; partIndex < partResults.Count; partIndex++)
            {
                VatBakePartResult partResult = partResults[partIndex];
                detail.AppendLine(
                    partResult.Source.DisplayName + ": " + partResult.Result.message
                    + "  source hash 0x" + partResult.Result.sourceHash.ToString("X16"));

                List<VatClipRange> clipRanges = partResult.Result.clipRanges;
                for (int rangeIndex = 0; rangeIndex < clipRanges.Count; rangeIndex++)
                {
                    VatClipRange range = clipRanges[rangeIndex];
                    string targetLabel = range.targetId == 0u
                        ? string.Empty
                        : "  target 0x" + range.targetId.ToString("X8");
                    detail.AppendLine(
                        "  clip 0x" + range.clipId.ToString("X16")
                        + targetLabel
                        + "  frames " + range.frameStart.ToString()
                        + ".." + (range.frameStart + range.frameCount - 1).ToString()
                        + "  @" + range.fps.ToString() + "fps");
                }
            }

            AppendLog(detail.ToString());
        }

        // Rig-target sockets are deliberately excluded: their motion is the part's own transform,
        // computed live every frame, so baking it would store a second copy that could go stale.
        private static List<VatBakeSocket> CollectBoneSockets(RigAsset rig)
        {
            List<VatBakeSocket> boneSockets = new List<VatBakeSocket>();
            if (rig == null || rig.sockets == null)
            {
                return boneSockets;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket == null || socket.mode != SocketAttachMode.Bone || !socket.Id.IsValid)
                {
                    continue;
                }
                boneSockets.Add(new VatBakeSocket
                {
                    socketId = socket.Id.Value,
                    boneName = socket.boneName
                });
            }
            return boneSockets;
        }

        private void ReportFailure(string message)
        {
            ToolkitChrome.SetStatus(summaryLabel, "Bake failed.", ToolkitStatusTone.Error);
            AppendLog(message);
        }

        private void AppendLog(string text)
        {
            Label line = new Label(text);
            line.style.whiteSpace = WhiteSpace.Normal;
            line.selection.isSelectable = true;
            logView.Add(line);
        }

        /// <summary>
        /// The folder to write into: whatever the user typed, or the clip set's own folder.
        /// </summary>
        private string ResolveOutputFolder(ClipSetAsset clipSet)
        {
            string typedFolder = outputFolderField.value;
            if (!string.IsNullOrEmpty(typedFolder))
            {
                return typedFolder.TrimEnd('/');
            }

            string clipSetPath = AssetDatabase.GetAssetPath(clipSet);
            int lastSeparator = clipSetPath.LastIndexOf('/');
            return lastSeparator > 0 ? clipSetPath.Substring(0, lastSeparator) : clipSetPath;
        }
    }
}
