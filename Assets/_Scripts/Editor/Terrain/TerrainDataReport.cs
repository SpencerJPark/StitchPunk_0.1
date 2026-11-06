using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

public static class TerrainDataReport
{
    const double BytesPerMegabyte = 1024.0 * 1024.0;
    const double ResidentBudgetMegabytes = 150.0;
    const double DiskBudgetMegabytesPerBakedTile = 1.2;
    const int BakedMaskResolution = 256;
    const int ResidentRingTileCount = 9;

    sealed class TileReport
    {
        public Vector2Int coordinate;
        public int maskResolution;
        public long maskBytes;
        public long signedDistanceFieldBytes;
        public long dataBytes;
        public long meshBytes;
        public long sceneBytes;
        public long otherBytes;
        public long residentBytes;

        public long TotalDiskBytes
        {
            get { return maskBytes + signedDistanceFieldBytes + dataBytes + meshBytes + sceneBytes + otherBytes; }
        }
    }

    [MenuItem("Stitch Punk/Terrain/Report Data Size")]
    static void ReportDataSize()
    {
        List<TileReport> tileReports = new List<TileReport>();
        string[] tileDataGuids = AssetDatabase.FindAssets("t:TerrainTileData");
        for (int guidIndex = 0; guidIndex < tileDataGuids.Length; guidIndex++)
        {
            string tileDataAssetPath = AssetDatabase.GUIDToAssetPath(tileDataGuids[guidIndex]);
            TerrainTileData tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(tileDataAssetPath);
            if (tileData == null)
                continue;
            tileReports.Add(BuildTileReport(tileData, tileDataAssetPath));
        }

        if (tileReports.Count == 0)
        {
            Debug.Log("Terrain data report: no TerrainTileData assets found.");
            return;
        }

        tileReports.Sort(CompareByCoordinate);

        StringBuilder table = new StringBuilder();
        table.AppendLine("Terrain data report (disk MB per tile, by layer)");
        table.AppendLine("tile      res   masks   sdf     data    meshes  scene   other   total   resident");
        long totalDiskBytes = 0;
        List<string> failures = new List<string>();
        for (int tileIndex = 0; tileIndex < tileReports.Count; tileIndex++)
        {
            TileReport report = tileReports[tileIndex];
            totalDiskBytes += report.TotalDiskBytes;
            table.AppendLine(string.Format("{0,-9} {1,-5} {2,-7} {3,-7} {4,-7} {5,-7} {6,-7} {7,-7} {8,-7} {9}",
                report.coordinate.x + "," + report.coordinate.y, report.maskResolution,
                FormatMegabytes(report.maskBytes), FormatMegabytes(report.signedDistanceFieldBytes),
                FormatMegabytes(report.dataBytes), FormatMegabytes(report.meshBytes),
                FormatMegabytes(report.sceneBytes), FormatMegabytes(report.otherBytes),
                FormatMegabytes(report.TotalDiskBytes), FormatMegabytes(report.residentBytes)));

            if (report.maskResolution == BakedMaskResolution
                && report.TotalDiskBytes / BytesPerMegabyte > DiskBudgetMegabytesPerBakedTile)
            {
                failures.Add("FAIL disk budget: tile " + report.coordinate + " is "
                    + FormatMegabytes(report.TotalDiskBytes) + " MB, budget " + DiskBudgetMegabytesPerBakedTile.ToString("F1") + " MB");
            }
            else if (report.maskResolution == BakedMaskResolution)
            {
                table.AppendLine("  PASS disk budget: tile " + report.coordinate + " " + FormatMegabytes(report.TotalDiskBytes) + " MB");
            }
        }
        table.AppendLine("Total disk: " + FormatMegabytes(totalDiskBytes) + " MB across " + tileReports.Count + " tiles");

        List<TileReport> ring = new List<TileReport>(tileReports);
        ring.Sort(CompareByDistanceToOrigin);
        int ringCount = Math.Min(ResidentRingTileCount, ring.Count);
        long ringResidentBytes = 0;
        for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
            ringResidentBytes += ring[ringIndex].residentBytes;
        if (ring.Count < ResidentRingTileCount)
            table.AppendLine("Only " + ring.Count + " tiles exist; resident estimate covers all of them, not a full 3x3 ring.");
        double ringResidentMegabytes = ringResidentBytes / BytesPerMegabyte;
        table.AppendLine("Resident estimate, " + ringCount + " tiles nearest (0,0): " + FormatMegabytes(ringResidentBytes) + " MB");
        if (ringResidentMegabytes > ResidentBudgetMegabytes)
            failures.Add("FAIL resident budget: " + FormatMegabytes(ringResidentBytes) + " MB, budget " + ResidentBudgetMegabytes.ToString("F1") + " MB");
        else
            table.AppendLine("PASS resident budget: " + FormatMegabytes(ringResidentBytes) + " MB <= " + ResidentBudgetMegabytes.ToString("F1") + " MB");

        Debug.Log(table.ToString());
        for (int failureIndex = 0; failureIndex < failures.Count; failureIndex++)
            Debug.LogWarning(failures[failureIndex]);
        Debug.Log(failures.Count == 0
            ? "Terrain data report verdict: PASS"
            : "Terrain data report verdict: FAIL (" + failures.Count + " budget breaches)");
    }

    static TileReport BuildTileReport(TerrainTileData tileData, string tileDataAssetPath)
    {
        TileReport report = new TileReport();
        report.coordinate = tileData.coordinate;
        report.maskResolution = tileData.maskResolution;

        HashSet<string> maskPaths = new HashSet<string>();
        HashSet<string> signedDistanceFieldPaths = new HashSet<string>();
        AddAssetPath(maskPaths, tileData.baseMask);
        AddAssetPath(maskPaths, tileData.paintOverlay);
        AddAssetPath(maskPaths, tileData.roadMask);
        AddAssetPath(maskPaths, tileData.surfaceOverlay);
        AddAssetPath(signedDistanceFieldPaths, tileData.compositeSdf);
        string signedDistanceFieldPath = TerrainTileAssets.SignedDistanceFieldPath(tileData.coordinate);
        if (File.Exists(signedDistanceFieldPath))
            signedDistanceFieldPaths.Add(signedDistanceFieldPath);

        HashSet<string> counted = new HashSet<string>();
        List<string> candidatePaths = new List<string>();
        candidatePaths.Add(tileDataAssetPath);
        candidatePaths.AddRange(maskPaths);
        candidatePaths.AddRange(signedDistanceFieldPaths);
        string folder = TerrainTileAssets.FolderOf(tileData.coordinate);
        if (AssetDatabase.IsValidFolder(folder))
        {
            string[] folderGuids = AssetDatabase.FindAssets(string.Empty, new string[] { folder });
            for (int guidIndex = 0; guidIndex < folderGuids.Length; guidIndex++)
                candidatePaths.Add(AssetDatabase.GUIDToAssetPath(folderGuids[guidIndex]));
        }

        for (int pathIndex = 0; pathIndex < candidatePaths.Count; pathIndex++)
        {
            string assetPath = candidatePaths[pathIndex];
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath) || !counted.Add(assetPath))
                continue;
            long fileBytes = FileLengthOf(assetPath);
            if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                report.sceneBytes += fileBytes;
                continue;
            }

            if (assetPath == tileDataAssetPath) report.dataBytes += fileBytes;
            else if (maskPaths.Contains(assetPath)) report.maskBytes += fileBytes;
            else if (signedDistanceFieldPaths.Contains(assetPath)) report.signedDistanceFieldBytes += fileBytes;
            else if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(Mesh)) report.meshBytes += fileBytes;
            else report.otherBytes += fileBytes;

            report.residentBytes += ResidentBytesOf(assetPath);
        }
        return report;
    }

    static long ResidentBytesOf(string assetPath)
    {
        long residentBytes = 0;
        UnityEngine.Object[] loadedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int assetIndex = 0; assetIndex < loadedAssets.Length; assetIndex++)
        {
            UnityEngine.Object loadedAsset = loadedAssets[assetIndex];
            if (loadedAsset is Texture || loadedAsset is Mesh || loadedAsset is TerrainTileData)
                residentBytes += Profiler.GetRuntimeMemorySizeLong(loadedAsset);
        }
        return residentBytes;
    }

    static void AddAssetPath(HashSet<string> paths, UnityEngine.Object asset)
    {
        if (asset == null)
            return;
        string assetPath = AssetDatabase.GetAssetPath(asset);
        if (!string.IsNullOrEmpty(assetPath))
            paths.Add(assetPath);
    }

    static long FileLengthOf(string assetPath)
    {
        FileInfo fileInfo = new FileInfo(Path.GetFullPath(assetPath));
        return fileInfo.Exists ? fileInfo.Length : 0;
    }

    static string FormatMegabytes(long bytes)
    {
        return (bytes / BytesPerMegabyte).ToString("F1");
    }

    static int CompareByCoordinate(TileReport left, TileReport right)
    {
        int xComparison = left.coordinate.x.CompareTo(right.coordinate.x);
        return xComparison != 0 ? xComparison : left.coordinate.y.CompareTo(right.coordinate.y);
    }

    static int CompareByDistanceToOrigin(TileReport left, TileReport right)
    {
        return left.coordinate.sqrMagnitude.CompareTo(right.coordinate.sqrMagnitude);
    }
}
