// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Ratchet against new inline VisualElement.style assignments (colours, fonts, borders,
    /// radii) landing outside <see cref="InlineStyleAllowlist"/> instead of the shared USS.
    /// </summary>
    public sealed class EditorStyleConformanceTests
    {
        private static readonly Regex InlineVisualStylePattern = new Regex(
            "style\\.(backgroundColor|color|opacity|fontSize|unityFontStyleAndWeight|unityTextAlign|" +
            "border(Top|Bottom|Left|Right)Color|border(Top|Bottom|Left|Right)Width|" +
            "border(TopLeft|TopRight|BottomLeft|BottomRight)Radius|unityBackgroundImageTintColor)\\s*=",
            RegexOptions.Compiled);

        // Files whose inline visual styles are not yet converted. Shrink only: an entry may be removed when
        // its file is clean, never added.
        private static readonly HashSet<string> InlineStyleAllowlist = new HashSet<string>
        {
            "Editor/ClipEditor/Components/EventPayloadFieldBuilder.cs",
            "Editor/ClipEditor/Cutscene/CutsceneMomentLaneElement.cs",
            "Editor/ClipEditor/ActorEditor/LayerEventStripElement.cs",
            "Editor/ClipEditor/Components/EventMarkerInspectorElement.cs",
            "Editor/ClipEditor/Cutscene/CutsceneTimeRulerElement.cs",
            "Editor/ClipEditor/Panes/TimelinePane.cs",
            "Editor/ClipEditor/TimeRulerElement.cs",
            "Editor/SpriteSheets/SpriteSheetPreviewElement.cs",
            "Editor/TexturePacker/PackOutputNodeView.cs",
            "Editor/TexturePacker/SourceImageNodeView.cs",
            "Editor/ClipEditor/Preview/RagdollPreviewSceneryProvider.cs",
            "Editor/TexturePacker/TexturePackerGraphView.cs",
            "Editor/TexturePacker/TexturePackPortBuilder.cs",
            "Editor/ClipEditor/ActorEditor/ActorEditorInspectorColumn.cs",
            "Editor/ClipEditor/ClipEditorWindow.cs",
        };

        // Maps package-relative path to its non-exempt inline-style match count. Only files with
        // count > 0 are present.
        private static Dictionary<string, int> ScanInlineVisualStyleViolationCounts()
        {
            Dictionary<string, int> violationCounts = new Dictionary<string, int>();
            string editorFolderPath = Path.Combine(PackagingConformanceTests.PackageRootPath, "Editor");
            string[] editorSourceFiles;
            if (Directory.Exists(editorFolderPath))
            {
                editorSourceFiles = Directory.GetFiles(editorFolderPath, "*.cs", SearchOption.AllDirectories);
            }
            else
            {
                editorSourceFiles = new string[0];
            }

            foreach (string editorSourceFile in editorSourceFiles)
            {
                string packageRelativePath = PackagingConformanceTests.ToPackageRelativePath(editorSourceFile);
                if (packageRelativePath.Contains("Editor/Inspectors/"))
                {
                    continue;
                }

                string rawText = File.ReadAllText(editorSourceFile);
                string strippedText = PackagingConformanceTests.StripComments(rawText);
                string[] rawLines = rawText.Split('\n');
                string[] strippedLines = strippedText.Split('\n');

                // StripComments preserves newlines, so counts should always match; fall back to raw
                // lines only (matching and exempting off the same, unstripped text) if they ever don't.
                string[] matchLines = strippedLines.Length == rawLines.Length ? strippedLines : rawLines;

                int matchCount = 0;
                for (int lineIndex = 0; lineIndex < matchLines.Length; lineIndex++)
                {
                    if (!InlineVisualStylePattern.IsMatch(matchLines[lineIndex]))
                    {
                        continue;
                    }

                    string rawLine = lineIndex < rawLines.Length ? rawLines[lineIndex] : matchLines[lineIndex];
                    if (rawLine.TrimEnd().EndsWith("// colour from data"))
                    {
                        continue;
                    }

                    matchCount++;
                }

                if (matchCount > 0)
                {
                    violationCounts[packageRelativePath] = matchCount;
                }
            }

            return violationCounts;
        }

        [Test]
        public void Conformance_I_NoInlineVisualStyles_OutsideTheAllowlist()
        {
            Dictionary<string, int> violationCounts = ScanInlineVisualStyleViolationCounts();
            List<string> violations = new List<string>();
            foreach (KeyValuePair<string, int> violationCount in violationCounts)
            {
                if (!InlineStyleAllowlist.Contains(violationCount.Key))
                {
                    violations.Add(violationCount.Key + ":" + violationCount.Value);
                }
            }

            Assert.IsEmpty(
                violations,
                "Colours, opacity, fonts, borders and radii belong in ClipEditorWindow.uss as a toolkit-* " +
                "class, and a colour that genuinely comes from data ends its line with " +
                "\"// colour from data\": " + string.Join(", ", violations));
        }

        [Test]
        public void Conformance_I_AllowlistEntriesStillNeedListing()
        {
            Dictionary<string, int> violationCounts = ScanInlineVisualStyleViolationCounts();
            List<string> staleEntries = new List<string>();
            foreach (string allowlistEntry in InlineStyleAllowlist)
            {
                string fullPath = Path.Combine(PackagingConformanceTests.PackageRootPath, allowlistEntry);
                bool hasViolations = violationCounts.ContainsKey(allowlistEntry) && violationCounts[allowlistEntry] > 0;
                if (!File.Exists(fullPath) || !hasViolations)
                {
                    staleEntries.Add(allowlistEntry + " (remove it from the allowlist)");
                }
            }

            Assert.IsEmpty(
                staleEntries,
                "Allowlist entries with no remaining inline visual styles are stale: " +
                string.Join(", ", staleEntries));
        }
    }
}
