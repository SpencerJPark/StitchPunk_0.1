using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    // Reads and writes the file-queue gate protocol shared with the Python gate_client.
    // Directory and file names below are pinned to gate_client.py and must not drift.
    public sealed class GateRequestStore
    {
        private const string HeartbeatFileName = "broker-heartbeat.json";

        public string QueueDirectory { get; }
        public string ResultsDirectory { get; }
        public string HeartbeatFilePath { get; }

        public GateRequestStore(string stateDirectory)
        {
            this.QueueDirectory = Path.Combine(stateDirectory, "queue");
            this.ResultsDirectory = Path.Combine(stateDirectory, "results");
            this.HeartbeatFilePath = Path.Combine(stateDirectory, HeartbeatFileName);

            Directory.CreateDirectory(this.QueueDirectory);
            Directory.CreateDirectory(this.ResultsDirectory);
        }

        // Only queue/*.json directly in the queue directory — never the .jsonl side files
        // or a .tmp in flight from a concurrent writer. Unreadable files are skipped, not thrown.
        public List<GateRequestDto> ListQueuedRequests()
        {
            List<GateRequestDto> requests = new List<GateRequestDto>();
            string[] queueFilePaths = Directory.GetFiles(this.QueueDirectory, "*.json", SearchOption.TopDirectoryOnly);

            foreach (string queueFilePath in queueFilePaths)
            {
                try
                {
                    string requestJsonText = File.ReadAllText(queueFilePath);
                    GateRequestDto request = JsonUtility.FromJson<GateRequestDto>(requestJsonText);
                    if (request != null)
                    {
                        requests.Add(request);
                    }
                }
                catch (Exception)
                {
                    // A half-written or malformed queue file is skipped rather than failing the whole listing.
                }
            }

            requests.Sort((GateRequestDto firstRequest, GateRequestDto secondRequest) =>
                string.CompareOrdinal(firstRequest.createdUtc, secondRequest.createdUtc));
            return requests;
        }

        public void WriteRequest(GateRequestDto request)
        {
            string destinationPath = Path.Combine(this.QueueDirectory, request.requestId + ".json");
            WriteJsonAtomically(destinationPath, JsonUtility.ToJson(request, true));
        }

        public void WriteResult(GateResultDto result)
        {
            string destinationPath = Path.Combine(this.ResultsDirectory, result.requestId + ".json");
            WriteJsonAtomically(destinationPath, JsonUtility.ToJson(result, true));
        }

        public void DeleteRequest(string requestId)
        {
            string requestFilePath = Path.Combine(this.QueueDirectory, requestId + ".json");
            if (File.Exists(requestFilePath))
            {
                File.Delete(requestFilePath);
            }
        }

        // mtime on this file is what the Python client's broker_is_alive check reads,
        // so SetLastWriteTimeUtc must run even though the atomic write already touches it.
        public void WriteHeartbeat()
        {
            HeartbeatDto heartbeat = new HeartbeatDto
            {
                pid = Process.GetCurrentProcess().Id,
                utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            };

            WriteJsonAtomically(this.HeartbeatFilePath, JsonUtility.ToJson(heartbeat, true));
            File.SetLastWriteTimeUtc(this.HeartbeatFilePath, DateTime.UtcNow);
        }

        public string SideFilePath(string requestId, string suffix)
        {
            return Path.Combine(this.QueueDirectory, requestId + "." + suffix + ".jsonl");
        }

        // A crashed writer must never leave a half-written queue/result file for the other side to read.
        private static void WriteJsonAtomically(string destinationPath, string jsonText)
        {
            string temporaryPath = destinationPath + ".tmp";
            File.WriteAllText(temporaryPath, jsonText);

            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, null);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }

        [Serializable]
        private sealed class HeartbeatDto
        {
            public int pid;
            public string utc;
        }
    }
}
