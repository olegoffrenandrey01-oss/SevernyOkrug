// LAN transport for the co-op mode: TCP with length-prefixed frames (one receive + one send thread per connection,
// keep-alive pings from the send thread) and UDP broadcast discovery of hosts. Plain .NET sockets (no Unity API, no
// packages): network events are queued here and handled on the main thread by NetSession.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Severny
{
    public static class NetConst
    {
        public const int TcpPort = 27015, UdpPort = 27016;
        public const int Protocol = 9;                 // bump when messages or the shared world change (game v09: city dress)
        public const int MaxPlayers = 4;
        public const int MaxFrame = 16 * 1024 * 1024;
        public const float TimeoutSeconds = 25f;
        public const string DiscoverRequest = "SEVERNY?", DiscoverReply = "SEVERNY!";
    }

    public enum NetEv { Connected, Data, Disconnected, ConnectFailed }

    public struct NetEvent
    {
        public NetEv type;
        public NetConn conn;
        public byte[] data;
        public string info;
    }

    /// <summary>One TCP connection. Send() is thread-safe and never blocks the caller.</summary>
    public sealed class NetConn
    {
        static readonly byte[] PingFrame = { 0 };
        static readonly byte[] CloseSentinel = new byte[0];

        public int pid = -1;                // player id (assigned by the host)
        public readonly string remote;
        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly BlockingCollection<byte[]> _out = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
        readonly ConcurrentQueue<NetEvent> _events;
        int _closed;
        long _lastRecv;
        public long BytesIn, BytesOut;

        public bool Closed => Volatile.Read(ref _closed) != 0;
        public double SecondsSinceRecv => (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastRecv)) / (double)Stopwatch.Frequency;
        public int Queued => _out.Count;

        internal NetConn(TcpClient tcp, ConcurrentQueue<NetEvent> events)
        {
            _tcp = tcp; _events = events;
            tcp.NoDelay = true;
            tcp.ReceiveBufferSize = 1 << 20; tcp.SendBufferSize = 1 << 20;
            _stream = tcp.GetStream();
            try { remote = tcp.Client.RemoteEndPoint != null ? tcp.Client.RemoteEndPoint.ToString() : "?"; } catch { remote = "?"; }
            Interlocked.Exchange(ref _lastRecv, Stopwatch.GetTimestamp());
        }

        internal void StartThreads()
        {
            new Thread(RecvLoop) { IsBackground = true, Name = "SevNetRecv" }.Start();
            new Thread(SendLoop) { IsBackground = true, Name = "SevNetSend" }.Start();
        }

        /// <summary>Queues one message (first byte = message type). The 4-byte length prefix is added by the send thread.</summary>
        public void Send(byte[] msg)
        {
            if (msg == null || msg.Length == 0 || Closed) return;
            try { _out.Add(msg); } catch (InvalidOperationException) { }
        }

        /// <summary>Sends everything queued so far, then closes the connection.</summary>
        public void CloseAfterSend()
        {
            if (Closed) return;
            try { _out.Add(CloseSentinel); } catch { Close("local"); }
        }

        void SendLoop()
        {
            var bs = new BufferedStream(_stream, 64 * 1024);
            var len = new byte[4];
            try
            {
                while (!Closed)
                {
                    if (!_out.TryTake(out var f, 1000)) f = PingFrame;           // keep-alive while idle
                    if (f == null) continue;
                    if (ReferenceEquals(f, CloseSentinel)) { bs.Flush(); Close("local"); break; }
                    int n = f.Length;
                    len[0] = (byte)n; len[1] = (byte)(n >> 8); len[2] = (byte)(n >> 16); len[3] = (byte)(n >> 24);
                    bs.Write(len, 0, 4);
                    bs.Write(f, 0, n);
                    BytesOut += n + 4;
                    if (_out.Count == 0) bs.Flush();
                }
            }
            catch (Exception e) { Close("send: " + e.Message); }
        }

        void RecvLoop()
        {
            var len = new byte[4];
            try
            {
                while (!Closed)
                {
                    if (!ReadExact(len, 4)) break;
                    int n = len[0] | (len[1] << 8) | (len[2] << 16) | (len[3] << 24);
                    if (n <= 0 || n > NetConst.MaxFrame) throw new IOException("bad frame length " + n);
                    var data = new byte[n];
                    if (!ReadExact(data, n)) break;
                    BytesIn += n + 4;
                    Interlocked.Exchange(ref _lastRecv, Stopwatch.GetTimestamp());
                    if (n == 1 && data[0] == 0) continue;                          // ping
                    _events.Enqueue(new NetEvent { type = NetEv.Data, conn = this, data = data });
                }
                Close("соединение закрыто");
            }
            catch (Exception e) { Close(e.Message); }
        }

        bool ReadExact(byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = _stream.Read(b, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        public void Close(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _out.CompleteAdding(); } catch { }
            try { _tcp.Close(); } catch { }
            _events.Enqueue(new NetEvent { type = NetEv.Disconnected, conn = this, info = reason });
        }
    }

    /// <summary>Accepts incoming connections (host).</summary>
    public sealed class NetHost
    {
        public readonly ConcurrentQueue<NetEvent> Events = new ConcurrentQueue<NetEvent>();
        TcpListener _listener;
        volatile bool _running;

        public bool Start(int port, out string error)
        {
            error = null;
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start(8);
                _running = true;
                new Thread(AcceptLoop) { IsBackground = true, Name = "SevNetAccept" }.Start();
                return true;
            }
            catch (Exception e) { error = e.Message; _listener = null; return false; }
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var tcp = _listener.AcceptTcpClient();
                    if (!_running) { tcp.Close(); break; }
                    var c = new NetConn(tcp, Events);
                    Events.Enqueue(new NetEvent { type = NetEv.Connected, conn = c });
                    c.StartThreads();
                }
                catch (Exception) { if (!_running) break; Thread.Sleep(50); }
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }
    }

    /// <summary>Connects to a host in a background thread (the UI never freezes).</summary>
    public sealed class NetClient
    {
        public readonly ConcurrentQueue<NetEvent> Events = new ConcurrentQueue<NetEvent>();
        public volatile NetConn Conn;

        public void Connect(string host, int port, int timeoutMs = 4000)
        {
            new Thread(() =>
            {
                TcpClient tcp = null;
                try
                {
                    tcp = new TcpClient(AddressFamily.InterNetwork);
                    var ar = tcp.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !tcp.Connected)
                    {
                        try { tcp.Close(); } catch { }
                        Events.Enqueue(new NetEvent { type = NetEv.ConnectFailed, info = "нет ответа от " + host + ":" + port });
                        return;
                    }
                    tcp.EndConnect(ar);
                    var c = new NetConn(tcp, Events);
                    Conn = c;
                    Events.Enqueue(new NetEvent { type = NetEv.Connected, conn = c });
                    c.StartThreads();
                }
                catch (Exception e)
                {
                    try { tcp?.Close(); } catch { }
                    Events.Enqueue(new NetEvent { type = NetEv.ConnectFailed, info = e.Message });
                }
            }) { IsBackground = true, Name = "SevNetConnect" }.Start();
        }

        public void Close() { var c = Conn; Conn = null; c?.Close("local"); }
    }

    /// <summary>UDP discovery: the host answers broadcast requests; clients search the local network.</summary>
    public sealed class NetDiscovery
    {
        public struct Found { public string address; public int port, players, max, protocol; public string name, status; }

        UdpClient _udp;
        volatile bool _run;
        /// <summary>Reply payload (set by the main thread): "protocol|name|players|max|tcpPort|status".</summary>
        public volatile string Info = "";

        public bool StartResponder(int port, out string error)
        {
            error = null;
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _run = true;
                new Thread(ResponderLoop) { IsBackground = true, Name = "SevNetDiscovery" }.Start();
                return true;
            }
            catch (Exception e) { error = e.Message; try { _udp?.Close(); } catch { } _udp = null; return false; }
        }

        void ResponderLoop()
        {
            while (_run)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = _udp.Receive(ref ep);
                    if (data == null || data.Length < NetConst.DiscoverRequest.Length) continue;
                    if (!Encoding.UTF8.GetString(data).StartsWith(NetConst.DiscoverRequest, StringComparison.Ordinal)) continue;
                    var reply = Encoding.UTF8.GetBytes(NetConst.DiscoverReply + "|" + Info);
                    _udp.Send(reply, reply.Length, ep);
                }
                catch (Exception) { if (!_run) break; Thread.Sleep(20); }
            }
        }

        public void Stop()
        {
            _run = false;
            try { _udp?.Close(); } catch { }
            _udp = null;
        }

        /// <summary>Broadcasts a request (global + every interface's subnet + localhost) and collects replies for timeoutMs.
        /// Runs in a background thread; results are added to the queue, then `done` is set.</summary>
        public static void Search(int port, int timeoutMs, ConcurrentQueue<Found> results, Action done)
        {
            new Thread(() =>
            {
                UdpClient udp = null;
                try
                {
                    udp = new UdpClient(0);
                    udp.EnableBroadcast = true;
                    udp.Client.ReceiveTimeout = 250;
                    var req = Encoding.UTF8.GetBytes(NetConst.DiscoverRequest + NetConst.Protocol);
                    var targets = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
                    try
                    {
                        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus != OperationalStatus.Up) continue;
                            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                            {
                                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                                var a = ua.Address.GetAddressBytes(); var m = ua.IPv4Mask.GetAddressBytes();
                                if (a.Length != 4 || m.Length != 4) continue;
                                var b = new byte[4];
                                for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                                var bc = new IPAddress(b);
                                if (!targets.Contains(bc)) targets.Add(bc);
                            }
                        }
                    }
                    catch (Exception) { }
                    var seen = new HashSet<string>();
                    var sw = Stopwatch.StartNew();
                    long nextSend = 0;
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        if (sw.ElapsedMilliseconds >= nextSend)
                        {
                            nextSend += 600;
                            foreach (var t in targets) { try { udp.Send(req, req.Length, new IPEndPoint(t, port)); } catch (Exception) { } }
                        }
                        try
                        {
                            var ep = new IPEndPoint(IPAddress.Any, 0);
                            var data = udp.Receive(ref ep);
                            var s = Encoding.UTF8.GetString(data);
                            if (!s.StartsWith(NetConst.DiscoverReply + "|", StringComparison.Ordinal)) continue;
                            var f = s.Split('|');
                            if (f.Length < 7) continue;
                            var found = new Found { address = ep.Address.ToString(), name = f[2], status = f[6] };
                            int.TryParse(f[1], out found.protocol); int.TryParse(f[3], out found.players); int.TryParse(f[4], out found.max); int.TryParse(f[5], out found.port);
                            // the same host answers on several interfaces: keep the first (a loopback reply = this PC)
                            string key = found.name + ":" + found.port + ":" + (IPAddress.IsLoopback(ep.Address) ? "lo" : found.address);
                            if (seen.Add(key)) results.Enqueue(found);
                        }
                        catch (SocketException) { }
                    }
                }
                catch (Exception) { }
                finally { try { udp?.Close(); } catch { } done?.Invoke(); }
            }) { IsBackground = true, Name = "SevNetSearch" }.Start();
        }

        /// <summary>IPv4 addresses of this PC (shown to the host so friends can type it in).</summary>
        public static List<string> LocalAddresses()
        {
            var l = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        {
                            var s = ua.Address.ToString();
                            if (!s.StartsWith("169.254.") && !l.Contains(s)) l.Add(s);
                        }
                }
            }
            catch (Exception) { }
            return l;
        }
    }

    // ------------------------------------------------------------------ binary messages
    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits { [FieldOffset(0)] public float f; [FieldOffset(0)] public int i; }

    public sealed class NetWriter
    {
        byte[] _b = new byte[256];
        int _n;

        public NetWriter(byte type) { _b[0] = type; _n = 1; }
        public int Length => _n;

        void Need(int k) { if (_n + k > _b.Length) Array.Resize(ref _b, Math.Max(_b.Length * 2, _n + k)); }

        public NetWriter U8(int v) { Need(1); _b[_n++] = (byte)v; return this; }
        public NetWriter Bool(bool v) => U8(v ? 1 : 0);
        public NetWriter I16(int v) { Need(2); _b[_n++] = (byte)v; _b[_n++] = (byte)(v >> 8); return this; }
        public NetWriter I32(int v) { Need(4); _b[_n++] = (byte)v; _b[_n++] = (byte)(v >> 8); _b[_n++] = (byte)(v >> 16); _b[_n++] = (byte)(v >> 24); return this; }
        public NetWriter F32(float v) { var u = new FloatBits { f = v }; return I32(u.i); }
        public NetWriter Str(string s)
        {
            if (string.IsNullOrEmpty(s)) return I16(0);
            var bytes = Encoding.UTF8.GetBytes(s);
            int n = Math.Min(bytes.Length, 32000);
            I16(n); Need(n); Buffer.BlockCopy(bytes, 0, _b, _n, n); _n += n;
            return this;
        }
        public NetWriter Bytes(byte[] d) { I32(d.Length); Need(d.Length); Buffer.BlockCopy(d, 0, _b, _n, d.Length); _n += d.Length; return this; }

        public byte[] ToArray() { var r = new byte[_n]; Buffer.BlockCopy(_b, 0, r, 0, _n); return r; }
    }

    public sealed class NetReader
    {
        readonly byte[] _b;
        int _p;
        public NetReader(byte[] b, int start = 1) { _b = b; _p = start; }
        public byte Type => _b[0];
        public bool End => _p >= _b.Length;

        void Need(int k) { if (_p + k > _b.Length) throw new EndOfStreamException("net message too short"); }
        public int U8() { Need(1); return _b[_p++]; }
        public bool Bool() => U8() != 0;
        public int I16() { Need(2); int v = (short)(_b[_p] | (_b[_p + 1] << 8)); _p += 2; return v; }
        public int I32() { Need(4); int v = _b[_p] | (_b[_p + 1] << 8) | (_b[_p + 2] << 16) | (_b[_p + 3] << 24); _p += 4; return v; }
        public float F32() { var u = new FloatBits { i = I32() }; return u.f; }
        public string Str() { int n = I16() & 0xFFFF; if (n == 0) return ""; Need(n); var s = Encoding.UTF8.GetString(_b, _p, n); _p += n; return s; }
        public byte[] Bytes() { int n = I32(); Need(n); var r = new byte[n]; Buffer.BlockCopy(_b, _p, r, 0, n); _p += n; return r; }
    }
}
