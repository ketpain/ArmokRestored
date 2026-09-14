/*
https://github.com/peterix/dfhack
Copyright (c) 2009-2012 Petr Mrázek (peterix@gmail.com)

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any
damages arising from the use of this software.

Permission is granted to anyone to use this software for any
purpose, including commercial applications, and to alter it and
redistribute it freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must
not claim that you wrote the original software. If you use this
software in a product, an acknowledgment in the product documentation
would be appreciated but is not required.

2. Altered source versions must be plainly marked as such, and
must not be misrepresented as being the original software.

3. This notice may not be removed or altered from any source
distribution.
*/

// Locally altered for bounded I/O and cooperative session shutdown; see Docs/ThirdParty.md.
using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using dfproto;

namespace DFHack
{
    public class RemoteClient
    {
        public static bool ReadFullBuffer(Socket socket, byte[] buf, int size)
        {
            if (socket == null || !socket.Connected)
                return false;

            if (size == 0)
                return true;
            int left = size;
            for (; left > 0;)
            {
                int cnt;
                try { cnt = socket.Receive(buf, size - left, left, SocketFlags.None); }
                catch { socket.Dispose(); throw; }
                if (cnt <= 0) return false;
                left -= cnt;
            }

            return true;
        }

        public bool Bind(IDFStream outStream, RemoteFunctionBase function,
                  string name, string proto)
        {
            if (!_active || Socket == null)
                return false;

            _bindCall.Reset();

            {
                var input = _bindCall.Input;

                input.method = name;
                if (proto.Length != 0)
                    input.plugin = proto;
                input.input_msg = function.PInTemplate.GetType().ToString();
                input.output_msg = function.POutTemplate.GetType().ToString();
            }

            if (_bindCall.TryExecute(outStream) != CommandResult.CrOk)
                return false;

            function.Id = (Int16)_bindCall.Output.assigned_id;

            return true;
        }

        public RemoteClient(IDFStream defaultOutput = null)
        {
            _pDefaultOutput = defaultOutput;
            _active = false;
            Socket = null;
            _suspendReady = false;

            if (_pDefaultOutput == null)
            {
                _deleteOutput = true;
                _pDefaultOutput = new ConsoleDFStream();
            }
            else
                _deleteOutput = false;
        }
        ~RemoteClient()
        {
            Interrupt();

            if (_deleteOutput)
                _pDefaultOutput = null;
        }

        public static int GetDefaultPort()
        {
            string port = Environment.GetEnvironmentVariable("DFHACK_PORT") ?? "0";

            int portval = int.Parse(port);
            if (portval <= 0)
                return 5000;
            else
                return portval;
        }

        public IDFStream DefaultOutput
        {
            get { return _pDefaultOutput; }
        }

        public int IoTimeoutMilliseconds { get; set; } = 5000;

        private Socket ConnectSocket(string server, int port)
        {
            Socket s = null;

            // Get host related information.
            IPAddress literal;
            IPAddress[] addresses;
            if (IPAddress.TryParse(server, out literal)) addresses = new[] { literal };
            else
            {
                var lookup = Dns.GetHostAddressesAsync(server);
                if (!lookup.Wait(2000)) throw new IOException("DF server name lookup timed out.");
                addresses = lookup.Result;
            }

            // Loop through the AddressList to obtain the supported AddressFamily. This is to avoid 
            // an exception that occurs when the host IP Address is not compatible with the address family 
            // (typical in the IPv6 case). 
            foreach (IPAddress address in addresses)
            {
                IPEndPoint ipe = new IPEndPoint(address, port);
                Socket tempSocket =
                    new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    var pending = tempSocket.BeginConnect(ipe, null, null);
                    using (pending.AsyncWaitHandle)
                    {
                        if (!pending.AsyncWaitHandle.WaitOne(2000))
                            throw new SocketException((int)SocketError.TimedOut);
                        tempSocket.EndConnect(pending);
                    }
                    tempSocket.ReceiveTimeout = IoTimeoutMilliseconds;
                    tempSocket.SendTimeout = IoTimeoutMilliseconds;
                }
                catch (SocketException)
                {
                    tempSocket.Dispose();
                    continue;
                }

                if (tempSocket.Connected)
                {
                    s = tempSocket;
                    break;
                }
            }
            return s;
        }

        static bool PartialArrayCompare(byte[] a, byte[] b) //compares the intersection of the two arrays, ignoring the rest.
        {
            int size = a.Length;
            if (size > b.Length) size = b.Length;
            for (int i = 0; i < size; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        public bool Connect(string server = "localhost", int port = -1)
        {
            try { return ConnectCore(server, port); }
            catch { Interrupt(); throw; }
        }

        private bool ConnectCore(string server, int port)
        {
            if (_active) throw new InvalidOperationException("Already connected.");

            if (port <= 0)
                port = GetDefaultPort();

            Socket = ConnectSocket(server, port);
            if (Socket == null)
            {
                DefaultOutput.PrintErr("Could not connect to %s:%d\n", server, port);
                return false;
            }

            _active = true;

            var headerList = new List<byte>();

            headerList.AddRange(Encoding.ASCII.GetBytes(RpcHandshakeHeader.RequestMagic));
            headerList.AddRange(BitConverter.GetBytes(1));

            byte[] header = headerList.ToArray();

            if (Socket.Send(header) != header.Length)
            {
                DefaultOutput.PrintErr("Could not send handshake header.\n");
                Socket.Close();
                Socket = null;
                return _active = false;
            }

            if (!ReadFullBuffer(Socket, header, header.Length))
            {
                DefaultOutput.PrintErr("Could not read handshake header.\n");
                Socket.Close();
                Socket = null;
                return _active = false;
            }

            if (!PartialArrayCompare(header, Encoding.ASCII.GetBytes(RpcHandshakeHeader.ResponseMagic)) ||
                BitConverter.ToInt32(header, Encoding.ASCII.GetBytes(RpcHandshakeHeader.ResponseMagic).Length) != 1)
            {
                DefaultOutput.PrintErr("Invalid handshake response: %s.\n", Encoding.Default.GetString(header));
                Socket.Close();
                Socket = null;
                return _active = false;
            }

            if (_bindCall == null) _bindCall = new RemoteFunction<CoreBindRequest, CoreBindReply>();
            _bindCall.Name = "BindMethod";
            _bindCall.PClient = this;
            _bindCall.Id = 0;

            if (_runcmdCall == null) _runcmdCall = new RemoteFunction<CoreRunCommandRequest>();
            _runcmdCall.Name = "RunCommand";
            _runcmdCall.PClient = this;
            _runcmdCall.Id = 1;

            return true;
        }

        public void Disconnect()
        {
            // Only the RPC owner may send Quit. Interrupt never writes into an in-flight RPC.
            var socket = Interlocked.Exchange(ref Socket, null);
            _active = false;
            _suspendReady = false;
            if (socket == null) return;
            try
            {
                socket.SendTimeout = 100;
                var header = new RpcMessageHeader { Id = (short)DfHackReplyCode.RpcRequestQuit, Size = 0 };
                socket.Send(header.ConvertToBtyes());
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { socket.Dispose(); }

        }

        // Safe from the stop thread: wakes a synchronous receive without sending another RPC.
        public void Interrupt()
        {
            _active = false;
            var socket = Interlocked.Exchange(ref Socket, null);
            if (socket == null) return;
            try { socket.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { socket.Dispose(); }
        }

        public CommandResult RunCommand(string cmd, List<string> args)
        {
            return RunCommand(DefaultOutput, cmd, args);
        }
        public CommandResult RunCommand(IDFStream output, string cmd, List<string> args)
        {
            if (!_active || Socket == null)
            {
                output.PrintErr("In RunCommand: client connection not valid.\n");
                return CommandResult.CrFailure;
            }

            _runcmdCall.Reset();

            _runcmdCall.Input.command = cmd;
            foreach (var t in args)
                _runcmdCall.Input.arguments.Add(t);

            return _runcmdCall.TryExecute(output);
        }

        //    // For executing multiple calls in rapid succession.
        //    // Best used via RemoteSuspender.
        public int SuspendGame()
        {
            if (!_active)
                return -1;

            if (!_suspendReady)
            {
                if (!_suspendCall.Bind(this, "CoreSuspend") || !_resumeCall.Bind(this, "CoreResume"))
                    return -1;
                _suspendReady = true;
            }

            if (_suspendCall.TryExecute(DefaultOutput) == CommandResult.CrOk)
                return _suspendCall.Output.value;
            else
                return -1;
        }
        public int ResumeGame()
        {
            if (!_suspendReady)
                return -1;

            if (_resumeCall.TryExecute(DefaultOutput) == CommandResult.CrOk)
                return _resumeCall.Output.value;
            else
                return -1;
        }

        //private:
        volatile bool _active;

        readonly bool _deleteOutput;
        public Socket Socket;
        IDFStream _pDefaultOutput;

        RemoteFunction<CoreBindRequest, CoreBindReply> _bindCall;
        RemoteFunction<CoreRunCommandRequest> _runcmdCall;

        bool _suspendReady;
        readonly RemoteFunction<EmptyMessage, IntMessage> _suspendCall = new RemoteFunction<EmptyMessage, IntMessage>();
        readonly RemoteFunction<EmptyMessage, IntMessage> _resumeCall = new RemoteFunction<EmptyMessage, IntMessage>();
    }
}
