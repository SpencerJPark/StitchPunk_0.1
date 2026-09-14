using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    public static class TestCapture
    {
        public static void RunEditModeFixtures(string[] fixtureNames, string resultSideFilePath)
        {
            if (File.Exists(resultSideFilePath))
            {
                File.Delete(resultSideFilePath);
            }

            TestRunnerApi testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            ResultWritingCallbacks resultWritingCallbacks = new ResultWritingCallbacks(testRunnerApi, resultSideFilePath);
            testRunnerApi.RegisterCallbacks(resultWritingCallbacks);

            Filter[] filters = new Filter[fixtureNames.Length];
            for (int fixtureIndex = 0; fixtureIndex < fixtureNames.Length; fixtureIndex++)
            {
                filters[fixtureIndex] = new Filter
                {
                    testMode = TestMode.EditMode,
                    groupNames = new[] { "^" + Regex.Escape(fixtureNames[fixtureIndex]) + "(\\.|$)" },
                };
            }

            ExecutionSettings executionSettings = new ExecutionSettings(filters);
            testRunnerApi.Execute(executionSettings);
        }

        public static bool TryReadSummary(string resultSideFilePath, out TestSummaryDto summary)
        {
            if (!File.Exists(resultSideFilePath))
            {
                summary = null;
                return false;
            }

            string jsonText = File.ReadAllText(resultSideFilePath);
            summary = JsonUtility.FromJson<TestSummaryDto>(jsonText);
            return summary != null;
        }

        // Owns the side-file path for one run; unregisters itself once RunFinished has written the summary.
        private sealed class ResultWritingCallbacks : ICallbacks
        {
            private readonly TestRunnerApi testRunnerApi;
            private readonly string resultSideFilePath;

            public ResultWritingCallbacks(TestRunnerApi testRunnerApi, string resultSideFilePath)
            {
                this.testRunnerApi = testRunnerApi;
                this.resultSideFilePath = resultSideFilePath;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                int passedCount = 0;
                int failedCount = 0;
                List<TestFailureDto> failures = new List<TestFailureDto>();

                CountLeafResults(result, ref passedCount, ref failedCount, failures);

                TestSummaryDto summary = new TestSummaryDto
                {
                    passed = passedCount,
                    failed = failedCount,
                    failures = failures.ToArray(),
                };

                WriteSummaryAtomically(this.resultSideFilePath, JsonUtility.ToJson(summary, true));

                this.testRunnerApi.UnregisterCallbacks(this);
            }

            // Only leaves (no children) are real test cases; suite/fixture nodes would double-count.
            private static void CountLeafResults(ITestResultAdaptor resultNode, ref int passedCount, ref int failedCount, List<TestFailureDto> failures)
            {
                if (resultNode.HasChildren)
                {
                    foreach (ITestResultAdaptor childResult in resultNode.Children)
                    {
                        CountLeafResults(childResult, ref passedCount, ref failedCount, failures);
                    }

                    return;
                }

                if (resultNode.TestStatus == TestStatus.Passed)
                {
                    passedCount++;
                }
                else if (resultNode.TestStatus == TestStatus.Failed)
                {
                    failedCount++;
                    failures.Add(new TestFailureDto
                    {
                        name = resultNode.FullName,
                        message = resultNode.Message,
                    });
                }
            }

            // A crashed writer must never leave a half-written result file for the broker to read.
            private static void WriteSummaryAtomically(string destinationPath, string jsonText)
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
        }
    }
}
