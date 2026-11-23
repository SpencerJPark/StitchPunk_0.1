// Copyright (c) 2026 Spencer Park. All rights reserved.
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public struct CutoutIsland
    {
        public RectInt boundsPixels;
        public int opaquePixelCount;
    }

    public struct CutoutSheetFlipbookSuggestion
    {
        public List<int> islandIndices;
        public List<RectInt> cells;
    }

    // Pure pixel maths: masks are row-major with y = 0 as the bottom row, like Unity textures.
    public static class CutoutIslandFinder
    {
        public const int DefaultJoinDistancePixels = 3;
        public const int DefaultSizeTolerancePixels = 4;

        public static List<CutoutIsland> FindIslands(bool[] mask, Vector2Int size, int joinDistancePixels, int minimumOpaquePixels)
        {
            List<CutoutIsland> islands = new List<CutoutIsland>();
            if (mask == null || size.x <= 0 || size.y <= 0 || mask.Length < size.x * size.y)
            {
                return islands;
            }

            bool[] dilated = Dilate(mask, size, Mathf.Max(0, joinDistancePixels));
            bool[] visited = new bool[dilated.Length];
            Stack<int> pending = new Stack<int>();

            for (int startIndex = 0; startIndex < dilated.Length; startIndex++)
            {
                if (!dilated[startIndex] || visited[startIndex])
                {
                    continue;
                }

                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;
                int opaqueCount = 0;
                visited[startIndex] = true;
                pending.Push(startIndex);
                while (pending.Count > 0)
                {
                    int pixelIndex = pending.Pop();
                    int pixelX = pixelIndex % size.x;
                    int pixelY = pixelIndex / size.x;
                    if (mask[pixelIndex])
                    {
                        opaqueCount++;
                        minX = Mathf.Min(minX, pixelX);
                        minY = Mathf.Min(minY, pixelY);
                        maxX = Mathf.Max(maxX, pixelX);
                        maxY = Mathf.Max(maxY, pixelY);
                    }

                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        int neighbourY = pixelY + offsetY;
                        if (neighbourY < 0 || neighbourY >= size.y)
                        {
                            continue;
                        }
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            int neighbourX = pixelX + offsetX;
                            if (neighbourX < 0 || neighbourX >= size.x)
                            {
                                continue;
                            }
                            int neighbourIndex = neighbourY * size.x + neighbourX;
                            if (dilated[neighbourIndex] && !visited[neighbourIndex])
                            {
                                visited[neighbourIndex] = true;
                                pending.Push(neighbourIndex);
                            }
                        }
                    }
                }

                if (opaqueCount == 0 || opaqueCount < minimumOpaquePixels)
                {
                    continue;
                }
                islands.Add(new CutoutIsland
                {
                    boundsPixels = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1),
                    opaquePixelCount = opaqueCount,
                });
            }

            List<int> readingOrder = SortIntoReadingOrder(islands, out int[] rowIndices);
            List<CutoutIsland> ordered = new List<CutoutIsland>(islands.Count);
            for (int orderIndex = 0; orderIndex < readingOrder.Count; orderIndex++)
            {
                ordered.Add(islands[readingOrder[orderIndex]]);
            }
            return ordered;
        }

        public static List<CutoutSheetFlipbookSuggestion> SuggestSheetFlipbooks(IReadOnlyList<CutoutIsland> islands, int sizeTolerancePixels)
        {
            List<CutoutSheetFlipbookSuggestion> suggestions = new List<CutoutSheetFlipbookSuggestion>();
            if (islands == null || islands.Count < 2)
            {
                return suggestions;
            }

            List<int> readingOrder = SortIntoReadingOrder(islands, out int[] rowIndices);
            bool[] used = new bool[islands.Count];

            for (int seedOrder = 0; seedOrder < readingOrder.Count; seedOrder++)
            {
                int seedIndex = readingOrder[seedOrder];
                if (used[seedIndex])
                {
                    continue;
                }

                List<int> firstRun = BuildRun(islands, readingOrder, rowIndices, used, seedIndex, seedIndex, sizeTolerancePixels, out float firstPitch);
                if (firstRun.Count < 2)
                {
                    continue;
                }

                List<int> group = new List<int>(firstRun);
                foreach (int memberIndex in firstRun)
                {
                    used[memberIndex] = true;
                }

                float startCentreX = islands[seedIndex].boundsPixels.center.x;
                int lastRow = rowIndices[seedIndex];
                int maxRow = 0;
                for (int rowScan = 0; rowScan < rowIndices.Length; rowScan++)
                {
                    maxRow = Mathf.Max(maxRow, rowIndices[rowScan]);
                }
                for (int nextRow = lastRow + 1; nextRow <= maxRow; nextRow++)
                {
                    int rowStartIsland = -1;
                    foreach (int candidateIndex in readingOrder)
                    {
                        if (rowIndices[candidateIndex] == nextRow && !used[candidateIndex]
                            && IsSimilarSize(islands[seedIndex], islands[candidateIndex], sizeTolerancePixels)
                            && Mathf.Abs(islands[candidateIndex].boundsPixels.center.x - startCentreX) <= sizeTolerancePixels)
                        {
                            rowStartIsland = candidateIndex;
                            break;
                        }
                    }
                    if (rowStartIsland < 0)
                    {
                        continue;
                    }
                    List<int> rowRun = BuildRun(islands, readingOrder, rowIndices, used, rowStartIsland, seedIndex, sizeTolerancePixels, out float rowPitch);
                    if (rowRun.Count != firstRun.Count || Mathf.Abs(rowPitch - firstPitch) > sizeTolerancePixels)
                    {
                        continue;
                    }
                    foreach (int memberIndex in rowRun)
                    {
                        used[memberIndex] = true;
                        group.Add(memberIndex);
                    }
                }

                suggestions.Add(new CutoutSheetFlipbookSuggestion
                {
                    islandIndices = group,
                    cells = BuildCells(islands, group),
                });
            }
            return suggestions;
        }

        public static bool[] CropMask(bool[] mask, Vector2Int size, RectInt rect)
        {
            int cropWidth = Mathf.Max(0, rect.width);
            int cropHeight = Mathf.Max(0, rect.height);
            bool[] cropped = new bool[cropWidth * cropHeight];
            for (int rowIndex = 0; rowIndex < cropHeight; rowIndex++)
            {
                int sourceY = rect.y + rowIndex;
                if (sourceY < 0 || sourceY >= size.y)
                {
                    continue;
                }
                for (int columnIndex = 0; columnIndex < cropWidth; columnIndex++)
                {
                    int sourceX = rect.x + columnIndex;
                    if (sourceX < 0 || sourceX >= size.x)
                    {
                        continue;
                    }
                    cropped[rowIndex * cropWidth + columnIndex] = mask[sourceY * size.x + sourceX];
                }
            }
            return cropped;
        }

        // A piece's rect on a packed sheet usually catches a neighbour's leaves too; keep only the biggest joined island.
        public static bool[] KeepLargestIsland(bool[] mask, Vector2Int size, int joinDistancePixels)
        {
            bool[] dilated = Dilate(mask, size, Mathf.Max(0, joinDistancePixels));
            int[] labels = new int[mask.Length];
            Stack<int> pending = new Stack<int>();
            int nextLabel = 0;
            int largestLabel = 0;
            int largestOpaqueCount = 0;
            for (int startIndex = 0; startIndex < mask.Length; startIndex++)
            {
                if (!dilated[startIndex] || labels[startIndex] != 0)
                {
                    continue;
                }
                nextLabel++;
                int opaqueCount = 0;
                labels[startIndex] = nextLabel;
                pending.Push(startIndex);
                while (pending.Count > 0)
                {
                    int pixelIndex = pending.Pop();
                    if (mask[pixelIndex])
                    {
                        opaqueCount++;
                    }
                    int pixelX = pixelIndex % size.x;
                    int pixelY = pixelIndex / size.x;
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        int neighbourY = pixelY + offsetY;
                        if (neighbourY < 0 || neighbourY >= size.y)
                        {
                            continue;
                        }
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            int neighbourX = pixelX + offsetX;
                            if (neighbourX < 0 || neighbourX >= size.x)
                            {
                                continue;
                            }
                            int neighbourIndex = neighbourY * size.x + neighbourX;
                            if (dilated[neighbourIndex] && labels[neighbourIndex] == 0)
                            {
                                labels[neighbourIndex] = nextLabel;
                                pending.Push(neighbourIndex);
                            }
                        }
                    }
                }
                if (opaqueCount > largestOpaqueCount)
                {
                    largestOpaqueCount = opaqueCount;
                    largestLabel = nextLabel;
                }
            }

            bool[] isolated = new bool[mask.Length];
            for (int pixelIndex = 0; pixelIndex < mask.Length; pixelIndex++)
            {
                isolated[pixelIndex] = mask[pixelIndex] && labels[pixelIndex] == largestLabel;
            }
            return isolated;
        }

        static bool[] Dilate(bool[] mask, Vector2Int size, int distance)
        {
            if (distance == 0)
            {
                return mask;
            }

            bool[] horizontal = new bool[size.x * size.y];
            for (int rowIndex = 0; rowIndex < size.y; rowIndex++)
            {
                int rowStart = rowIndex * size.x;
                for (int columnIndex = 0; columnIndex < size.x; columnIndex++)
                {
                    if (!mask[rowStart + columnIndex])
                    {
                        continue;
                    }
                    int from = Mathf.Max(0, columnIndex - distance);
                    int to = Mathf.Min(size.x - 1, columnIndex + distance);
                    for (int spreadIndex = from; spreadIndex <= to; spreadIndex++)
                    {
                        horizontal[rowStart + spreadIndex] = true;
                    }
                }
            }

            bool[] both = new bool[horizontal.Length];
            for (int rowIndex = 0; rowIndex < size.y; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < size.x; columnIndex++)
                {
                    if (!horizontal[rowIndex * size.x + columnIndex])
                    {
                        continue;
                    }
                    int from = Mathf.Max(0, rowIndex - distance);
                    int to = Mathf.Min(size.y - 1, rowIndex + distance);
                    for (int spreadIndex = from; spreadIndex <= to; spreadIndex++)
                    {
                        both[spreadIndex * size.x + columnIndex] = true;
                    }
                }
            }
            return both;
        }

        // Rows are bands of islands whose vertical ranges overlap the band's first (tallest-top) island.
        static List<int> SortIntoReadingOrder(IReadOnlyList<CutoutIsland> islands, out int[] rowIndices)
        {
            List<int> byTop = new List<int>(islands.Count);
            for (int islandIndex = 0; islandIndex < islands.Count; islandIndex++)
            {
                byTop.Add(islandIndex);
            }
            byTop.Sort((int left, int right) =>
            {
                int topCompare = islands[right].boundsPixels.yMax.CompareTo(islands[left].boundsPixels.yMax);
                return topCompare != 0 ? topCompare : islands[left].boundsPixels.x.CompareTo(islands[right].boundsPixels.x);
            });

            rowIndices = new int[islands.Count];
            List<List<int>> rows = new List<List<int>>();
            RectInt anchorBounds = default;
            foreach (int islandIndex in byTop)
            {
                RectInt bounds = islands[islandIndex].boundsPixels;
                bool startsNewRow = rows.Count == 0 || bounds.yMax <= anchorBounds.yMin;
                if (startsNewRow)
                {
                    rows.Add(new List<int>());
                    anchorBounds = bounds;
                }
                rows[rows.Count - 1].Add(islandIndex);
                rowIndices[islandIndex] = rows.Count - 1;
            }

            List<int> ordered = new List<int>(islands.Count);
            foreach (List<int> row in rows)
            {
                row.Sort((int left, int right) => islands[left].boundsPixels.x.CompareTo(islands[right].boundsPixels.x));
                ordered.AddRange(row);
            }
            return ordered;
        }

        static bool IsSimilarSize(CutoutIsland reference, CutoutIsland candidate, int tolerance)
        {
            return Mathf.Abs(reference.boundsPixels.width - candidate.boundsPixels.width) <= tolerance
                && Mathf.Abs(reference.boundsPixels.height - candidate.boundsPixels.height) <= tolerance;
        }

        // Walks right along the start island's row, keeping similar-size islands that sit on an equal pitch.
        static List<int> BuildRun(IReadOnlyList<CutoutIsland> islands, List<int> readingOrder, int[] rowIndices, bool[] used,
            int startIsland, int sizeReferenceIsland, int tolerance, out float pitch)
        {
            List<int> run = new List<int> { startIsland };
            pitch = 0f;
            float lastCentreX = islands[startIsland].boundsPixels.center.x;
            foreach (int candidateIndex in readingOrder)
            {
                if (candidateIndex == startIsland || used[candidateIndex] || rowIndices[candidateIndex] != rowIndices[startIsland]
                    || islands[candidateIndex].boundsPixels.x < islands[startIsland].boundsPixels.x
                    || !IsSimilarSize(islands[sizeReferenceIsland], islands[candidateIndex], tolerance))
                {
                    continue;
                }
                float candidatePitch = islands[candidateIndex].boundsPixels.center.x - lastCentreX;
                if (run.Count == 1)
                {
                    pitch = candidatePitch;
                }
                else if (Mathf.Abs(candidatePitch - pitch) > tolerance)
                {
                    break;
                }
                run.Add(candidateIndex);
                lastCentreX = islands[candidateIndex].boundsPixels.center.x;
            }
            return run;
        }

        static List<RectInt> BuildCells(IReadOnlyList<CutoutIsland> islands, List<int> group)
        {
            int cellWidth = 0;
            int cellHeight = 0;
            foreach (int islandIndex in group)
            {
                cellWidth = Mathf.Max(cellWidth, islands[islandIndex].boundsPixels.width);
                cellHeight = Mathf.Max(cellHeight, islands[islandIndex].boundsPixels.height);
            }

            List<RectInt> cells = new List<RectInt>(group.Count);
            foreach (int islandIndex in group)
            {
                Vector2 centre = islands[islandIndex].boundsPixels.center;
                cells.Add(new RectInt(
                    Mathf.RoundToInt(centre.x - cellWidth * 0.5f),
                    Mathf.RoundToInt(centre.y - cellHeight * 0.5f),
                    cellWidth,
                    cellHeight));
            }
            return cells;
        }
    }
}
