// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using System.Text;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The VAT bake window (architecture section 7.1) — the UI shell over
    /// <see cref="VatTextureBaker"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The window decides nothing.</strong> Every rule about what gets baked lives in the
    /// data: a clip is VAT-bound when its <c>ClipAsset</c> names a <c>vatSource.sourceClip</c>
    /// (amendment A36), and its id comes from the clip asset. That keeps the window and a headless
    /// batch bake producing identical output, which is the whole reason
    /// <see cref="VatTextureBaker.Bake"/> is a plain static taking a struct.
    /// </para>
    /// <para>
    /// UI Toolkit rather than IMGUI, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>.
    /// </para>
    /// </remarks>
    public sealed class VatBakeWindow : EditorWindow
    {
        private ObjectField clipSetField;
        private ObjectField skinnedRendererField;
        private EnumField flavorField;
        private FloatField sampleRateField;
        private Toggle fullPrecisionField;
        private TextField outputFolderField;
        private Label summaryLabel;
        private ScrollView logView;

        [MenuItem("Window/DOTS Animation Toolkit/VAT Bake")]
        public static void ShowWindow()
        {
            VatBakeWindow window = GetWindow<VatBakeWindow>();
            window.titleContent = new GUIContent("VAT Bake");
            window.minSize = new Vector2(420f, 380f);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
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

            sampleRateField = new FloatField("Samples / Second") { value = 30f };
            sampleRateField.tooltip =
                "Frames baked per second of clip. Higher is smoother and larger; the shader lerps "
                + "between frames, so this is not the playback framerate.";
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
                    + "vatSource.sourceClip on the ClipAssets you want baked — that field is what "
                    + "marks a clip as VAT-bound (amendment A36).");
                return;
            }

            VatBakeInput bakeInput = new VatBakeInput
            {
                skinnedMeshRenderer = renderer,
                flavor = (VatFlavor)flavorField.value,
                samplesPerSecond = sampleRateField.value,
                useFullPrecision = fullPrecisionField.value,
                clips = bakeClips
            };

            VatBakeResult bakeResult;
            if (!VatTextureBaker.Bake(bakeInput, out bakeResult))
            {
                ReportFailure(bakeResult.message);
                return;
            }

            string setPath = SaveResult(clipSet, bakeResult, bakeInput.flavor);
            ReportSuccess(bakeResult, bakeClips, setPath);
        }

        /// <summary>
        /// The clips a set wants baked: those whose <c>ClipAsset</c> names a source clip.
        /// </summary>
        /// <remarks>
        /// Ids come from the <c>ClipAsset</c>, never minted here. A texture set whose ranges do not
        /// match the registry's clip ids is a set that resolves to nothing at runtime, and the
        /// failure is silent — <c>VatMaterialSystem</c> simply holds the last frame.
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
                if (clip == null || clip.vatSource == null || clip.vatSource.sourceClip == null)
                {
                    continue;
                }

                bakeClips.Add(new VatBakeClip
                {
                    clipId = clip.Id.Value,
                    animationClip = clip.vatSource.sourceClip,
                    loopSafe = clip.vatSource.loopSafe
                });
            }
            return bakeClips;
        }

        private string SaveResult(ClipSetAsset clipSet, VatBakeResult bakeResult, VatFlavor flavor)
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
                detail.AppendLine(
                    "clip 0x" + range.clipId.ToString("X16")
                    + "  frames " + range.frameStart.ToString()
                    + ".." + (range.frameStart + range.frameCount - 1).ToString()
                    + "  @" + range.fps.ToString() + "fps");
            }

            AppendLog(detail.ToString());
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
