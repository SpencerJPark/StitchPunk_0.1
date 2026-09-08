// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Guards that ToolkitPalette and the shared stylesheet's --toolkit-color-* tokens agree,
    /// and that ColorForEventKey is a stable, table-covering hash.
    /// </summary>
    public sealed class ToolkitPaletteTests
    {
        private const string PackageId = "com.dotsanimationtoolkit";
        private const string StylesheetRelativePath = "Editor/ClipEditor/ClipEditorWindow.uss";

        private static readonly Regex TokenPattern = new Regex(
            @"--toolkit-color-([a-z-]+):\s*rgba?\(([^)]*)\)",
            RegexOptions.Compiled);

        [Test]
        public void UssTokens_MatchToolkitPalette()
        {
            string stylesheetPath = Path.Combine(
                Path.GetFullPath("Packages/" + PackageId), StylesheetRelativePath);
            Assert.IsTrue(File.Exists(stylesheetPath), "Missing stylesheet: " + stylesheetPath);

            string stylesheetText = File.ReadAllText(stylesheetPath);
            Dictionary<string, Color> parsedTokens = new Dictionary<string, Color>();
            foreach (Match tokenMatch in TokenPattern.Matches(stylesheetText))
            {
                string tokenName = tokenMatch.Groups[1].Value;
                string[] channelParts = tokenMatch.Groups[2].Value.Split(',');
                float redChannel = float.Parse(channelParts[0].Trim(), CultureInfo.InvariantCulture);
                float greenChannel = float.Parse(channelParts[1].Trim(), CultureInfo.InvariantCulture);
                float blueChannel = float.Parse(channelParts[2].Trim(), CultureInfo.InvariantCulture);
                float alphaChannel = channelParts.Length > 3
                    ? float.Parse(channelParts[3].Trim(), CultureInfo.InvariantCulture)
                    : 1f;
                parsedTokens[tokenName] = new Color(
                    redChannel / 255f, greenChannel / 255f, blueChannel / 255f, alphaChannel);
            }

            foreach (KeyValuePair<string, Color> paletteEntry in ToolkitPalette.Tokens)
            {
                Assert.IsTrue(
                    parsedTokens.ContainsKey(paletteEntry.Key),
                    "Stylesheet is missing token --toolkit-color-" + paletteEntry.Key);

                Color parsedColor = parsedTokens[paletteEntry.Key];
                Color32 paletteColor32 = paletteEntry.Value;
                int parsedRedChannel = Mathf.RoundToInt(parsedColor.r * 255f);
                int parsedGreenChannel = Mathf.RoundToInt(parsedColor.g * 255f);
                int parsedBlueChannel = Mathf.RoundToInt(parsedColor.b * 255f);

                Assert.AreEqual(paletteColor32.r, parsedRedChannel, "Token " + paletteEntry.Key + " red channel mismatch");
                Assert.AreEqual(paletteColor32.g, parsedGreenChannel, "Token " + paletteEntry.Key + " green channel mismatch");
                Assert.AreEqual(paletteColor32.b, parsedBlueChannel, "Token " + paletteEntry.Key + " blue channel mismatch");

                float expectedAlpha = (float)Math.Round(paletteEntry.Value.a, 2);
                float parsedAlpha = (float)Math.Round(parsedColor.a, 2);
                Assert.AreEqual(expectedAlpha, parsedAlpha, 0.001f, "Token " + paletteEntry.Key + " alpha mismatch");
            }

            foreach (string parsedTokenName in parsedTokens.Keys)
            {
                Assert.IsTrue(
                    ToolkitPalette.Tokens.ContainsKey(parsedTokenName),
                    "Stylesheet declares unexpected token --toolkit-color-" + parsedTokenName);
            }
        }

        [Test]
        public void ColorForEventKey_IsStableAndUsesEveryPaletteEntry()
        {
            HashSet<Color> hitColors = new HashSet<Color>();
            for (uint eventKey = 16; eventKey <= 1015; eventKey += 1)
            {
                Color firstColor = ToolkitPalette.ColorForEventKey(eventKey);
                Color secondColor = ToolkitPalette.ColorForEventKey(eventKey);
                Assert.AreEqual(firstColor, secondColor, "Event key " + eventKey + " is not stable");
                hitColors.Add(firstColor);
            }

            Assert.AreEqual(
                ToolkitPalette.EventColors.Length,
                hitColors.Count,
                "Not every palette entry was hit across event keys 16..1015");

            foreach (Color eventColor in ToolkitPalette.EventColors)
            {
                Assert.IsTrue(hitColors.Contains(eventColor), "A palette entry was never produced by ColorForEventKey");
            }
        }
    }
}
