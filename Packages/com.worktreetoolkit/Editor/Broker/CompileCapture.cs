using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    public static class CompileCapture
    {
        [Serializable]
        private sealed class BurstErrorLineDto
        {
            public string message;
        }

        [Serializable]
        private sealed class CompilationFinishedMarkerDto
        {
            public string marker;
        }

        private static readonly object WriteLock = new object();
        private static readonly Regex CompilerCodePattern = new Regex(@"\b(CS|BC)\d{4}\b", RegexOptions.Compiled);

        private static string compileSideFilePath = string.Empty;
        private static string burstSideFilePath = string.Empty;

        public static void Begin(string compileSideFilePath, string burstSideFilePath)
        {
            // Never double-subscribe: an earlier Begin without a matching End would duplicate every callback.
            End();

            CompileCapture.compileSideFilePath = compileSideFilePath;
            CompileCapture.burstSideFilePath = burstSideFilePath;

            File.WriteAllText(compileSideFilePath, string.Empty);
            File.WriteAllText(burstSideFilePath, string.Empty);

            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;
        }

        public static void End()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;
        }

        public static bool HasCompilationFinishedMarker(string compileSideFilePath)
        {
            if (!File.Exists(compileSideFilePath))
            {
                return false;
            }

            string[] lines = File.ReadAllLines(compileSideFilePath);
            for (int lineIndex = lines.Length - 1; lineIndex >= 0; lineIndex--)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                CompilationFinishedMarkerDto markerDto = TryFromJson<CompilationFinishedMarkerDto>(line);
                if (markerDto != null && markerDto.marker == "compilationFinished")
                {
                    return true;
                }
            }

            return false;
        }

        public static List<CompilerErrorDto> ReadCompilerErrors(string compileSideFilePath)
        {
            List<CompilerErrorDto> compilerErrors = new List<CompilerErrorDto>();

            if (!File.Exists(compileSideFilePath))
            {
                return compilerErrors;
            }

            string[] lines = File.ReadAllLines(compileSideFilePath);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Contains("\"marker\""))
                {
                    continue;
                }

                CompilerErrorDto compilerErrorDto = TryFromJson<CompilerErrorDto>(line);
                if (compilerErrorDto != null)
                {
                    compilerErrors.Add(compilerErrorDto);
                }
            }

            return compilerErrors;
        }

        public static List<string> ReadBurstErrors(string burstSideFilePath)
        {
            List<string> burstErrors = new List<string>();

            if (!File.Exists(burstSideFilePath))
            {
                return burstErrors;
            }

            string[] lines = File.ReadAllLines(burstSideFilePath);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                BurstErrorLineDto burstErrorLineDto = TryFromJson<BurstErrorLineDto>(line);
                if (burstErrorLineDto != null && burstErrorLineDto.message != null)
                {
                    burstErrors.Add(burstErrorLineDto.message);
                }
            }

            return burstErrors;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] compilerMessages)
        {
            // This callback runs in the domain that is about to unload, so the line must be appended
            // immediately rather than buffered for a later flush that will never happen.
            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type != CompilerMessageType.Error)
                {
                    continue;
                }

                CompilerErrorDto compilerErrorDto = new CompilerErrorDto
                {
                    file = compilerMessage.file,
                    line = compilerMessage.line,
                    code = ExtractCompilerCode(compilerMessage.message),
                    message = compilerMessage.message
                };

                AppendLine(compileSideFilePath, JsonUtility.ToJson(compilerErrorDto));
            }
        }

        private static void OnCompilationFinished(object context)
        {
            CompilationFinishedMarkerDto markerDto = new CompilationFinishedMarkerDto { marker = "compilationFinished" };
            AppendLine(compileSideFilePath, JsonUtility.ToJson(markerDto));
        }

        private static void OnLogMessageReceivedThreaded(string condition, string stackTrace, LogType logType)
        {
            if (logType != LogType.Error && logType != LogType.Exception)
            {
                return;
            }

            if (!condition.Contains("error BC") && !condition.StartsWith("Burst error", StringComparison.Ordinal))
            {
                return;
            }

            BurstErrorLineDto burstErrorLineDto = new BurstErrorLineDto { message = condition };
            AppendLine(burstSideFilePath, JsonUtility.ToJson(burstErrorLineDto));
        }

        private static string ExtractCompilerCode(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            Match match = CompilerCodePattern.Match(message);
            return match.Success ? match.Value : string.Empty;
        }

        private static void AppendLine(string filePath, string jsonLine)
        {
            lock (WriteLock)
            {
                File.AppendAllText(filePath, jsonLine + "\n");
            }
        }

        private static T TryFromJson<T>(string jsonLine) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(jsonLine);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
