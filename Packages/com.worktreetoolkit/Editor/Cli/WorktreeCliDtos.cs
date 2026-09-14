using System;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    [Serializable]
    public sealed class WorktreeGraphDto
    {
        public int protocol;
        public string trunk;
        public StageDto stage;
        public WorktreeNodeDto[] worktrees;
    }

    [Serializable]
    public sealed class StageDto
    {
        public string path;
        public string branch;
        public string detachedSha;
        public int dirtyTracked;
        public StageLockDto busyWith;
        public string reviewWorktreeId;
        public string[] stashes;
    }

    [Serializable]
    public sealed class StageLockDto
    {
        public string holder;
        public string commitSha;
        public string since;

        // JsonUtility turns a null "busyWith" into an empty instance rather than null, so
        // presence must be inferred from the holder field instead of a null check.
        public bool IsHeld => !string.IsNullOrEmpty(holder);
    }

    [Serializable]
    public sealed class WorktreeNodeDto
    {
        public string id;
        public bool registered;
        public string branch;
        public string path;
        public string headSha;
        public string parentBranch;
        public string forkPointSha;
        public int ahead;
        public int behind;
        public int dirty;
        public bool parked;
        public string mode;
        public int sizeMegabytes;
        public bool stale;
        public bool locked;
        public bool missing;
        public LeadDto lead;
    }

    [Serializable]
    public sealed class LeadDto
    {
        public string spec;
        public string leadModel;
        public string workerModel;
        public string status;

        public bool IsBound => !string.IsNullOrEmpty(status);
    }

    [Serializable]
    public sealed class CliErrorDto
    {
        public string error;
        public int exitCode;
    }

    [Serializable]
    public sealed class WherePathsDto
    {
        public string stagePath;
        public string commonGitDirectory;
        public string stateDirectory;
        public int protocol;
    }

    [Serializable]
    public sealed class ChangedPathsDto
    {
        public string[] paths;
    }

    public static class WorktreeCliJson
    {
        public static bool TryParse<T>(WorktreeCliResult result, out T value, out string errorMessage)
        {
            if (result.Succeeded)
            {
                try
                {
                    value = JsonUtility.FromJson<T>(result.standardOutput);
                    errorMessage = null;
                    return true;
                }
                catch (ArgumentException argumentException)
                {
                    value = default;
                    errorMessage = argumentException.Message;
                    return false;
                }
            }

            value = default;
            errorMessage = ResolveFailureMessage(result);
            return false;
        }

        private static string ResolveFailureMessage(WorktreeCliResult result)
        {
            try
            {
                CliErrorDto cliErrorDto = JsonUtility.FromJson<CliErrorDto>(result.standardOutput);
                if (cliErrorDto != null && !string.IsNullOrEmpty(cliErrorDto.error))
                {
                    return cliErrorDto.error;
                }
            }
            catch (ArgumentException)
            {
                // standardOutput was not a CliErrorDto payload; fall through to stderr/exit code.
            }

            if (!string.IsNullOrEmpty(result.standardError))
            {
                return result.standardError.Trim();
            }

            return $"exit code {result.exitCode}";
        }
    }
}
