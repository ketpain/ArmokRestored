using System;
using System.Collections.Generic;
using Armok.Lifecycle;
using UnityEngine;
using UnityEngine.SceneManagement;

public interface ISessionContentParticipant
{
    void RegisterSessionContent();
}

// Scene composition stays in Assembly-CSharp while the lifecycle is independently testable.
public sealed class SessionRuntime : MonoBehaviour
{
    public static SessionCoordinator Current { get; private set; } = new SessionCoordinator();
    private static SessionRuntime host;
    private static readonly List<ISessionWorker> workers = new List<ISessionWorker>();
    private static readonly List<Action> release = new List<Action>();
    public static event Action Stopping;
    private static bool failureShown;
    public static bool Ready => Current.IsReady;
    public static ISessionWorker[] Workers => workers.ToArray();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        // A new session must never overlap workers retained from a previous one.
        if (!WorkerShutdown.Stop(workers.ToArray(), 500))
            throw new InvalidOperationException("Previous Armok workers are still stopping. Retry Play when they finish.");
        workers.Clear(); release.Clear(); Stopping = null;
        Current = new SessionCoordinator(); host = null; failureShown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        host = new GameObject("Armok Session").AddComponent<SessionRuntime>();
        DontDestroyOnLoad(host.gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RegisterSceneContent();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterSceneContent()
    {
        // ExecuteInEditMode components may not receive Awake again with reload disabled.
        // Registration is idempotent per coordinator and also runs after an explicit retry.
        foreach (var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (component is ISessionContentParticipant participant) participant.RegisterSessionContent();
    }

    public static void Own(ISessionWorker worker, Action afterStop = null)
    {
        if (Current.IsStopping) throw new InvalidOperationException("Cannot start a worker in a stopped session.");
        workers.Add(worker);
        if (afterStop != null) release.Add(afterStop);
    }

    public static void AfterStop(Action cleanup) => release.Add(cleanup);

    public static void Stop(SessionCoordinator owner, Exception failure = null)
    {
        if (!ReferenceEquals(owner, Current)) return;
        if (Current.Stage == SessionStage.Stopping || Current.Stage == SessionStage.Failed ||
            (Current.Stage == SessionStage.Disconnected && release.Count == 0 && workers.Count == 0)) return;
        if (failure != null) Current.Fail(failure); else Current.BeginStop();
        if (Stopping != null)
            foreach (Action stop in Stopping.GetInvocationList())
                try { stop(); } catch (Exception error) { Current.Fail(error); }
        if (WorkerShutdown.Stop(workers.ToArray(), 500)) FinishStop();
        else Debug.LogWarning("Armok workers are stopping; resources and retry remain held until completion.");
    }

    private static void FinishStop()
    {
        foreach (var worker in workers) worker.WaitForStop(0);
        workers.Clear();
        foreach (var cleanup in release)
            try { cleanup(); } catch (Exception error) { Current.Fail(error); }
        release.Clear();
        Current.Stopped();
    }

    private void Update()
    {
        if (!Current.IsStopping && Current.NotificationError != null) Stop(Current, Current.NotificationError);
        if (Current.Stage == SessionStage.Stopping && workers.TrueForAll(worker => worker.IsStopped)) FinishStop();
        if (Current.Stage == SessionStage.Failed && !failureShown)
        {
            failureShown = true;
            Debug.LogWarning("Armok session failed: " + Current.Failure);
            if (ModalPanel.Instance != null)
                ModalPanel.Instance.Choice("Armok Vision could not finish this session.\n\n" + Current.Message +
                    "\n\nCheck that DF has a loaded save and DFHack is running, then retry.", Retry, Quit, "Retry", "Quit");
        }
    }

    public static void Retry()
    {
        if (Current.Stage != SessionStage.Failed && Current.Stage != SessionStage.Disconnected) return;
        if (workers.Count != 0) return;
        // Old components retain their original coordinator, so their OnDestroy cannot stop this one.
        Current = new SessionCoordinator(); Stopping = null; failureShown = false;
        ContentLoader.ResetForNewScene();
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }
    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    private void OnApplicationQuit() => Stop(Current);
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (host == this) host = null;
    }
}
