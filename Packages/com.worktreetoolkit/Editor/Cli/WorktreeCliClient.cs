using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    public sealed class WorktreeCliResult
    {
        public int exitCode;
        public string standardOutput;
        public string standardError;

        public bool Succeeded => exitCode == 0;
    }

    public static class WorktreeCliClient
    {
        private const int TimeoutMilliseconds = 120000;

        public static string ProjectRootPath { get; } = Directory.GetParent(Application.dataPath).FullName;

        public static string CliScriptPath { get; } =
            Path.GetFullPath("Packages/com.worktreetoolkit/Tools~/worktree.py");

        public static WorktreeCliResult Run(params string[] arguments)
        {
            if (!PythonLocator.TryResolvePythonCommand(out PythonCommand pythonCommand, out string failureReason))
            {
                return new WorktreeCliResult
                {
                    exitCode = -1,
                    standardOutput = string.Empty,
                    standardError = failureReason
                };
            }

            return RunResolved(arguments, pythonCommand, ProjectRootPath, CliScriptPath);
        }

        public static Task<WorktreeCliResult> RunAsync(params string[] arguments)
        {
            // EditorPrefs and Application.dataPath throw off the main thread, so resolve everything here first.
            if (!PythonLocator.TryResolvePythonCommand(out PythonCommand pythonCommand, out string failureReason))
            {
                return Task.FromResult(new WorktreeCliResult
                {
                    exitCode = -1,
                    standardOutput = string.Empty,
                    standardError = failureReason
                });
            }

            string projectRootPath = ProjectRootPath;
            string cliScriptPath = CliScriptPath;
            return Task.Run(() => RunResolved(arguments, pythonCommand, projectRootPath, cliScriptPath));
        }

        private static WorktreeCliResult RunResolved(string[] arguments, PythonCommand pythonCommand, string projectRootPath, string cliScriptPath)
        {
            List<string> fullArgumentList = new List<string>();
            for (int prefixIndex = 0; prefixIndex < pythonCommand.prefixArguments.Length; prefixIndex++)
            {
                fullArgumentList.Add(pythonCommand.prefixArguments[prefixIndex]);
            }

            fullArgumentList.Add(cliScriptPath);
            for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
            {
                fullArgumentList.Add(arguments[argumentIndex]);
            }

            fullArgumentList.Add("--cwd");
            fullArgumentList.Add(projectRootPath);
            fullArgumentList.Add("--json");

            using (Process cliProcess = new Process())
            {
                cliProcess.StartInfo.FileName = pythonCommand.executablePath;
                cliProcess.StartInfo.Arguments = BuildQuotedArgumentString(fullArgumentList);
                cliProcess.StartInfo.UseShellExecute = false;
                cliProcess.StartInfo.CreateNoWindow = true;
                cliProcess.StartInfo.RedirectStandardOutput = true;
                cliProcess.StartInfo.RedirectStandardError = true;
                cliProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                cliProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                cliProcess.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                try
                {
                    cliProcess.Start();
                }
                catch (Exception startException)
                {
                    return new WorktreeCliResult
                    {
                        exitCode = -1,
                        standardOutput = string.Empty,
                        standardError = "Failed to start Python process: " + startException.Message
                    };
                }

                // Both pipes must be drained concurrently with WaitForExit — reading only after exit
                // risks a deadlock if the CLI writes more than the OS pipe buffer before exiting.
                Task<string> standardOutputReadTask = cliProcess.StandardOutput.ReadToEndAsync();
                Task<string> standardErrorReadTask = cliProcess.StandardError.ReadToEndAsync();

                bool processExitedInTime = cliProcess.WaitForExit(TimeoutMilliseconds);
                if (!processExitedInTime)
                {
                    try
                    {
                        cliProcess.Kill();
                    }
                    catch (Exception)
                    {
                        // process may have exited between the timeout check and the kill call
                    }

                    return new WorktreeCliResult
                    {
                        exitCode = -2,
                        standardOutput = string.Empty,
                        standardError = "Worktree CLI timed out after " + TimeoutMilliseconds + " ms."
                    };
                }

                standardOutputReadTask.Wait();
                standardErrorReadTask.Wait();

                return new WorktreeCliResult
                {
                    exitCode = cliProcess.ExitCode,
                    standardOutput = standardOutputReadTask.Result,
                    standardError = standardErrorReadTask.Result
                };
            }
        }

        private static string BuildQuotedArgumentString(List<string> argumentValues)
        {
            StringBuilder argumentStringBuilder = new StringBuilder();
            for (int argumentIndex = 0; argumentIndex < argumentValues.Count; argumentIndex++)
            {
                if (argumentIndex > 0)
                {
                    argumentStringBuilder.Append(' ');
                }

                argumentStringBuilder.Append(QuoteArgument(argumentValues[argumentIndex]));
            }

            return argumentStringBuilder.ToString();
        }

        // Windows CommandLineToArgvW quoting: wrap in quotes when the argument has spaces/quotes/tabs,
        // double any backslash run that precedes a literal quote or the closing quote.
        private static string QuoteArgument(string argumentValue)
        {
            if (string.IsNullOrEmpty(argumentValue))
            {
                return "\"\"";
            }

            bool needsQuoting = argumentValue.IndexOfAny(new char[] { ' ', '"', '\t' }) >= 0;
            if (!needsQuoting)
            {
                return argumentValue;
            }

            StringBuilder quotedArgumentBuilder = new StringBuilder();
            quotedArgumentBuilder.Append('"');

            int backslashRunLength = 0;
            for (int characterIndex = 0; characterIndex < argumentValue.Length; characterIndex++)
            {
                char currentCharacter = argumentValue[characterIndex];
                if (currentCharacter == '\\')
                {
                    backslashRunLength++;
                    continue;
                }

                if (currentCharacter == '"')
                {
                    quotedArgumentBuilder.Append('\\', backslashRunLength * 2 + 1);
                    quotedArgumentBuilder.Append('"');
                    backslashRunLength = 0;
                    continue;
                }

                if (backslashRunLength > 0)
                {
                    quotedArgumentBuilder.Append('\\', backslashRunLength);
                    backslashRunLength = 0;
                }

                quotedArgumentBuilder.Append(currentCharacter);
            }

            if (backslashRunLength > 0)
            {
                quotedArgumentBuilder.Append('\\', backslashRunLength * 2);
            }

            quotedArgumentBuilder.Append('"');
            return quotedArgumentBuilder.ToString();
        }
    }
}
