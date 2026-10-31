using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainRoadGraphBuilderTests
    {
        [Test]
        public void TJunction_ProducesFourNodesAndThreeEdges()
        {
            RoadGraphData graph = TerrainRoadGraphBuilder.Build(new List<Vector3[]>
            {
                SampleLine(new Vector3(-50f, 0f, 0f), new Vector3(50f, 0f, 0f)),
                SampleLine(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 40f))
            });

            Assert.AreEqual(4, graph.nodePositions.Count);
            Assert.AreEqual(3, graph.edges.Count);
        }

        [Test]
        public void XCrossing_ProducesFiveNodesAndFourEdges()
        {
            RoadGraphData graph = TerrainRoadGraphBuilder.Build(new List<Vector3[]>
            {
                SampleLine(new Vector3(-50f, 0f, 0f), new Vector3(50f, 0f, 0f)),
                SampleLine(new Vector3(0f, 0f, -50f), new Vector3(0f, 0f, 50f))
            });

            Assert.AreEqual(5, graph.nodePositions.Count);
            Assert.AreEqual(4, graph.edges.Count);
        }

        [Test]
        public void DisjointRoads_ShareNoNodes()
        {
            RoadGraphData graph = TerrainRoadGraphBuilder.Build(new List<Vector3[]>
            {
                SampleLine(new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f)),
                SampleLine(new Vector3(0f, 0f, 100f), new Vector3(100f, 0f, 100f))
            });

            Assert.AreEqual(4, graph.nodePositions.Count);
            Assert.AreEqual(2, graph.edges.Count);
            HashSet<int> firstEdgeNodes = new HashSet<int> { graph.edges[0].fromNode, graph.edges[0].toNode };
            Assert.IsFalse(firstEdgeNodes.Contains(graph.edges[1].fromNode));
            Assert.IsFalse(firstEdgeNodes.Contains(graph.edges[1].toNode));
        }

        [Test]
        public void CurvedRoad_EdgeLengthMatchesQuarterCircleArc()
        {
            const float radius = 60f;
            const int sampleCount = 95;
            Vector3[] arc = new Vector3[sampleCount];
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float angle = (Mathf.PI * 0.5f) * sampleIndex / (sampleCount - 1);
                arc[sampleIndex] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            RoadGraphData graph = TerrainRoadGraphBuilder.Build(new List<Vector3[]> { arc });

            Assert.AreEqual(2, graph.nodePositions.Count);
            Assert.AreEqual(1, graph.edges.Count);
            Assert.AreEqual(Mathf.PI * 30f, graph.edges[0].lengthMetres, Mathf.PI * 30f * 0.01f);
        }

        [Test]
        public void StraightRoad_SimplifiesToTwoPoints()
        {
            RoadGraphData graph = TerrainRoadGraphBuilder.Build(new List<Vector3[]>
            {
                SampleLine(new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f))
            });

            Assert.AreEqual(1, graph.edges.Count);
            Assert.AreEqual(2, graph.edges[0].points.Length);
        }

        [Test]
        public void EndpointsOneMetreApart_ShareOneNode()
        {
            RoadGraphData graph = TerrainRoadGraphBuilder.Build(new List<Vector3[]>
            {
                SampleLine(new Vector3(-50f, 0f, 0f), new Vector3(0f, 0f, 0f)),
                SampleLine(new Vector3(1f, 0f, 0f), new Vector3(50f, 0f, 0f))
            });

            Assert.AreEqual(3, graph.nodePositions.Count);
            Assert.AreEqual(2, graph.edges.Count);
        }

        private static Vector3[] SampleLine(Vector3 start, Vector3 end)
        {
            int segmentCount = Mathf.CeilToInt(Vector3.Distance(start, end));
            Vector3[] samples = new Vector3[segmentCount + 1];
            for (int sampleIndex = 0; sampleIndex <= segmentCount; sampleIndex++)
            {
                samples[sampleIndex] = Vector3.Lerp(start, end, (float)sampleIndex / segmentCount);
            }
            return samples;
        }
    }
}
