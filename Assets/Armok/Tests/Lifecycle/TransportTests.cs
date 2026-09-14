using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Armok.Lifecycle;
using NUnit.Framework;

namespace Armok.Tests
{
    public class TransportTests
    {
        // Exercise the real vendored transport while it remains in Assembly-CSharp.
        private sealed class Client : IDisposable
        {
            private readonly Type type = Type.GetType("DFHack.RemoteClient, Assembly-CSharp", true);
            public readonly object value;
            public Client(int timeout = 300)
            {
                value = Activator.CreateInstance(type, new object[] { null });
                type.GetProperty("IoTimeoutMilliseconds").SetValue(value, timeout);
            }
            public object Call(string name, params object[] args)
            {
                try { return type.GetMethod(name).Invoke(value, args); }
                catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            }
            public bool Connect(int port) => (bool)Call("Connect", "127.0.0.1", port);
            public int Suspend() => (int)Call("SuspendGame");
            public int Resume() => (int)Call("ResumeGame");
            public void Interrupt() => Call("Interrupt");
            public bool HasSocket => type.GetField("Socket").GetValue(value) != null;
            public void Dispose() => Call("Disconnect");
        }

        private sealed class Peer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Thread thread;
            private Socket socket;
            public readonly ManualResetEvent accepted = new ManualResetEvent(false);
            public readonly ManualResetEvent closed = new ManualResetEvent(false);
            public readonly ManualResetEvent suspended = new ManualResetEvent(false);
            public readonly ManualResetEvent resumeEntered = new ManualResetEvent(false);
            public readonly string mode;
            public int resumes;
            public Exception failure;
            public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
            public Peer(string mode = "ok")
            {
                this.mode = mode; listener.Start();
                thread = new Thread(Run) { IsBackground = true, Name = "Armok test DF peer" }; thread.Start();
            }
            private static byte[] Read(Socket socket, int size)
            {
                var bytes = new byte[size];
                for (int offset = 0; offset < size;)
                {
                    int count = socket.Receive(bytes, offset, size - offset, SocketFlags.None);
                    if (count == 0) return null;
                    offset += count;
                }
                return bytes;
            }
            private void Reply(byte[] body)
            {
                byte[] header = new byte[8]; Array.Copy(BitConverter.GetBytes((short)-1), header, 2);
                Array.Copy(BitConverter.GetBytes(body.Length), 0, header, 4, 4);
                socket.Send(header); if (body.Length > 0) socket.Send(body);
            }
            private void Run()
            {
                try
                {
                    socket = listener.AcceptSocket(); socket.ReceiveTimeout = 3000; accepted.Set();
                    if (Read(socket, 12) == null) return;
                    if (mode == "stallHandshake") { Read(socket, 1); return; }
                    byte[] handshake = new byte[12];
                    Encoding.ASCII.GetBytes(mode == "badHandshake" ? "invalid!" : "DFHack!\n").CopyTo(handshake, 0);
                    BitConverter.GetBytes(1).CopyTo(handshake, 8); socket.Send(handshake);
                    while (true)
                    {
                        var header = Read(socket, 8); if (header == null) return;
                        int id = BitConverter.ToInt16(header, 0), size = BitConverter.ToInt32(header, 4);
                        var body = Read(socket, size); if (body == null) return;
                        if (id == -4) return;
                        if (id == 0)
                        {
                            if (mode == "rejectBind")
                            {
                                byte[] rejected = new byte[8]; BitConverter.GetBytes((short)-2).CopyTo(rejected, 0);
                                BitConverter.GetBytes(-1).CopyTo(rejected, 4); socket.Send(rejected); continue;
                            }
                            Reply(new byte[] { 8, (byte)(Encoding.UTF8.GetString(body).Contains("CoreSuspend") ? 2 : 3) });
                        }
                        else if (id == 2) { suspended.Set(); Reply(new byte[] { 8, 1 }); }
                        else if (id == 3)
                        {
                            Interlocked.Increment(ref resumes);
                            resumeEntered.Set();
                            if (mode == "dropResume") return;
                            if (mode == "stallResume") { Read(socket, 1); return; }
                            suspended.Reset(); Reply(new byte[] { 8, 0 });
                        }
                        else throw new IOException("Unexpected test RPC id " + id);
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (Exception error) { failure = error; }
                finally { socket?.Dispose(); suspended.Reset(); closed.Set(); }
            }
            public void Dispose()
            {
                listener.Stop(); socket?.Dispose();
                Assert.That(thread.Join(1000), Is.True, "Controlled peer leaked a thread.");
                accepted.Dispose(); closed.Dispose(); suspended.Dispose(); resumeEntered.Dispose();
                Assert.That(failure, Is.Null);
            }
        }

        [Test]
        public void AbsentPeerReturnsFailureWithoutKeepingSocket()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            using (var client = new Client())
            {
                Assert.That(client.Connect(port), Is.False);
                Assert.That(client.HasSocket, Is.False);
            }
        }

        [TestCase("badHandshake")]
        [TestCase("stallHandshake")]
        public void InvalidOrUnresponsiveHandshakeClosesTransport(string mode)
        {
            using (var peer = new Peer(mode))
            using (var client = new Client())
            {
                if (mode == "badHandshake") Assert.That(client.Connect(peer.Port), Is.False);
                else Assert.Throws<SocketException>(() => client.Connect(peer.Port));
                Assert.That(client.HasSocket, Is.False);
                Assert.That(peer.closed.WaitOne(1000), Is.True);
            }
        }

        [Test]
        public void SetupRejectionCannotRunSuspendedBody()
        {
            using (var peer = new Peer("rejectBind"))
            using (var client = new Client())
            {
                Assert.That(client.Connect(peer.Port), Is.True);
                Assert.Throws<IOException>(() => SuspendedOperation.Run(client.Suspend, client.Resume, client.Interrupt,
                    () => Assert.Fail("Should not run after rejected setup.")));
                Assert.That(client.HasSocket, Is.False);
                Assert.That(peer.closed.WaitOne(1000), Is.True);
            }
        }

        [Test]
        public void ExceptionInPollStillSendsExactlyOneResumeToPeer()
        {
            using (var peer = new Peer())
            using (var client = new Client())
            {
                Assert.That(client.Connect(peer.Port), Is.True);
                Assert.Throws<InvalidDataException>(() => SuspendedOperation.Run(client.Suspend, client.Resume,
                    client.Interrupt, () => throw new InvalidDataException("bad poll data")));
                Assert.That(peer.resumes, Is.EqualTo(1));
                Assert.That(peer.suspended.WaitOne(0), Is.False);
            }
        }

        [TestCase("dropResume")]
        [TestCase("stallResume")]
        public void MissingResumeAcknowledgementFailsAndClosesConnection(string mode)
        {
            using (var peer = new Peer(mode))
            using (var client = new Client())
            {
                Assert.That(client.Connect(peer.Port), Is.True);
                var error = Assert.Throws<IOException>(() => SuspendedOperation.Run(client.Suspend, client.Resume, client.Interrupt, () => { }));
                Assert.That(error.Message, Does.Contain("resume was not confirmed"));
                Assert.That(client.HasSocket, Is.False);
                Assert.That(peer.closed.WaitOne(1000), Is.True);
            }
        }

        private sealed class ClientOperations : IConnectionOperations
        {
            private readonly Client client;
            public ClientOperations(Client client) { this.client = client; }
            public void Poll() => SuspendedOperation.Run(client.Suspend, client.Resume, client.Interrupt, () => { });
            public void Interrupt() => client.Interrupt();
            public void Close() => client.Dispose();
        }

        [Test]
        public void StopInterruptsRealBlockedReceiveBeforeSocketTimeout()
        {
            using (var peer = new Peer("stallResume"))
            using (var client = new Client(10000))
            {
                Assert.That(client.Connect(peer.Port), Is.True);
                var worker = new PollingWorker(new ClientOperations(client), 60000);
                try
                {
                    Assert.That(peer.resumeEntered.WaitOne(1000), Is.True);
                    Assert.That(WorkerShutdown.Stop(new ISessionWorker[] { worker }, 500), Is.True);
                    Assert.That(client.HasSocket, Is.False);
                    Assert.That(peer.closed.WaitOne(1000), Is.True);
                    Assert.That(worker.Failure, Is.Null, "Deliberate cancellation became a polling failure.");
                }
                finally { worker.RequestStop(); worker.WaitForStop(1000); }
            }
        }
    }
}
