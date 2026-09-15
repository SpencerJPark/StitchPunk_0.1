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
            "Editor/Flipbooks/FlipbookPreviewElement.cs",
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

        private const string ToolkitComponentsSheetRelativePath = "Editor/ClipEditor/Shared/ToolkitComponents.uss";
        private const string ToolkitTokensSheetRelativePath = "Editor/ClipEditor/Shared/ToolkitTokens.uss";
        private const string WindowSheetRelativePath = "Editor/ClipEditor/ClipEditorWindow.uss";

        private static readonly Regex ColourLiteralPattern = new Regex(
            @"rgba?\(|#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);

        private static readonly Regex PixelFontSizePattern = new Regex(
            @"font-size:\s*(\d+)px", RegexOptions.Compiled);

        private static readonly Regex CssCommentPattern = new Regex(
            @"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly HashSet<int> TypeScalePixelSizes = new HashSet<int> { 11, 12, 13, 16 };

        // Shrink-only: lower both when a cleanup removes literals, never raise.
        private const int WindowSheetColourLiteralPin = 151;
        private const int WindowSheetNonScaleFontSizePin = 5;

        private static readonly string[] RequiredTokenNames =
        {
            "--toolkit-surface",
            "--toolkit-window",
            "--toolkit-raised",
            "--toolkit-divider",
            "--toolkit-field",
            "--toolkit-field-border",
            "--toolkit-text",
            "--toolkit-label",
            "--toolkit-color-muted",
            "--toolkit-selection",
            "--toolkit-hover",
            "--toolkit-color-hairline",
            "--toolkit-color-focus",
            "--toolkit-text-meta",
            "--toolkit-text-body",
            "--toolkit-text-title",
            "--toolkit-text-detail",
            "--toolkit-height-field",
            "--toolkit-height-row",
            "--toolkit-height-control",
            "--toolkit-height-primary",
            "--toolkit-height-pane-header",
            "--toolkit-height-asset-bar",
            "--toolkit-inset",
            "--toolkit-radius-field",
            "--toolkit-radius-button",
            "--toolkit-radius-card",
            "--toolkit-radius-track",
        };

        private static int CountNonScaleFontSizes(string sheetText)
        {
            int nonScaleCount = 0;
            foreach (Match fontSizeMatch in PixelFontSizePattern.Matches(sheetText))
            {
                int fontSizePixels = int.Parse(fontSizeMatch.Groups[1].Value);
                if (!TypeScalePixelSizes.Contains(fontSizePixels))
                {
                    nonScaleCount++;
                }
            }

            return nonScaleCount;
        }

        [Test]
        public void Conformance_J_ToolkitComponentSheet_UsesTokensAndTheTypeScale()
        {
            string componentsSheetPath = Path.Combine(PackagingConformanceTests.PackageRootPath, ToolkitComponentsSheetRelativePath);
            string tokensSheetPath = Path.Combine(PackagingConformanceTests.PackageRootPath, ToolkitTokensSheetRelativePath);
            Assert.IsTrue(File.Exists(componentsSheetPath), "ToolkitComponents.uss not found at " + componentsSheetPath);
            Assert.IsTrue(File.Exists(tokensSheetPath), "ToolkitTokens.uss not found at " + tokensSheetPath);

            string componentsText = CssCommentPattern.Replace(File.ReadAllText(componentsSheetPath), string.Empty);

            List<string> colourLiteralViolations = new List<string>();
            foreach (Match colourMatch in ColourLiteralPattern.Matches(componentsText))
            {
                colourLiteralViolations.Add(colourMatch.Value);
            }

            Assert.IsEmpty(
                colourLiteralViolations,
                "ToolkitComponents.uss draws colours from var(--toolkit-*) or var(--unity-colors-*) tokens, never literals: " +
                string.Join(", ", colourLiteralViolations));

            List<string> nonScaleFontSizeViolations = new List<string>();
            foreach (Match fontSizeMatch in PixelFontSizePattern.Matches(componentsText))
            {
                int fontSizePixels = int.Parse(fontSizeMatch.Groups[1].Value);
                if (!TypeScalePixelSizes.Contains(fontSizePixels))
                {
                    nonScaleFontSizeViolations.Add(fontSizeMatch.Value);
                }
            }

            Assert.IsEmpty(
                nonScaleFontSizeViolations,
                "font sizes are 11/12/13/16px (use var(--toolkit-text-*)): " +
                string.Join(", ", nonScaleFontSizeViolations));

            string tokensText = CssCommentPattern.Replace(File.ReadAllText(tokensSheetPath), string.Empty);
            List<string> missingTokenNames = new List<string>();
            foreach (string requiredTokenName in RequiredTokenNames)
            {
                if (!Regex.IsMatch(tokensText, Regex.Escape(requiredTokenName) + @"\s*:"))
                {
                    missingTokenNames.Add(requiredTokenName);
                }
            }

            Assert.IsEmpty(
                missingTokenNames,
                "ToolkitTokens.uss is missing tokens: " + string.Join(", ", missingTokenNames));
        }

        [Test]
        public void Conformance_J_WindowSheet_LiteralRatchet()
        {
            string windowSheetPath = Path.Combine(PackagingConformanceTests.PackageRootPath, WindowSheetRelativePath);
            Assert.IsTrue(File.Exists(windowSheetPath), "ClipEditorWindow.uss not found at " + windowSheetPath);

            string windowText = File.ReadAllText(windowSheetPath);
            int colourLiteralCount = ColourLiteralPattern.Matches(windowText).Count;
            int nonScaleFontSizeCount = CountNonScaleFontSizes(windowText);

            Assert.LessOrEqual(
                colourLiteralCount,
                WindowSheetColourLiteralPin,
                "ClipEditorWindow.uss gained a colour literal (" + colourLiteralCount + " > pin); use a var(--unity-colors-*) or --toolkit-* token");
            Assert.LessOrEqual(
                nonScaleFontSizeCount,
                WindowSheetNonScaleFontSizePin,
                "ClipEditorWindow.uss gained a non-scale font size (" + nonScaleFontSizeCount + " > pin); use var(--toolkit-text-*)");

            Assert.AreEqual(
                WindowSheetColourLiteralPin,
                colourLiteralCount,
                "the window sheet has fewer colour literals than its pin; lower WindowSheetColourLiteralPin to " + colourLiteralCount);
            Assert.AreEqual(
                WindowSheetNonScaleFontSizePin,
                nonScaleFontSizeCount,
                "the window sheet has fewer non-scale font sizes than its pin; lower WindowSheetNonScaleFontSizePin to " + nonScaleFontSizeCount);
        }
    }
}
