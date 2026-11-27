using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerraceCaseContourTests
    {
        private const float CellSizeMetres = 2f;
        private const float Tolerance = 1e-4f;

        private static readonly Vector2[] CornerPositions =
        {
            new Vector2(0f, 2f), new Vector2(2f, 2f), new Vector2(2f, 0f), new Vector2(0f, 0f)
        };

        private static bool CornerIsInside(int caseIndex, int cornerIndex)
        {
            return (caseIndex & (1 << cornerIndex)) != 0;
        }

        [Test]
        public void EveryCrossingSideMidpointIsOnTheContour()
        {
            for (int caseIndex = 1; caseIndex <= 14; caseIndex++)
            {
                for (int sideStart = 0; sideStart < 4; sideStart++)
                {
                    int sideEnd = (sideStart + 1) % 4;
                    if (CornerIsInside(caseIndex, sideStart) == CornerIsInside(caseIndex, sideEnd))
                    {
                        continue;
                    }
                    Vector2 midpoint = (CornerPositions[sideStart] + CornerPositions[sideEnd]) * 0.5f;
                    float distance = TerraceCaseContour.SignedDistanceInCell(caseIndex, midpoint);
                    Assert.Less(Mathf.Abs(distance), Tolerance,
                        "case " + caseIndex + " side " + sideStart + "-" + sideEnd + " midpoint " + midpoint + " gave " + distance);
                }

                for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
                {
                    Vector2 towardCentre = (new Vector2(1f, 1f) - CornerPositions[cornerIndex]).normalized;
                    Vector2 samplePoint = CornerPositions[cornerIndex] + towardCentre * 0.05f;
                    float distance = TerraceCaseContour.SignedDistanceInCell(caseIndex, samplePoint);
                    if (CornerIsInside(caseIndex, cornerIndex))
                    {
                        Assert.Greater(distance, 0f, "case " + caseIndex + " inside corner " + cornerIndex + " at " + samplePoint + " gave " + distance);
                    }
                    else
                    {
                        Assert.Less(distance, 0f, "case " + caseIndex + " outside corner " + cornerIndex + " at " + samplePoint + " gave " + distance);
                    }
                }
            }
        }

        [Test]
        public void NeighbouringCellsAgreeOnTheSharedSide()
        {
            for (int leftCase = 0; leftCase < 16; leftCase++)
            {
                for (int rightCase = 0; rightCase < 16; rightCase++)
                {
                    bool topMatches = CornerIsInside(leftCase, 1) == CornerIsInside(rightCase, 0);
                    bool bottomMatches = CornerIsInside(leftCase, 2) == CornerIsInside(rightCase, 3);
                    if (!topMatches || !bottomMatches)
                    {
                        continue;
                    }
                    for (int sampleStep = 0; sampleStep <= 16; sampleStep++)
                    {
                        float z = sampleStep * 0.125f;
                        float leftValue = TerraceCaseContour.SignedDistanceInCell(leftCase, new Vector2(CellSizeMetres, z));
                        float rightValue = TerraceCaseContour.SignedDistanceInCell(rightCase, new Vector2(0f, z));
                        Assert.AreEqual(leftValue, rightValue, Tolerance,
                            "left case " + leftCase + " right case " + rightCase + " at z " + z);
                    }
                }
            }

            for (int bottomCase = 0; bottomCase < 16; bottomCase++)
            {
                for (int topCase = 0; topCase < 16; topCase++)
                {
                    bool leftMatches = CornerIsInside(bottomCase, 0) == CornerIsInside(topCase, 3);
                    bool rightMatches = CornerIsInside(bottomCase, 1) == CornerIsInside(topCase, 2);
                    if (!leftMatches || !rightMatches)
                    {
                        continue;
                    }
                    for (int sampleStep = 0; sampleStep <= 16; sampleStep++)
                    {
                        float x = sampleStep * 0.125f;
                        float bottomValue = TerraceCaseContour.SignedDistanceInCell(bottomCase, new Vector2(x, CellSizeMetres));
                        float topValue = TerraceCaseContour.SignedDistanceInCell(topCase, new Vector2(x, 0f));
                        Assert.AreEqual(bottomValue, topValue, Tolerance,
                            "bottom case " + bottomCase + " top case " + topCase + " at x " + x);
                    }
                }
            }
        }
    }
}
