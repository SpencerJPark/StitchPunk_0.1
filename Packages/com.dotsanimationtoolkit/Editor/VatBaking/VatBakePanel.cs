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
        private List<VatBakeSource> resolvedSources;
        private EnumField flavorField;
        private ObjectField rigField;
        private FloatField sampleRateField;
        private Toggle fullPrecisionField;
        private TextField outputFolderField;
        private Label summaryLabel;
        private Label sourceBoundHint;
        private ScrollView logView;
        private VatPreviewElement preview;
        private ObjectField previewSetField;

        /// <summary>The preview's own transport, so a host window can route Space/Home/End/arrow keys to it.</summary>
        public ITransportTarget TransportTarget
        {
            get { return preview; }
        }

        /// <summary>Releases the preview's render utility and its copy of the source hierarchy.</summary>
        public void Dispose()
        {
            preview?.Dispose();
        }

        public VatBakePanel()
        {
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Row;

            VisualElement formColumn = new VisualElement { name = "vat-bake-form-column" };
            formColumn.style.width = 420f;
            formColumn.style.flexShrink = 0f;
            formColumn.style.paddingLeft = 10f;
            formColumn.style.paddingRight = 10f;
            formColumn.style.paddingTop = 8f;
            Add(formColumn);

            VisualElement root = formColumn;
            root.Add(BuildHeading("Source"));

            clipSetField = new ObjectField("Clip Set")
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                tooltip = "Clips whose ClipAsset names a VAT source clip are baked. Others are skipped."
            };
            clipSetField.RegisterValueChangedCallback(changeEvent =>
            {
                RefreshPreview();
                RefreshResolvedSources();
            });
            root.Add(clipSetField);

            // The bake needs the rig twice over: to read the socket rows it samples, and to stamp
            // sourceRigKey so a later bind cannot pair these textures with another character's mesh.
            rigField = new ObjectField("Rig")
            {
                objectType = typeof(RigAsset),
                allowSceneObjects = false,
                tooltip = "The rig these textures are baked for. Socket rows come from it, and it " +
                    "is stamped into the texture set so the wrong rig cannot bind them."
            };
            rigField.RegisterValueChangedCallback(changeEvent => RefreshResolvedSources());
            root.Add(rigField);

            // Hidden until a host calls SetSource — in the standalone window there is nowhere else
            // to change these fields, so a line pointing elsewhere would be pointing at nothing.
            sourceBoundHint = new Label(
                "Clip Set and Rig follow the Clip Editor's own — change them in its top bar.");
            sourceBoundHint.style.whiteSpace = WhiteSpace.Normal;
            sourceBoundHint.style.display = DisplayStyle.None;
            root.Add(sourceBoundHint);

            // Not a field: which meshes a bake covers is a fact about the rig, not a fourth thing to keep in
            // step with it. This line is the receipt — what the rig resolved to, or why it did not.
            resolvedSourceLabel = new Label(string.Empty);
            resolvedSourceLabel.name = "vat-resolved-source-label";
            resolvedSourceLabel.AddToClassList("clip-editor__hint");
            resolvedSourceLabel.RegisterCallback<ClickEvent>(clickEvent => PingSourcePrefab());
            root.Add(resolvedSourceLabel);

            root.Add(BuildHeading("Settings"));

            flavorField = new EnumField("Flavor", VatFlavor.BoneMatrix)
            {
                tooltip = "Bone matrices are small and exact for skinned rigs. "
                    + "Vertex positions reproduce anything, including cloth and blendshapes."
            };
            root.Add(flavorField);

            sampleRateField = new FloatField("Fallback Samples / Second") { value = 30f };
            sampleRateField.tooltip =
                "Used only for a clip that carries no frame rate of its own. Every clip in a set "
                + "bakes at its own FPS — the field in the Clip Editor's transport bar — so a set "
                + "can hold a 12fps clip beside a 60fps one and each keeps its own row count.";
            root.Add(sampleRateField);

            fullPrecisionField = new Toggle("Full Precision (RGBAFloat)");
            fullPrecisionField.tooltip =
                "Doubles memory. Needed for rigs much larger than a couple of metres, where half "
                + "precision quantisation becomes visible as stepping.";
            root.Add(fullPrecisionField);

            root.Add(BuildHeading("Output"));

            // Left empty on purpose: a package must not hardcode a host's project folders, since
            // that would be wrong in every project organised differently.
            outputFolderField = new TextField("Output Folder")
            {
                value = string.Empty,
                tooltip = "Leave empty to write beside the clip set."
            };
            root.Add(outputFolderField);

            previewSetField = new ObjectField("Preview Set")
            {
                objectType = typeof(VatTextureSetAsset),
                allowSceneObjects = false,
                tooltip = "The baked set shown in the preview on the right. Filled automatically after a bake, or pick one by hand."
            };
            previewSetField.name = "vat-preview-set-field";
            previewSetField.RegisterValueChangedCallback(OnPreviewSetFieldChanged);
            root.Add(previewSetField);

            root.Add(BuildHeading("Bake"));

            Button bakeButton = new Button(Bake) { text = "Bake VAT Textures" };
            bakeButton.style.height = 28f;
            bakeButton.style.marginTop = 4f;
            root.Add(bakeButton);

            summaryLabel = new Label(string.Empty);
            summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            summaryLabel.style.marginTop = 8f;
            summaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(summaryLabel);

            logView = new ScrollView();
            logView.style.flexGrow = 1f;
            logView.style.marginTop = 4f;
            root.Add(logView);

            VisualElement previewPane = new VisualElement { name = "vat-bake-preview-pane" };
            previewPane.style.flexGrow = 1f;
            previewPane.style.minWidth = 320f;
            Add(previewPane);

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

        /// <summary>
        /// Binds what is baked to the host's own selection, disabling both fields so they follow
        /// the host rather than a stale local choice. <see cref="VatBakeWindow"/> never calls this.
        /// </summary>
        public void SetSource(ClipSetAsset clipSet, RigAsset rig)
        {
            if (clipSetField != null)
            {
                clipSetField.SetValueWithoutNotify(clipSet);
                clipSetField.SetEnabled(false);
            }
            if (rigField != null)
            {
                rigField.SetValueWithoutNotify(rig);
                rigField.SetEnabled(false);
            }
            if (sourceBoundHint != null)
            {
                sourceBoundHint.style.display = DisplayStyle.Flex;
            }
            RefreshResolvedSources();
        }

        private static Label BuildHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
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
            SkinnedMeshRenderer firstResolvedRenderer = resolvedSources != null && resolvedSources.Count > 0
                ? resolvedSources[0].PrefabRenderer
                : null;
            preview.Show(
                previewSetField.value as VatTextureSetAsset,
                clipSetField.value as ClipSetAsset,
                firstResolvedRenderer);
        }

        // Not called from Bake, which resolves fresh so a rig edited elsewhere between a refresh
        // and the button press cannot bake a stale set of parts. This is only the on-screen receipt.
        private void RefreshResolvedSources()
        {
            RigAsset rig = rigField.value as RigAsset;
            List<VatBakeSource> sources;
            string failureMessage;
            if (!VatBakeSourceResolver.TryResolve(rig, out sources, out failureMessage))
            {
                resolvedSources = null;
                resolvedSourceLabel.text = failureMessage;
                resolvedSourceLabel.style.color = new StyleColor(ToolkitPalette.Warning);
                RefreshPreview();
                return;
            }

            resolvedSources = sources;
            resolvedSourceLabel.style.color = StyleKeyword.Null;

            if (sources.Count == 1)
            {
                VatBakeSource onlySource = sources[0];
                int boneCount = onlySource.PrefabRenderer.bones == null ? 0 : onlySource.PrefabRenderer.bones.Length;
                resolvedSourceLabel.text = rig.sourcePrefab.name + " ▸ " + onlySource.DisplayName
                    + " · " + boneCount.ToString() + " bones";
                RefreshPreview();
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
                RefreshPreview();
                return;
            }

            VatBakePlan bakePlan = VatBakeClipBuilder.Build(clipSet, sources);
            List<string> bakedPartNames = new List<string>();
            for (int sourceIndex = 0; sourceIndex < bakePlan.Sources.Count; sourceIndex++)
            {
                bakedPartNames.Add(bakePlan.Sources[sourceIndex].Source.DisplayName);
            }

            string headline = "baking " + bakePlan.Sources.Count.ToString() + " of " + sources.Count.ToString()
                + " VAT parts · " + string.Join(", ", bakedPartNames);
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

            RefreshPreview();
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

            summaryLabel.text = "Baked " + partResults.Count.ToString() + " VAT part(s), "
                + totalClipRangeCount.ToString() + " clip range(s).";
            summaryLabel.style.color = new StyleColor(new Color(0.45f, 0.8f, 0.5f));

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
            summaryLabel.text = "Bake failed.";
            summaryLabel.style.color = new StyleColor(new Color(0.9f, 0.45f, 0.4f));
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
