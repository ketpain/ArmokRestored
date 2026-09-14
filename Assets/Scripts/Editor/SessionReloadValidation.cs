using System;
using System.IO;
using Armok.Lifecycle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Armok.Editor
{
    // A batch-only live check whose control state survives the domain reload being tested.
    public static class SessionReloadValidation
    {
        private const string Prefix = "Armok.FullReloadValidation.";
        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string failure;
            public int registeredWorkers;
            public int contentCallbacks;
            public bool allWorkersStopped;
            public bool domainReloadEnabled;
            public bool sceneReloadEnabled;
            public string editorDiagnostic;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this validation in a closed batch checkout.");
            SessionState.SetBool(Prefix + "reloadOptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Prefix + "reloadOptions", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(Prefix + "background", Application.runInBackground);
            SessionState.SetString(Prefix + "start", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(Prefix + "failure", "");
            SessionState.SetString(Prefix + "editorDiagnostic", "");
            SessionState.SetBool(Prefix + "stopped", false);
            SessionState.SetInt(Prefix + "workers", 0);
            SessionState.SetInt(Prefix + "callbacks", 0);
            SessionState.SetInt(Prefix + "phase", 1);
            EditorSettings.enterPlayModeOptionsEnabled = false;
            Application.runInBackground = true;
            Resume();
            EditorSceneManager.OpenScene("Assets/Scenes/Map Mode.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || SessionState.GetInt(Prefix + "phase", 0) == 0) return;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Capture; Application.logMessageReceived += Capture;
        }

        private static void Capture(string message, string trace, LogType type)
        {
            // Unity 6000.6 batch startup can throw here while its search index is empty.
            // Keep the exact editor diagnostic in the report; never filter application errors.
            if (type == LogType.Exception && message.StartsWith("ArgumentOutOfRangeException:") &&
                trace.Contains("UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase") && !trace.Contains("Assets/"))
            {
                SessionState.SetString(Prefix + "editorDiagnostic", message + "\n" + trace);
                return;
            }
            if (type == LogType.Error || type == LogType.Exception)
                SessionState.SetString(Prefix + "failure", message);
        }

        private static void Tick()
        {
            int phase = SessionState.GetInt(Prefix + "phase", 0);
            string failure = SessionState.GetString(Prefix + "failure", "");
            if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Prefix + "start", "0")) > TimeSpan.FromMinutes(3).Ticks)
                failure = "Full reload validation timed out.";
            if (phase == 1 && EditorApplication.isPlaying)
            {
                var session = SessionRuntime.Current;
                if (session.Stage == SessionStage.Failed) failure = session.Failure.ToString();
                if (session.IsReady)
                {
                    // Wait for DFConnection.Update to start the configured polling worker.
                    var workers = SessionRuntime.Workers;
                    if (workers.Length < (DFConnection.Instance.RunOnAlternateThread ? 2 : 1)) return;
                    SessionState.SetInt(Prefix + "workers", workers.Length);
                    SessionState.SetInt(Prefix + "callbacks", session.ContentCallbackCount);
                    SessionRuntime.Stop(session);
                    if (!Array.TrueForAll(workers, worker => worker.IsStopped)) failure = "A worker survived shutdown.";
                    SessionState.SetBool(Prefix + "stopped", Array.TrueForAll(workers, worker => worker.IsStopped));
                    SessionState.SetInt(Prefix + "phase", 2);
                    EditorApplication.isPlaying = false;
                }
            }
            if (!string.IsNullOrEmpty(failure))
            {
                SessionState.SetString(Prefix + "failure", failure);
                if (EditorApplication.isPlaying)
                {
                    SessionRuntime.Stop(SessionRuntime.Current);
                    SessionState.SetInt(Prefix + "phase", 2);
                    EditorApplication.isPlaying = false;
                    return;
                }
                Finish();
            }
            else if (phase == 2 && !EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }

        private static void Finish()
        {
            var report = new Report {
                failure = SessionState.GetString(Prefix + "failure", ""),
                registeredWorkers = SessionState.GetInt(Prefix + "workers", 0),
                contentCallbacks = SessionState.GetInt(Prefix + "callbacks", 0),
                allWorkersStopped = SessionState.GetBool(Prefix + "stopped", false),
                domainReloadEnabled = !EditorSettings.enterPlayModeOptionsEnabled,
                sceneReloadEnabled = !EditorSettings.enterPlayModeOptionsEnabled,
                editorDiagnostic = SessionState.GetString(Prefix + "editorDiagnostic", "")
            };
            report.passed = string.IsNullOrEmpty(report.failure) && report.registeredWorkers > 0 && report.allWorkersStopped;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/full-reload-validation.json", JsonUtility.ToJson(report, true));
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Prefix + "reloadOptionsEnabled", true);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Prefix + "reloadOptions", 3);
            Application.runInBackground = SessionState.GetBool(Prefix + "background", false);
            SessionState.SetInt(Prefix + "phase", 0);
            EditorApplication.update -= Tick; Application.logMessageReceived -= Capture;
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
