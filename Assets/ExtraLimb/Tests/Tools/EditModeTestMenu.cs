using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ExtraLimb.Tests.Tools
{
    /// <summary>
    /// メニュー「ExtraLimb/EditModeテストを実行」から ExtraLimb のEditModeテストを走らせ、
    /// 結果を Logs/ExtraLimbTestResults.txt に書き、コンソールに要約を出す。
    /// Claude が Unity MCP 経由でテストを実行して結果を読むための道具。
    /// Window → General → Test Runner から実行しても、同じテストが走る。
    /// </summary>
    internal static class EditModeTestMenu
    {
        public const string ResultPath = "Logs/ExtraLimbTestResults.txt";
        const string TestAssemblyName = "ExtraLimb.Tests.EditMode";

        [MenuItem("ExtraLimb/EditModeテストを実行")]
        static void Run()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, "status: Running\n");

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(api));
            var filter = new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { TestAssemblyName },
            };
            api.Execute(new ExecutionSettings(filter));
        }

        class Callbacks : ICallbacks
        {
            readonly TestRunnerApi api;

            public Callbacks(TestRunnerApi api) => this.api = api;

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
                var text = new StringBuilder();
                text.AppendLine($"status: {result.TestStatus}");
                text.AppendLine($"passed: {result.PassCount}, failed: {result.FailCount}, skipped: {result.SkipCount}, inconclusive: {result.InconclusiveCount}");
                text.AppendLine($"duration: {result.Duration:F2}s");
                AppendLeaves(result, text);
                File.WriteAllText(ResultPath, text.ToString());

                var summary = $"[ExtraLimb] EditModeテスト：合格 {result.PassCount}、失敗 {result.FailCount}、スキップ {result.SkipCount}";
                if (result.FailCount > 0) Debug.LogError($"{summary}（詳細は {ResultPath}）");
                else Debug.Log(summary);

                api.UnregisterCallbacks(this);
                Object.DestroyImmediate(api);
            }

            static void AppendLeaves(ITestResultAdaptor result, StringBuilder text)
            {
                if (result.HasChildren)
                {
                    foreach (var child in result.Children) AppendLeaves(child, text);
                    return;
                }

                text.AppendLine($"{result.TestStatus}: {result.Test.FullName}");
                if (result.TestStatus == TestStatus.Failed)
                {
                    text.AppendLine($"  {result.Message}");
                    if (!string.IsNullOrEmpty(result.StackTrace))
                        text.AppendLine("  " + result.StackTrace.Replace("\n", "\n  "));
                }
            }
        }
    }
}
