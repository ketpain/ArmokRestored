using System;
using System.Diagnostics;
using System.Threading;

namespace Armok.Lifecycle
{
    public interface IConnectionOperations
    {
        void Poll();
        void Interrupt();
        void Close();
    }

    public interface ISessionWorker
    {
        void RequestStop();
        bool WaitForStop(int milliseconds);
        bool IsStopped { get; }
    }

    public static class WorkerShutdown
    {
        // Signal everyone first, then share one deadline across the entire group.
        public static bool Stop(ISessionWorker[] workers, int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            foreach (var worker in workers) worker.RequestStop();
            bool stopped = true;
            foreach (var worker in workers)
                stopped &= worker.WaitForStop(Math.Max(0, milliseconds - (int)watch.ElapsedMilliseconds));
            return stopped;
        }
    }

    public sealed class PollingWorker : ISessionWorker
    {
        private readonly IConnectionOperations operations;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly int delayMilliseconds;
        private readonly object stopLock = new object();
        private volatile bool requested;
        private bool handlesDisposed;
        private Exception failure;
        public Thread Thread { get; }
        public Exception Failure => Volatile.Read(ref failure);
        public bool IsStopped => !Thread.IsAlive;
        public PollingWorker(IConnectionOperations operations, int delayMilliseconds)
        {
            this.operations = operations;
            this.delayMilliseconds = Math.Max(1, delayMilliseconds);
            Thread = new Thread(Run) { IsBackground = true, Name = "Armok DF polling" };
            Thread.Start();
        }
        private void Run()
        {
            try
            {
                while (!stop.WaitOne(0))
                {
                    operations.Poll();
                    if (stop.WaitOne(delayMilliseconds)) break;
                }
            }
            catch (Exception error) { if (!requested) Volatile.Write(ref failure, error); }
            finally
            {
                try { operations.Close(); }
                catch (Exception error) { if (Failure == null && !requested) Volatile.Write(ref failure, error); }
            }
        }
        public void RequestStop()
        {
            lock (stopLock)
            {
                if (requested || handlesDisposed) return;
                requested = true; stop.Set();
                // The only concurrent transport operation: close the socket to wake Receive.
                operations.Interrupt();
            }
        }
        public bool WaitForStop(int milliseconds)
        {
            if (!Thread.Join(milliseconds)) return false;
            lock (stopLock)
                if (!handlesDisposed) { stop.Dispose(); handlesDisposed = true; }
            return true;
        }
    }
}
