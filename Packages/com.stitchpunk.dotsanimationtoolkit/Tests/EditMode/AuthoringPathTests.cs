// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Collections;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>AuthoringPathText.RenderPath</c> — the half of the diagnostic path builder that
    /// turns a hierarchy's node names into text small enough for a <see cref="FixedString128Bytes"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This code had no coverage at all when it shipped, and it runs on <em>every</em> successfully
    /// bound rig part. The bug that absence hid was not subtle: it budgeted 110 <em>characters</em>
    /// against a capacity of 125 UTF-8 <em>bytes</em> and then used the throwing
    /// <c>FixedString128Bytes(string)</c> constructor, so a hierarchy of about 42 CJK characters —
    /// well under the character guard — overflowed and threw <c>ArgumentException</c> out of
    /// <c>RigTargetBaker.Bake</c>, leaving the part a half-built entity. Every fixture in the suite
    /// was short and ASCII, so nothing noticed.
    /// </para>
    /// <para>
    /// The tests below therefore lean on the non-ASCII and boundary cases rather than on the happy
    /// path: what this method must never do is throw, whatever it is handed.
    /// </para>
    /// </remarks>
    public sealed class AuthoringPathTests
    {
        /// <summary>UTF-8 payload capacity of a <see cref="FixedString128Bytes"/>.</summary>
        private const int MaximumPathBytes = 125;

        /// <summary>A three-byte-per-character CJK ideograph.</summary>
        private const string CjkCharacter = "体";

        /// <summary>A four-byte astral character, i.e. a UTF-16 surrogate pair.</summary>
        private const string AstralCharacter = "\U0001F600";

        [Test]
        public void RenderPath_JoinsNodesRootFirst_FromALeafFirstWalk()
        {
            // The walk that feeds this method runs leaf to root, because that is the direction a
            // Transform chain is navigable. The text has to read the other way round.
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(
                new List<string> { "Leaf", "Child", "Root" });

            Assert.AreEqual("Root/Child/Leaf", renderedPath.ToString());
        }

        [Test]
        public void RenderPath_OnASingleNode_EmitsTheBareNameWithNoSeparator()
        {
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(new List<string> { "Actor" });

            Assert.AreEqual("Actor", renderedPath.ToString());
        }

        [Test]
        public void RenderPath_OnNoNodes_IsEmptyRatherThanASeparator()
        {
            Assert.AreEqual(string.Empty, AuthoringPathText.RenderPath(new List<string>()).ToString());
        }

        [Test]
        public void RenderPath_OnANullList_IsEmpty()
        {
            // PathOf returns default for a null transform, and this is the same contract one level
            // down. A path is diagnostic text; there is no failure to report about not having one.
            Assert.AreEqual(string.Empty, AuthoringPathText.RenderPath(null).ToString());
        }

        [Test]
        public void RenderPath_OnALongAsciiPath_TruncatesFromTheLeftAndKeepsTheLeaf()
        {
            // Truncation drops the outermost ancestors, never the leaf: ".../Torso/LeftArm" locates
            // the part, "SceneRoot/Rig/..." does not.
            List<string> deepPath = new List<string> { "TheLeafThatMustSurvive" };
            for (int ancestorIndex = 0; ancestorIndex < 20; ancestorIndex++)
            {
                deepPath.Add("Ancestor" + ancestorIndex.ToString());
            }

            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(deepPath);
            string renderedText = renderedPath.ToString();

            StringAssert.StartsWith(".../", renderedText, "A truncated path must say that it is one.");
            StringAssert.EndsWith(
                "TheLeafThatMustSurvive",
                renderedText,
                "The leaf is the whole point of the message. Truncating from the right would leave " +
                "the user with the part of the path they could already guess.");
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(renderedText), MaximumPathBytes);
        }

        [Test]
        public void RenderPath_OnAShortPath_DoesNotTruncate()
        {
            // The false-positive guard for the test above: a truncator that fired unconditionally
            // would satisfy every assertion there.
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(
                new List<string> { "LeftArm", "Torso", "Rig" });

            Assert.AreEqual("Rig/Torso/LeftArm", renderedPath.ToString());
            StringAssert.DoesNotStartWith(".../", renderedPath.ToString());
        }

        [Test]
        public void RenderPath_OnANonAsciiPathUnderTheOldCharacterGuard_DoesNotThrow()
        {
            // THE regression test. Sixty CJK characters is 180 UTF-8 bytes but only 60 chars, so a
            // character-counted guard of 110 never fires and the throwing FixedString constructor
            // gets a string half again over its capacity. This is an ordinary hierarchy for any
            // studio that does not name its GameObjects in English.
            List<string> japanesePath = new List<string>
            {
                Repeat(CjkCharacter, 20),
                Repeat(CjkCharacter, 20),
                Repeat(CjkCharacter, 20)
            };

            FixedString128Bytes renderedPath = default;
            Assert.DoesNotThrow(
                () => { renderedPath = AuthoringPathText.RenderPath(japanesePath); },
                "Rendering a non-ASCII path must never throw. It runs inside RigTargetBaker.Bake, " +
                "so an exception here abandons the part mid-bake: no rest pose, no output pose, no " +
                "technique components — a hard bake failure caused purely by the user's choice of " +
                "GameObject names, on a code path that exists only to print a nicer error message.");
            Assert.LessOrEqual(
                Encoding.UTF8.GetByteCount(renderedPath.ToString()),
                MaximumPathBytes,
                "The budget is UTF-8 bytes, not characters.");
            Assert.IsNotEmpty(
                renderedPath.ToString(),
                "Degrading to a shortened message is the intended failure mode; degrading to no " +
                "message at all is not.");
        }

        [Test]
        public void RenderPath_OnANonAsciiPath_StillKeepsTheLeafReadable()
        {
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(
                new List<string> { "左腕", Repeat(CjkCharacter, 40), Repeat(CjkCharacter, 40) });
            string renderedText = renderedPath.ToString();

            StringAssert.EndsWith("左腕", renderedText, "The leaf survives truncation in any script.");
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(renderedText), MaximumPathBytes);
        }

        [Test]
        public void RenderPath_OnAPathOfSurrogatePairs_NeverEmitsALoneSurrogate()
        {
            // Trimming by UTF-16 code unit can land between the halves of an astral character,
            // leaving a lone surrogate at the start of the retained text — which encodes to a
            // replacement character and makes the first thing the user reads mojibake.
            //
            // THE INPUT IS LOAD-BEARING. This fixture originally used two 20-astral nodes and
            // discriminated nothing: delete the low-surrogate step-back in TakeTrailingBytes and it
            // still passed. With pure-astral nodes the naive walk measures each lone surrogate at
            // three bytes, so it consumes floor((121 - 6k - 1) / 3) = 40 - 2k code units of the
            // parent — always EVEN, so it always lands on a pair boundary and never splits one. The
            // parity is structural, not luck; no pure-astral input can catch this bug.
            //
            // One ASCII character in the leaf breaks that parity. With leaf "x" + 5 astral, the
            // naive walk spends 30 bytes on the leaf's surrogates, 1 on the 'x' and 1 on the '/',
            // leaving 89 of the 121-byte budget — floor(89 / 3) = 29 code units, ODD, so it stops
            // mid-pair and the retained text opens with a lone low surrogate. The shipped
            // implementation instead steps back onto the high surrogate and keeps 24 whole
            // characters, 122 bytes.
            //
            // Only the lone-surrogate loop discriminates. The byte-budget assertion does not: the
            // broken walk over-counts lone surrogates at three bytes each and so retains LESS than
            // the budget allows (85 bytes), staying comfortably inside the limit while emitting
            // mojibake. Verified by simulating both variants before this fixture was written.
            List<string> emojiPath = new List<string>
            {
                "x" + Repeat(AstralCharacter, 5),
                Repeat(AstralCharacter, 30)
            };

            FixedString128Bytes renderedPath = default;
            Assert.DoesNotThrow(() => { renderedPath = AuthoringPathText.RenderPath(emojiPath); });
            string renderedText = renderedPath.ToString();

            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(renderedText), MaximumPathBytes);
            for (int characterIndex = 0; characterIndex < renderedText.Length; characterIndex++)
            {
                if (char.IsHighSurrogate(renderedText[characterIndex]))
                {
                    Assert.IsTrue(
                        characterIndex + 1 < renderedText.Length &&
                        char.IsLowSurrogate(renderedText[characterIndex + 1]),
                        "A high surrogate at index " + characterIndex.ToString() + " has no partner.");
                    characterIndex++;
                    continue;
                }
                Assert.IsFalse(
                    char.IsLowSurrogate(renderedText[characterIndex]),
                    "A lone low surrogate at index " + characterIndex.ToString() +
                    " means truncation cut an astral character in half.");
            }
        }

        [Test]
        public void RenderPath_OnAPathExactlyAtCapacity_IsNotTruncated()
        {
            // The off-by-one boundary. 125 bytes fits; the guard must be > and not >=, or a path
            // that fits perfectly still gets a ".../" prefix and loses four bytes of ancestry.
            string exactlyFullName = Repeat("a", MaximumPathBytes);
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(
                new List<string> { exactlyFullName });

            Assert.AreEqual(exactlyFullName, renderedPath.ToString());
        }

        [Test]
        public void RenderPath_OnAPathOneByteOverCapacity_Truncates()
        {
            string oneOverName = Repeat("a", MaximumPathBytes + 1);
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(
                new List<string> { oneOverName });
            string renderedText = renderedPath.ToString();

            StringAssert.StartsWith(".../", renderedText);
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(renderedText), MaximumPathBytes);
        }

        [Test]
        public void RenderPath_OnAnEmptyNodeName_StillSeparatesTheNodesAroundIt()
        {
            // A GameObject can be named "". The path should still show the shape of the hierarchy
            // rather than silently closing the gap and naming the wrong ancestor.
            FixedString128Bytes renderedPath = AuthoringPathText.RenderPath(
                new List<string> { "Leaf", string.Empty, "Root" });

            Assert.AreEqual("Root//Leaf", renderedPath.ToString());
        }

        private static string Repeat(string unit, int count)
        {
            StringBuilder builder = new StringBuilder(unit.Length * count);
            for (int repetitionIndex = 0; repetitionIndex < count; repetitionIndex++)
            {
                builder.Append(unit);
            }
            return builder.ToString();
        }
    }
}
