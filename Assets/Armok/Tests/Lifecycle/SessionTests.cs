using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Armok.Lifecycle;
using NUnit.Framework;

namespace Armok.Tests
{
    public class SessionTests
    {
        [Test]
        public void BrokenProgressObserverCannotPreventShutdownTransitions()
        {
            var session = new SessionCoordinator();
            session.Changed += () => throw new InvalidOperationException("missing progress UI");
            session.BeginConnect();
            Assert.That(session.NotificationError, Is.TypeOf<InvalidOperationException>());
            session.Fail(session.NotificationError); session.Stopped();
            Assert.That(session.Stage, Is.EqualTo(SessionStage.Failed));
            Assert.That(session.Failure.Message, Is.EqualTo("missing progress UI"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        public void IdleMesherStopsWithoutAnotherPoll(int threadCount)
        {
            var type = Type.GetType("BlockMesher, Assembly-CSharp", true);
            var catalogType = Type.GetType("ContentCatalog, Assembly-CSharp", true);
            var catalog = Activator.CreateInstance(catalogType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { null }, null);
            var mesher = (ISessionWorker)type.GetMethod("GetMesher").Invoke(null, new object[] { threadCount, catalog });
            try
            {
                Assert.That(WorkerShutdown.Stop(new[] { mesher }, 500), Is.True);
                Assert.That(mesher.IsStopped, Is.True);
                Assert.That(WorkerShutdown.Stop(new[] { mesher }, 0), Is.True);
            }
            finally { mesher.RequestStop(); mesher.WaitForStop(1000); }
        }

        [Test]
        public void StartupRunsDefinitionsThenContentThenPresentationExactlyOnce()
        {
            var session = new SessionCoordinator();
            var calls = new List<string>();
            session.OnDefinitions(() => calls.Add("definitions"));
            session.OnContent(() => Content(calls));
            session.OnReady(() => calls.Add("render"));
            session.BeginConnect(); session.Dispatch();
            Assert.That(calls, Is.Empty);
            session.DefinitionsReady(); session.Dispatch(); session.Dispatch();
            Assert.That(calls, Is.EqualTo(new[] { "definitions" }));
            var callbacks = session.BeginContent();
            using (var load = new LoadOperation(callbacks[0](), () => session.IsStopping))
            {
                Assert.That(load.MoveNext(), Is.True);
                Assert.That(session.IsReady, Is.False);
                Assert.That(load.MoveNext(), Is.False);
                Assert.That(load.Completed, Is.True);
            }
            session.ContentReady(); session.Dispatch(); session.Dispatch();
            Assert.That(calls, Is.EqualTo(new[] { "definitions", "content", "render" }));
            Assert.Throws<InvalidOperationException>(() => session.BeginContent());
        }
        private static IEnumerator Content(List<string> calls) { yield return null; calls.Add("content"); }

        [Test]
        public void RemovedAndFailedSessionCallbacksCannotLeakIntoRetry()
        {
            int called = 0;
            var first = new SessionCoordinator();
            var registration = first.OnDefinitions(() => called++);
            registration.Dispose(); registration.Dispose();
            first.BeginConnect(); first.DefinitionsReady(); first.Dispatch();
            first.OnReady(() => called++); first.OnContent(() => Content(new List<string>()));
            first.Fail(new IOException("offline")); first.Stopped(); first.Dispatch();
            Assert.That(first.Stage, Is.EqualTo(SessionStage.Failed));
            Assert.That(first.ContentCallbackCount, Is.Zero);
            var second = new SessionCoordinator();
            second.OnDefinitions(() => called++);
            second.BeginConnect(); second.DefinitionsReady(); second.Dispatch();
            first.BeginStop(); first.Stopped();
            Assert.That(called, Is.EqualTo(1));
            Assert.That(second.Stage, Is.EqualTo(SessionStage.Connecting));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedOrCancelledNestedLoadDisposesParentsAndChildren(bool cancel)
        {
            var cleanup = new List<string>(); bool stopped = false;
            using (var load = new LoadOperation(Parent(cleanup), () => stopped))
            {
                Assert.That(load.MoveNext(), Is.True);
                stopped = cancel;
                Assert.That(load.MoveNext(), Is.False);
                Assert.That(load.Completed, Is.False);
                if (cancel) Assert.That(load.Failure, Is.Null);
                else Assert.That(load.Failure, Is.TypeOf<IOException>());
            }
            Assert.That(cleanup, Is.EqualTo(new[] { "child", "parent" }));
        }
        private static IEnumerator Parent(List<string> cleanup)
        {
            try { yield return Child(cleanup); }
            finally { cleanup.Add("parent"); }
        }
        private static IEnumerator Child(List<string> cleanup)
        {
            try { yield return null; throw new IOException("content invalid"); }
            finally { cleanup.Add("child"); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SuspensionAlwaysResumesAndPreservesFailure(bool operationFails)
        {
            int resumes = 0, interrupts = 0;
            Action run = () => SuspendedOperation.Run(() => 1, () => { resumes++; return 0; },
                () => interrupts++, () => { if (operationFails) throw new IOException("poll failed"); });
            if (operationFails) Assert.Throws<IOException>(() => run()); else run();
            Assert.That(resumes, Is.EqualTo(1)); Assert.That(interrupts, Is.Zero);
        }

        [Test]
        public void UnconfirmedResumeClosesTransportAndPreservesBothErrors()
        {
            int interrupts = 0;
            var error = Assert.Throws<AggregateException>(() => SuspendedOperation.Run(() => 1,
                () => throw new IOException("peer closed"), () => interrupts++, () => throw new ArgumentException("bad data")));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ArgumentException>());
            Assert.That(error.InnerExceptions[1].Message, Does.Contain("resume was not confirmed"));
            Assert.That(interrupts, Is.EqualTo(1));
        }

        [Test]
        public void RejectedSuspendDoesNotExecuteOrResume()
        {
            Assert.Throws<IOException>(() => SuspendedOperation.Run(() => -1,
                () => throw new AssertionException("resume"), () => { }, () => Assert.Fail("operation")));
        }

        private sealed class FakeConnection : IConnectionOperations, IDisposable
        {
            public readonly ManualResetEvent entered = new ManualResetEvent(false);
            public readonly ManualResetEvent gate = new ManualResetEvent(false);
            public bool ignoreInterrupt;
            public bool fail;
            public int closed;
            public void Poll() { entered.Set(); if (fail) throw new IOException("peer lost"); gate.WaitOne(); }
            public void Interrupt() { if (!ignoreInterrupt) gate.Set(); }
            public void Close() { Interlocked.Increment(ref closed); }
            public void Dispose() { entered.Dispose(); gate.Dispose(); }
        }

        [Test]
        public void StopWakesBlockedPollAndLongRefreshDelay()
        {
            using (var connection = new FakeConnection())
            {
                var worker = new PollingWorker(connection, 60000);
                try
                {
                    Assert.That(connection.entered.WaitOne(1000), Is.True);
                    Assert.That(WorkerShutdown.Stop(new ISessionWorker[] { worker }, 500), Is.True);
                    Assert.That(worker.IsStopped, Is.True);
                    Assert.That(worker.Failure, Is.Null);
                    Assert.That(connection.closed, Is.EqualTo(1));
                    worker.RequestStop(); Assert.That(worker.WaitForStop(0), Is.True);
                }
                finally { connection.gate.Set(); worker.RequestStop(); worker.WaitForStop(1000); }
            }
        }

        [Test]
        public void PollFailureClosesOwnedConnectionWithoutNeedingAnotherPoll()
        {
            using (var connection = new FakeConnection { fail = true })
            {
                var worker = new PollingWorker(connection, 1);
                Assert.That(worker.WaitForStop(1000), Is.True);
                Assert.That(worker.Failure, Is.TypeOf<IOException>());
                Assert.That(connection.closed, Is.EqualTo(1));
            }
        }

        [Test]
        public void MultipleBlockedWorkersShareOneStopDeadlineAndRemainOwnedUntilFinished()
        {
            var connections = new List<FakeConnection>(); var workers = new List<ISessionWorker>();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var connection = new FakeConnection { ignoreInterrupt = true }; connections.Add(connection);
                    workers.Add(new PollingWorker(connection, 1));
                    Assert.That(connection.entered.WaitOne(1000), Is.True);
                }
                var watch = Stopwatch.StartNew();
                Assert.That(WorkerShutdown.Stop(workers.ToArray(), 100), Is.False);
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(300), "Timeout accumulated once per worker.");
                Assert.That(workers.TrueForAll(worker => !worker.IsStopped), Is.True);
            }
            finally
            {
                foreach (var connection in connections) connection.gate.Set();
                Assert.That(WorkerShutdown.Stop(workers.ToArray(), 1000), Is.True);
                foreach (var connection in connections) connection.Dispose();
            }
        }
    }
}
