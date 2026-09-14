using System;

namespace WorktreeToolkit.Editor
{
    // Field names must match the Python gate_client.py request/result dicts byte-for-byte —
    // JsonUtility serializes public fields by name, so any mismatch silently drops data.
    [Serializable]
    public sealed class GateRequestDto
    {
        public int protocol;
        public string requestId;
        public string worktreeId;
        public string commitSha;
        public string[] editModeFixtures;
        public string[] playModeFixtures;
        public float timeoutSeconds;
        public string createdUtc;
        public string phase;
    }

    [Serializable]
    public sealed class CompilerErrorDto
    {
        public string file;
        public int line;
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class TestFailureDto
    {
        public string name;
        public string message;
    }

    [Serializable]
    public sealed class TestSummaryDto
    {
        public int passed;
        public int failed;
        public TestFailureDto[] failures;
    }

    [Serializable]
    public sealed class GateResultDto
    {
        public string requestId;
        public string verdict;
        public CompilerErrorDto[] compilerErrors;
        public string[] burstErrors;
        public TestSummaryDto tests;
        public string refusedReason;
    }

    public static class GatePhases
    {
        public const string Queued = "queued";
        public const string Staging = "staging";
        public const string Compiling = "compiling";
        public const string Testing = "testing";
        public const string Returning = "returning";
        public const string Done = "done";
    }

    public static class GateVerdicts
    {
        public const string Pass = "pass";
        public const string CompileErrors = "compile-errors";
        public const string BurstErrors = "burst-errors";
        public const string TestFailures = "test-failures";
        public const string Refused = "refused";
        public const string Timeout = "timeout";
    }

    public static class GateProtocol
    {
        public const int GateProtocolVersion = 1;
    }
}
