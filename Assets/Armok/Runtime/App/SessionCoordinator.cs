using System;
using System.Collections;
using System.Collections.Generic;

namespace Armok.Lifecycle
{
    public enum SessionStage { Disconnected, Connecting, LoadingContent, Ready, Stopping, Failed }

    // One instance per scene session. All methods and notifications run on the owner thread.
    public sealed class SessionCoordinator
    {
        private readonly List<Action> definitions = new List<Action>();
        private readonly List<Action> ready = new List<Action>();
        private readonly List<Func<IEnumerator>> content = new List<Func<IEnumerator>>();
        private bool definitionsAvailable;
        public SessionStage Stage { get; private set; }
        public Exception Failure { get; private set; }
        public Exception NotificationError { get; private set; }
        public string Message { get; private set; } = "Disconnected";
        public float Progress { get; private set; }
        public bool IsReady => Stage == SessionStage.Ready;
        public bool IsStopping => Stage == SessionStage.Stopping || Stage == SessionStage.Failed || Stage == SessionStage.Disconnected;
        public int ContentCallbackCount => content.Count;
        public event Action Changed;

        public IDisposable OnDefinitions(Action callback) => Register(definitions, callback);
        public IDisposable OnReady(Action callback) => Register(ready, callback);
        public IDisposable OnContent(Func<IEnumerator> callback) => Register(content, callback);

        private static IDisposable Register<T>(List<T> list, T callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (list.Contains(callback)) throw new InvalidOperationException("Callback already registered.");
            list.Add(callback);
            return new Registration(() => list.Remove(callback));
        }

        private sealed class Registration : IDisposable
        {
            private Action remove;
            public Registration(Action remove) { this.remove = remove; }
            public void Dispose() { var action = remove; remove = null; action?.Invoke(); }
        }

        public void BeginConnect()
        {
            Require(SessionStage.Disconnected);
            Set(SessionStage.Connecting, "Connecting to Dwarf Fortress");
        }
        public void DefinitionsReady() { Require(SessionStage.Connecting); definitionsAvailable = true; }

        // Defer dispatch until Update: every scene component's Start must register first.
        public void Dispatch()
        {
            if (IsStopping) return;
            if (definitionsAvailable) Drain(definitions);
            if (IsReady) Drain(ready);
        }

        private void Drain(List<Action> callbacks)
        {
            var pending = callbacks.ToArray();
            callbacks.Clear();
            foreach (var callback in pending)
            {
                if (IsStopping) break;
                callback();
            }
        }

        public Func<IEnumerator>[] BeginContent()
        {
            Require(SessionStage.Connecting);
            if (!definitionsAvailable) throw new InvalidOperationException("DF definitions are not available.");
            Set(SessionStage.LoadingContent, "Loading content");
            return content.ToArray();
        }
        public void Report(string message, float progress)
        {
            if (Stage != SessionStage.LoadingContent) return;
            Message = message; Progress = progress; Notify();
        }
        public void ContentReady()
        {
            Require(SessionStage.LoadingContent);
            Progress = 1; Set(SessionStage.Ready, "Ready");
            // Publish dependent presentation in the same owner-thread turn as content.
            // Waiting for a later Update would expose content before map buffers exist.
            Drain(ready);
        }
        public void Fail(Exception failure)
        {
            if (Failure == null) Failure = failure ?? new Exception("Session failed.");
            BeginStop();
        }
        public void BeginStop()
        {
            if (Stage == SessionStage.Stopping || Stage == SessionStage.Failed) return;
            definitions.Clear(); ready.Clear(); content.Clear();
            Set(SessionStage.Stopping, "Stopping");
        }
        public void Stopped()
        {
            if (Stage == SessionStage.Disconnected || Stage == SessionStage.Failed) return;
            Require(SessionStage.Stopping);
            Set(Failure == null ? SessionStage.Disconnected : SessionStage.Failed,
                Failure == null ? "Disconnected" : Failure.Message);
        }
        private void Require(SessionStage expected)
        {
            if (Stage != expected) throw new InvalidOperationException($"Expected {expected}, received {Stage}.");
        }
        private void Set(SessionStage stage, string message) { Stage = stage; Message = message; Notify(); }
        private void Notify()
        {
            if (Changed == null) return;
            foreach (Action observer in Changed.GetInvocationList())
                try { observer(); }
                catch (Exception error) { if (NotificationError == null) NotificationError = error; }
        }
    }
}
