using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Armok.Tests.Editor
{
    public static class ValidationRunner
    {
        private const string PendingReport = "Armok.Tests.PendingReport";
        private const string RunActive = "Armok.Tests.RunActive";
        private static readonly Results callbacks = new Results();

        [InitializeOnLoadMethod]
        private static void Register()
        {
            TestRunnerApi.UnregisterTestCallback(callbacks);
            TestRunnerApi.RegisterTestCallback(callbacks);
        }

        [MenuItem("Mytools/Tests/Run Content Tests")]
        public static void RunContent() => Run(TestMode.EditMode, "Armok.Content.Tests", "content");

        [MenuItem("Mytools/Tests/Run Input Tests")]
        public static void RunInput() => Run(TestMode.PlayMode, "Armok.Input.Tests", "input");

        [MenuItem("Mytools/Tests/Run Live DF Smoke Test")]
        public static void RunLive() => Run(TestMode.EditMode, "Armok.Tests.Editor", "live");

        private static void Run(TestMode mode, string assembly, string label)
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Fix script compilation errors before starting a validation run.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(RunActive, false) ||
                !string.IsNullOrEmpty(SessionState.GetString(PendingReport, "")))
                throw new InvalidOperationException("Start a validation run outside Play Mode with no other test run active.");
            Directory.CreateDirectory("TestResults");
            SessionState.SetString(PendingReport, "TestResults/" + label);
            Register();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try { api.Execute(new ExecutionSettings(new Filter { testMode = mode, assemblyNames = new[] { assembly } })); }
            catch { SessionState.EraseString(PendingReport); throw; }
            finally { UnityEngine.Object.DestroyImmediate(api); }
        }

        private class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) => SessionState.SetBool(RunActive, true);
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetBool(RunActive, false);
                string path = SessionState.GetString(PendingReport, "");
                if (string.IsNullOrEmpty(path)) return;
                SessionState.EraseString(PendingReport);
                TestRunnerApi.SaveResultToFile(result, path + ".xml");
                File.WriteAllText(path + ".txt", $"{result.ResultState}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped, {result.InconclusiveCount} inconclusive.\n");
                Debug.Log("Armok tests finished: " + path + ".xml");
            }
        }
    }
}
