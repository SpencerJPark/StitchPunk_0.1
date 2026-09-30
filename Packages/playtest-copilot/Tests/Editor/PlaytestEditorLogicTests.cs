using System;
using System.Collections.Generic;
using NUnit.Framework;
using PlaytestCopilot.Editor;
using UnityEngine;

namespace PlaytestCopilot.Tests.Editor
{
    /// Pure-logic coverage for the path-building and key-clash-detection helpers that everything
    /// else in the package writes through. No Unity World, no scene, no asset writes.
    [TestFixture]
    public sealed class PlaytestEditorLogicTests
    {
        [Test]
        public void BuildSessionId_CombinesTimestampAndSceneName()
        {
            DateTime startedLocal = new DateTime(2026, 9, 28, 18, 8, 0);

            string sessionId = PlaytestSessionPaths.BuildSessionId(startedLocal, "Level1");

            Assert.AreEqual("2026-09-28_1808_Level1", sessionId);
        }

        [Test]
        public void BuildSessionId_ReturnsJustTheTimestamp_WhenSceneNameIsNullOrEmpty()
        {
            DateTime startedLocal = new DateTime(2026, 9, 28, 18, 8, 0);

            Assert.AreEqual("2026-09-28_1808", PlaytestSessionPaths.BuildSessionId(startedLocal, null));
            Assert.AreEqual("2026-09-28_1808", PlaytestSessionPaths.BuildSessionId(startedLocal, string.Empty));
        }

        [Test]
        public void SanitiseForFolderName_StripsSpacesSlashesAndColons()
        {
            string sanitisedName = PlaytestSessionPaths.SanitiseForFolderName("Level 1: Intro");

            Assert.AreEqual("Level1Intro", sanitisedName);
        }

        [Test]
        public void BuildSessionId_NeverContainsCharactersIllegalInAFolderName()
        {
            DateTime startedLocal = new DateTime(2026, 9, 28, 18, 8, 0);

            string sessionId = PlaytestSessionPaths.BuildSessionId(startedLocal, "Level 1: Intro");

            Assert.AreEqual("2026-09-28_1808_Level1Intro", sessionId);
            Assert.IsFalse(sessionId.Contains(" "));
            Assert.IsFalse(sessionId.Contains("/"));
            Assert.IsFalse(sessionId.Contains(":"));
        }

        [Test]
        public void BuildMarkerId_PadsMarkerIndexToThreeDigits()
        {
            Assert.AreEqual("note_007", PlaytestSessionPaths.BuildMarkerId(7));
            Assert.AreEqual("note_014", PlaytestSessionPaths.BuildMarkerId(14));
            Assert.AreEqual("note_123", PlaytestSessionPaths.BuildMarkerId(123));
        }

        [Test]
        public void RelativeFromSessionFolder_ConvertsAnAbsolutePathInsideTheFolderToAForwardSlashedRelativePath()
        {
            string sessionFolder = "C:/PlaytestSessions/2026-09-28_1808_Level1";
            string absolutePathWithBackslashes = "C:\\PlaytestSessions\\2026-09-28_1808_Level1\\notes\\note_001\\frame.png";

            string relativePath = PlaytestSessionPaths.RelativeFromSessionFolder(sessionFolder, absolutePathWithBackslashes);

            Assert.AreEqual("notes/note_001/frame.png", relativePath);
        }

        [Test]
        public void RelativeFromSessionFolder_IsCaseInsensitiveOnTheRootFolder()
        {
            string sessionFolder = "C:/PlaytestSessions/2026-09-28_1808_Level1";
            string absolutePathWithDifferentRootCase = "C:\\PLAYTESTSESSIONS\\2026-09-28_1808_LEVEL1\\notes\\note_001\\frame.png";

            string relativePath = PlaytestSessionPaths.RelativeFromSessionFolder(sessionFolder, absolutePathWithDifferentRootCase);

            Assert.AreEqual("notes/note_001/frame.png", relativePath);
        }

        [Test]
        public void RelativeFromSessionFolder_ReturnsTheNormalisedPathUnchanged_WhenItIsOutsideTheFolder()
        {
            string sessionFolder = "C:/PlaytestSessions/2026-09-28_1808_Level1";
            string absolutePathOutsideFolder = "D:\\SomeOtherFolder\\file.txt";

            string result = PlaytestSessionPaths.RelativeFromSessionFolder(sessionFolder, absolutePathOutsideFolder);

            Assert.AreEqual("D:/SomeOtherFolder/file.txt", result);
        }

        [Test]
        public void Normalise_ConvertsBackslashesToForwardSlashesAndTrimsTheTrailingSlash()
        {
            string normalisedPath = PlaytestSessionPaths.Normalise("C:\\PlaytestSessions\\Level1\\");

            Assert.AreEqual("C:/PlaytestSessions/Level1", normalisedPath);
        }

        [Test]
        public void InputSystemPathFor_ReturnsTheBackquoteBindingPath_ForBackQuoteKey()
        {
            Assert.AreEqual("<Keyboard>/backquote", PlaytestRecordKeyClashDetector.InputSystemPathFor(KeyCode.BackQuote));
        }

        [Test]
        public void InputSystemPathFor_ReturnsTheLowercaseLetterBindingPath_ForALetterKey()
        {
            Assert.AreEqual("<Keyboard>/g", PlaytestRecordKeyClashDetector.InputSystemPathFor(KeyCode.G));
        }

        [Test]
        public void InputSystemPathFor_ReturnsEmptyString_ForAnUnmappedKeyCode()
        {
            Assert.AreEqual(string.Empty, PlaytestRecordKeyClashDetector.InputSystemPathFor(KeyCode.Joystick1Button0));
        }

        [Test]
        public void FindClashes_ReturnsAnEmptyNonNullList_ForAnUnmappedKeyCode()
        {
            List<PlaytestKeyClash> clashes = PlaytestRecordKeyClashDetector.FindClashes(KeyCode.Joystick1Button0);

            Assert.IsNotNull(clashes);
            Assert.AreEqual(0, clashes.Count);
        }
    }
}
