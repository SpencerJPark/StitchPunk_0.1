using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;

namespace WorktreeToolkit.Editor
{
    public readonly struct PythonCommand
    {
        public readonly string executablePath;
        public readonly string[] prefixArguments;

        public PythonCommand(string executablePath, string[] prefixArguments)
        {
            this.executablePath = executablePath;
            this.prefixArguments = prefixArguments;
        }
    }

    public static class PythonLocator
    {
        public const string PythonPathEditorPrefsKey = "WorktreeToolkit.PythonPath";

        private const int ProbeTimeoutMilliseconds = 3000;

        // Cached once a probe succeeds so repeated calls (e.g. every CLI invocation) skip re-probing.
        private static bool cachedCommandIsValid;
        private static PythonCommand cachedCommand;

        public static bool TryResolvePythonCommand(out PythonCommand pythonCommand, out string failureReason)
        {
            if (cachedCommandIsValid)
            {
                pythonCommand = cachedCommand;
                failureReason = null;
                return true;
            }

            List<PythonCommand> candidateCommands = new List<PythonCommand>();

            string editorPrefsOverridePath = EditorPrefs.GetString(PythonPathEditorPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(editorPrefsOverridePath))
            {
                candidateCommands.Add(new PythonCommand(editorPrefsOverridePath, Array.Empty<string>()));
            }

            candidateCommands.Add(new PythonCommand("python3", Array.Empty<string>()));
            candidateCommands.Add(new PythonCommand("python", Array.Empty<string>()));
            candidateCommands.Add(new PythonCommand("py", new string[] { "-3" }));

            for (int candidateIndex = 0; candidateIndex < candidateCommands.Count; candidateIndex++)
            {
                PythonCommand candidateCommand = candidateCommands[candidateIndex];
                if (ProbeCommand(candidateCommand))
                {
                    cachedCommand = candidateCommand;
                    cachedCommandIsValid = true;
                    pythonCommand = candidateCommand;
                    failureReason = null;
                    return true;
                }
            }

            pythonCommand = default;
            failureReason = "No Python interpreter found. Tried EditorPrefs override ('" + PythonPathEditorPrefsKey +
                "'), python3, python, and py -3.";
            return false;
        }

        private static bool ProbeCommand(PythonCommand candidateCommand)
        {
            try
            {
                using (Process probeProcess = new Process())
                {
                    probeProcess.StartInfo.FileName = candidateCommand.executablePath;
                    probeProcess.StartInfo.Arguments = BuildProbeArguments(candidateCommand.prefixArguments);
                    probeProcess.StartInfo.UseShellExecute = false;
                    probeProcess.StartInfo.RedirectStandardOutput = true;
                    probeProcess.StartInfo.RedirectStandardError = true;
                    probeProcess.StartInfo.CreateNoWindow = true;

                    if (!probeProcess.Start())
                    {
                        return false;
                    }

                    // Drain both pipes concurrently so a chatty --version output cannot fill a pipe buffer and stall.
                    probeProcess.StandardOutput.ReadToEndAsync();
                    probeProcess.StandardError.ReadToEndAsync();

                    bool exitedInTime = probeProcess.WaitForExit(ProbeTimeoutMilliseconds);
                    if (!exitedInTime)
                    {
                        try
                        {
                            probeProcess.Kill();
                        }
                        catch (Exception)
                        {
                            // process may have exited between the timeout check and the kill call
                        }

                        return false;
                    }

                    return probeProcess.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string BuildProbeArguments(string[] prefixArguments)
        {
            if (prefixArguments == null || prefixArguments.Length == 0)
            {
                return "--version";
            }

            return string.Join(" ", prefixArguments) + " --version";
        }
    }
}
