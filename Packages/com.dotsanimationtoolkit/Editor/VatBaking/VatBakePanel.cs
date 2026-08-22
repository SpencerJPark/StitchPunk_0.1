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
    /// The VAT bake UI (architecture section 7.1) — the shell over <see cref="VatTextureBaker"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The panel decides nothing.</strong> Every rule about what gets baked lives in the
    /// data: a clip is VAT-bound when its <c>ClipAsset</c> names a <c>vatSource.sourceClip</c>
    /// (amendment A36), and its id comes from the clip asset. That keeps the panel and a headless
    /// batch bake producing identical output, which is the whole reason
    /// <see cref="VatTextureBaker.Bake"/> is a plain static taking a struct.
    /// </para>
    /// <para>
    /// <strong>An element rather than a window, so two hosts can show the same thing.</strong>
    /// <see cref="VatBakeWindow"/> is one of them; the Clip Editor's VAT Bake tab is the other, and
    /// a bake produced from either is the same bake. Splitting the two would mean two places to fix
    /// every time the bake grew a setting, and the second one would be the one nobody updated.
    /// </para>
    /// <para>
    /// UI Toolkit rather than IMGUI, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>.
    /// </para>
    /// </remarks>
    public sealed class VatBakePanel : VisualElement
    {
        private ObjectField clipSetField;
        private ObjectField skinnedRendererField;
        private EnumField flavorField;
        private FloatField sampleRateField;
        private Toggle fullPrecisionField;
        private TextField outputFolderField;
        private Label summaryLabel;
        private ScrollView logView;

        public VatBakePanel()
        {
            // Written inline rather than through a stylesheet because this element carries no
            // stylesheet of its own: it is added to whatever host asks for it, and a host's sheet
            // has no reason to know the names of the rows inside a bake panel.
            VisualElement root = this;
            root.style.flexGrow = 1f;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 8f;

            root.Add(BuildHeading("Source"));

            clipSetField = new ObjectField("Clip Set")
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                tooltip = "Clips whose ClipAsset names a VAT source clip are baked. Others are skipped."
            };
            root.Add(clipSetField);

            skinnedRendererField = new ObjectField("Skinned Mesh")
            {
                objectType = typeof(SkinnedMeshRenderer),
                allowSceneObjects = true,
                tooltip = "The rig to sample. Must be in an open scene — baking poses it."
            };
            root.Add(skinnedRendererField);

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
                + "precision quantisation becomes visible as stepping (risk R2).";
            root.Add(fullPrecisionField);

            // Left empty on purpose. A package must not name a host's project folders — the
            // conformance scan forbids it, and rightly: a hardcoded default would be wrong in every
            // project that organises differently. Empty means "beside the clip set", which is where
            // a bake belongs anyway.
            outputFolderField = new TextField("Output Folder")
            {
                value = string.Empty,
                tooltip = "Leave empty to write beside the clip set."
            };
            root.Add(outputFolderField);

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
        }

        /// <summary>
        /// Offers a clip set to bake, without overruling one the user has already picked here.
        /// </summary>
        /// <remarks>
        /// <strong>Only fills an empty field.</strong> A host that pushed its own selection in every
        /// time would make this field impossible to hold: switch to the tab, correct it, switch away
        /// to check something, and the correction is gone. Filling the blank is the helpful half of
        /// the behaviour and the half that cannot destroy an intention.
        /// </remarks>
        public void OfferClipSet(ClipSetAsset clipSet)
        {
            if (clipSet == null || clipSetField == null || clipSetField.value != null)
            {
                return;
            }
            clipSetField.value = clipSet;
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
            SkinnedMeshRenderer renderer = skinnedRendererField.value as SkinnedMeshRenderer;

            if (clipSet == null)
            {
                ReportFailure("Assign a Clip Set.");
                return;
            }
            if (renderer == null)
            {
                ReportFailure("Assign a Skinned Mesh Renderer from an open scene.");
                return;
            }

            List<VatBakeClip> bakeClips = CollectVatClips(clipSet);
            if (bakeClips.Count == 0)
            {
                ReportFailure(
                    "No clip in '" + clipSet.name + "' names a VAT source. Set "
                    + "vatSource.sourceClip on the ClipAssets you want baked (amendment A36), or add "
                    + "a vatTracks entry naming a target and a source clip for a target-scoped VAT "
                    + "part (C10), or author bone tracks in the Clip Editor (A42) — any of those "
                    + "marks a clip as VAT-bound.");
                return;
            }

            VatBakeInput bakeInput = new VatBakeInput
            {
                skinnedMeshRenderer = renderer,
                flavor = (VatFlavor)flavorField.value,
                samplesPerSecond = sampleRateField.value,
                useFullPrecision = fullPrecisionField.value,
                clips = bakeClips,
                sockets = CollectBoneSockets(clipSet.rig)
            };

            VatBakeResult bakeResult;
            if (!VatTextureBaker.Bake(bakeInput, out bakeResult))
            {
                ReportFailure(bakeResult.message);
                return;
            }

            // Same reasoning as the socket warning below: the textures are valid, but every listed
            // bone stayed at its rest pose, which presents as an animation that simply does not
            // play rather than as an error anyone would go looking for.
            if (bakeResult.unresolvedBoneTrackNames != null && bakeResult.unresolvedBoneTrackNames.Count > 0)
            {
                Debug.LogWarning(
                    "VAT bake could not resolve " + bakeResult.unresolvedBoneTrackNames.Count.ToString()
                    + " authored bone track name(s) in the source hierarchy: "
                    + string.Join(", ", bakeResult.unresolvedBoneTrackNames)
                    + ". Those bones baked at rest. Check the names on the clip's bone tracks "
                    + "against the skinned mesh you assigned.");
            }

            // Surfaced as a warning, not a failure: the textures are valid and usable, but every
            // listed socket would sit at the actor origin, which is not something to discover later
            // by watching a sword hover at a character's feet.
            if (bakeResult.unresolvedSocketBones != null && bakeResult.unresolvedSocketBones.Count > 0)
            {
                Debug.LogWarning(
                    "VAT bake could not resolve " + bakeResult.unresolvedSocketBones.Count.ToString()
                    + " socket bone(s) in the source hierarchy: "
                    + string.Join(", ", bakeResult.unresolvedSocketBones)
                    + ". Check the bone names on the rig's socket rows.");
            }

            string setPath = SaveResult(clipSet, bakeResult, bakeInput.flavor, renderer);
            ReportSuccess(bakeResult, bakeClips, setPath);
        }

        /// <summary>
        /// The clips a set wants baked: one entry per clip's untargeted <c>vatSource</c> when it
        /// names a source clip, plus one further entry per <c>vatTracks</c> row that names a source
        /// clip (C10) — so one <c>ClipAsset</c> can contribute several bake passes, each occupying its
        /// own frame block in the same texture.
        /// </summary>
        /// <remarks>
        /// Ids come from the <c>ClipAsset</c> and its tracks, never minted here. A texture set whose
        /// ranges do not match the registry's clip/target ids is a set that resolves to nothing at
        /// runtime, and the failure is silent — <c>VatMaterialSystem</c> simply holds the last frame.
        /// A <c>vatTracks</c> row with no source clip yet (added in the inspector but not filled in)
        /// carries no VAT intent and is skipped, exactly like an empty <c>vatSource</c> — the same
        /// rule <c>ClipValidation</c>'s coverage check applies when deciding whether a clip counts as
        /// VAT-sourced.
        /// </remarks>
        private static List<VatBakeClip> CollectVatClips(ClipSetAsset clipSet)
        {
            List<VatBakeClip> bakeClips = new List<VatBakeClip>();
            if (clipSet.clips == null)
            {
                return bakeClips;
            }

            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                int boneTrackCount = clip.boneTracks == null ? 0 : clip.boneTracks.Count;
                bool hasImportedSource = clip.vatSource != null && clip.vatSource.sourceClip != null;

                // Authored bone tracks make a clip VAT-bound on their own (amendment A42), so a
                // clip animated entirely inside the Clip Editor bakes without ever naming an
                // imported AnimationClip. When it has both, one bake pass carries both and the
                // baker applies the authored keys on top.
                if (hasImportedSource || boneTrackCount > 0)
                {
                    bakeClips.Add(new VatBakeClip
                    {
                        clipId = clip.Id.Value,
                        targetId = 0u,
                        animationClip = hasImportedSource ? clip.vatSource.sourceClip : null,
                        boneTracks = clip.boneTracks,
                        durationSeconds = clip.duration,
                        // The clip's own FPS, so its block of the texture is exactly the frames the
                        // Clip Editor rules its timeline into. A set of clips no longer has to
                        // share one rate to share one texture.
                        samplesPerSecond = clip.frameRate,
                        loopSafe = hasImportedSource && clip.vatSource.loopSafe
                    });
                }

                int vatTrackCount = clip.vatTracks == null ? 0 : clip.vatTracks.Count;
                for (int trackIndex = 0; trackIndex < vatTrackCount; trackIndex++)
                {
                    VatTrack track = clip.vatTracks[trackIndex];
                    if (track == null || track.sourceClip == null)
                    {
                        continue;
                    }

                    bakeClips.Add(new VatBakeClip
                    {
                        clipId = clip.Id.Value,
                        targetId = track.targetId,
                        animationClip = track.sourceClip,
                        // A target-scoped part is a block of the same clip and plays on the same
                        // clock, so it bakes at the same rate the clip does.
                        samplesPerSecond = clip.frameRate,
                        loopSafe = track.loopSafe
                    });
                }
            }
            return bakeClips;
        }

        private string SaveResult(
            ClipSetAsset clipSet,
            VatBakeResult bakeResult,
            VatFlavor flavor,
            SkinnedMeshRenderer sourceRenderer)
        {
            string outputFolder = ResolveOutputFolder(clipSet);
            EnsureFolderPath(outputFolder);

            string baseName = clipSet.name + "Vat";
            bool isBoneFlavor = flavor == VatFlavor.BoneMatrix;

            string texturePath = outputFolder + "/" + baseName + (isBoneFlavor ? "Bone" : "Position") + ".asset";
            CreateOrReplaceAsset(bakeResult.boneOrPositionTexture, texturePath);

            if (bakeResult.normalTexture != null)
            {
                CreateOrReplaceAsset(bakeResult.normalTexture, outputFolder + "/" + baseName + "Normal.asset");
            }

            VatTextureSetAsset textureSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            textureSet.flavor = flavor;
            if (isBoneFlavor)
            {
                textureSet.boneTexture = bakeResult.boneOrPositionTexture;
            }
            else
            {
                textureSet.positionTexture = bakeResult.boneOrPositionTexture;
                textureSet.normalTexture = bakeResult.normalTexture;
            }
            textureSet.boneCount = bakeResult.boneCount;
            textureSet.vertexCount = bakeResult.vertexCount;
            textureSet.textureWidth = bakeResult.textureWidth;
            textureSet.rowsPerFrame = bakeResult.rowsPerFrame;
            textureSet.sourceHash = bakeResult.sourceHash;
            textureSet.clipRanges = bakeResult.clipRanges;
            textureSet.socketTracks = bakeResult.socketTracks;

            // Bone flavour only: a vertex-flavour shader reads baked positions and never touches
            // bone influences, so packing them would be dead weight in the vertex stream.
            if (isBoneFlavor)
            {
                Mesh runtimeMesh;
                string meshFailureMessage;
                if (VatMeshPreparer.TryCreateRuntimeMesh(sourceRenderer, out runtimeMesh, out meshFailureMessage))
                {
                    CreateOrReplaceAsset(runtimeMesh, outputFolder + "/" + baseName + "RuntimeMesh.asset");
                    textureSet.runtimeMesh = runtimeMesh;
                }
                else
                {
                    // Warned rather than failed: the textures are valid and a host may already have
                    // its own prepared mesh. But a null runtimeMesh renders as a motionless clump
                    // rather than an error, so silence here would be the worst option.
                    Debug.LogWarning(
                        "VAT bake produced no runtime mesh: " + meshFailureMessage +
                        " The shader needs bone influences in UV1 — see the package's Documentation~/shader-contract.md.");
                }
            }

            string setPath = outputFolder + "/" + baseName + "Set.asset";
            CreateOrReplaceAsset(textureSet, setPath);

            // Assigning the set back onto the clip set is what clears validation rule V07 — a clip
            // with a VAT source and a set with no textures is an error that bakes no registry at all.
            clipSet.vatTextures = textureSet;
            EditorUtility.SetDirty(clipSet);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return setPath;
        }

        private void ReportSuccess(VatBakeResult bakeResult, List<VatBakeClip> bakeClips, string setPath)
        {
            summaryLabel.text = "Baked " + bakeClips.Count.ToString() + " clip(s).";
            summaryLabel.style.color = new StyleColor(new Color(0.45f, 0.8f, 0.5f));

            StringBuilder detail = new StringBuilder();
            detail.AppendLine(bakeResult.message);
            detail.AppendLine("Texture set: " + setPath);
            detail.AppendLine("Source hash: 0x" + bakeResult.sourceHash.ToString("X16"));
            detail.AppendLine();
            for (int rangeIndex = 0; rangeIndex < bakeResult.clipRanges.Count; rangeIndex++)
            {
                VatClipRange range = bakeResult.clipRanges[rangeIndex];
                string targetLabel = range.targetId == 0u
                    ? string.Empty
                    : "  target 0x" + range.targetId.ToString("X8");
                detail.AppendLine(
                    "clip 0x" + range.clipId.ToString("X16")
                    + targetLabel
                    + "  frames " + range.frameStart.ToString()
                    + ".." + (range.frameStart + range.frameCount - 1).ToString()
                    + "  @" + range.fps.ToString() + "fps");
            }

            AppendLog(detail.ToString());
        }

        /// <summary>
        /// Gathers the rig's bone sockets for the bake.
        /// </summary>
        /// <remarks>
        /// Rig-target sockets are deliberately excluded: their motion is the part's own transform,
        /// computed live every frame by the sampler, so baking it would store a second copy that
        /// could only ever go stale.
        /// </remarks>
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

        private static void EnsureFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string accumulated = segments[0];
            for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                string next = accumulated + "/" + segments[segmentIndex];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(accumulated, segments[segmentIndex]);
                }
                accumulated = next;
            }
        }

        private static void CreateOrReplaceAsset(Object asset, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
