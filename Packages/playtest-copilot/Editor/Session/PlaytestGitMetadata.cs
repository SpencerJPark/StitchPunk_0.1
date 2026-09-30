using System;
using System.IO;

namespace PlaytestCopilot.Editor
{
    /// Reads the current commit SHA by hand instead of launching `git` — this runs when play mode
    /// stops, and spawning a process there stalls the Editor.
    public static class PlaytestGitMetadata
    {
        private const string GitEntryName = ".git";
        private const string GitDirPrefix = "gitdir:";
        private const string HeadFileName = "HEAD";
        private const string CommonDirFileName = "commondir";
        private const string PackedRefsFileName = "packed-refs";
        private const string RefPrefix = "ref:";

        public static string TryReadHeadCommit(string repositoryRoot)
        {
            try
            {
                string gitDirectory = ResolveGitDirectory(repositoryRoot);
                if (string.IsNullOrEmpty(gitDirectory) || !Directory.Exists(gitDirectory))
                {
                    return string.Empty;
                }

                string headFilePath = Path.Combine(gitDirectory, HeadFileName);
                if (!File.Exists(headFilePath))
                {
                    return string.Empty;
                }

                string headContent = File.ReadAllText(headFilePath).Trim();
                if (headContent.StartsWith(RefPrefix, StringComparison.Ordinal))
                {
                    string referenceName = headContent.Substring(RefPrefix.Length).Trim();
                    return ResolveReference(gitDirectory, referenceName);
                }

                return IsCommitSha(headContent) ? headContent : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // ".git" is a directory in a normal checkout, or a file containing "gitdir: <path>" in a
        // git worktree — that file is the case this package actually runs under.
        private static string ResolveGitDirectory(string repositoryRoot)
        {
            string gitEntryPath = Path.Combine(repositoryRoot, GitEntryName);
            if (Directory.Exists(gitEntryPath))
            {
                return gitEntryPath;
            }

            if (!File.Exists(gitEntryPath))
            {
                return string.Empty;
            }

            string gitFileContent = File.ReadAllText(gitEntryPath).Trim();
            if (!gitFileContent.StartsWith(GitDirPrefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            string gitDirectoryPath = gitFileContent.Substring(GitDirPrefix.Length).Trim();
            return Path.IsPathRooted(gitDirectoryPath)
                ? gitDirectoryPath
                : Path.GetFullPath(Path.Combine(repositoryRoot, gitDirectoryPath));
        }

        // A worktree's private git directory holds no refs of its own — branch refs live in the
        // shared "common" directory named by "commondir". A non-worktree checkout has no
        // commondir file at all, so its common directory is simply itself.
        private static string ResolveCommonDirectory(string gitDirectory)
        {
            string commonDirFilePath = Path.Combine(gitDirectory, CommonDirFileName);
            if (!File.Exists(commonDirFilePath))
            {
                return gitDirectory;
            }

            string commonDirContent = File.ReadAllText(commonDirFilePath).Trim();
            return Path.IsPathRooted(commonDirContent)
                ? commonDirContent
                : Path.GetFullPath(Path.Combine(gitDirectory, commonDirContent));
        }

        private static string ResolveReference(string gitDirectory, string referenceName)
        {
            string commonDirectory = ResolveCommonDirectory(gitDirectory);

            string loosePathInGitDirectory = Path.Combine(gitDirectory, referenceName);
            if (File.Exists(loosePathInGitDirectory))
            {
                return File.ReadAllText(loosePathInGitDirectory).Trim();
            }

            string loosePathInCommonDirectory = Path.Combine(commonDirectory, referenceName);
            if (File.Exists(loosePathInCommonDirectory))
            {
                return File.ReadAllText(loosePathInCommonDirectory).Trim();
            }

            string shaFromPackedRefs = TryFindInPackedRefs(Path.Combine(gitDirectory, PackedRefsFileName), referenceName);
            if (!string.IsNullOrEmpty(shaFromPackedRefs))
            {
                return shaFromPackedRefs;
            }

            return TryFindInPackedRefs(Path.Combine(commonDirectory, PackedRefsFileName), referenceName);
        }

        // packed-refs lines look like "<sha> refs/heads/main"; a trailing "^<sha>" peel line for
        // an annotated tag never matches a branch reference name so it is safely ignored here.
        private static string TryFindInPackedRefs(string packedRefsPath, string referenceName)
        {
            if (!File.Exists(packedRefsPath))
            {
                return string.Empty;
            }

            foreach (string line in File.ReadAllLines(packedRefsPath))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == '^')
                {
                    continue;
                }

                if (!line.EndsWith(referenceName, StringComparison.Ordinal))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf(' ');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string candidateSha = line.Substring(0, separatorIndex).Trim();
                if (IsCommitSha(candidateSha))
                {
                    return candidateSha;
                }
            }

            return string.Empty;
        }

        private static bool IsCommitSha(string candidate)
        {
            if (candidate.Length != 40)
            {
                return false;
            }

            foreach (char character in candidate)
            {
                bool isHexDigit = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!isHexDigit)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
