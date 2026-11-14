// LAN co-op session (up to 4 players): lobby (create / find via UDP broadcast / join by IP), everybody starts the same
// raid (map, time of day, season, weather and seed come from the host), the host simulates the world (bots, loot
// containers, loose items) and streams it; every player owns their character, health and inventory (each one's
// progress goes to their own profile). Hits: the shooter decides (our bullets on host's bots -> HitBot; host's bots on
// a remote player -> HitPlayer to that player). Friendly fire is off.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public enum NetRole { None, Host, Client }

    public class NetSession : MonoBehaviour
    {
        public static NetSession I;
        public static bool Active => I != null && I.Role != NetRole.None;
        public static bool IsHost => I != null && I.Role == NetRole.Host;
        public static bool IsClient => I != null && I.Role == NetRole.Client;
        /// <summary>A session exists: the main menu is replaced by the lobby.</summary>
        public static bool InLobby => Active;
        /// <summary>Lobby / status changed (UI refresh).</summary>
        public static event Action Changed;

        public const byte StLobby = 0, StLoading = 1, StRaid = 2, StResults = 3;
        public static readonly string[] StateNames = { "в лобби", "загрузка…", "в рейде", "итоги рейда" };

        public class Peer
        {
            public int pid = -1;
            public string nick = "?";
            public byte state;
            public NetConn conn;
            public bool welcomed, loaded, worldSent;
        }

        public NetRole Role { get; private set; }
        public int MyId { get; private set; } = -1;
        public string Status { get; private set; } = "";
        public bool Connecting { get; private set; }
        public readonly List<Peer> Peers = new List<Peer>();
        public string HostNick = "", LobbyMap = "";
        public bool HostInRaid;

        // raid
        public NetWorld PendingWorld;
        public List<Vector3> WorldFires;
        public bool RaidAborted { get; private set; }
        public string AbortReason = "";

        NetHost _host;
        NetClient _client;
        NetDiscovery _disc;
        string _myNick = "Игрок";
        int _nextPid = 1, _dropCounter;
        bool _worldReady, _localInRaid, _sentLeft, _hooks;
        float _nextState, _nextBots, _nextTime, _nextPoll, _nextDiscInfo;
        readonly Dictionary<int, LootContainer> _containers = new Dictionary<int, LootContainer>();
        readonly Dictionary<int, BotController> _bots = new Dictionary<int, BotController>();
        readonly Dictionary<int, string> _contSig = new Dictionary<int, string>();
        readonly List<byte[]> _deferred = new List<byte[]>();
        LootContainer _lastOpen;
        GUIStyle _tagStyle;

        public static NetSession Ensure()
        {
            if (I != null) return I;
            var g = new GameObject("NetSession");
            if (GameBootstrap.I != null) g.transform.SetParent(GameBootstrap.I.transform, false); else DontDestroyOnLoad(g);
            return I = g.AddComponent<NetSession>();
        }

        void Awake() { I = this; }
        void OnDestroy() { Shutdown(null); if (I == this) I = null; }
        void OnApplicationQuit() { Shutdown(null); }

        public static string DefaultNick()
        {
            var n = GameSettings.Nick;
            if (string.IsNullOrEmpty(n)) n = SaveSystem.Loaded ? SaveSystem.Current.nickname : "Выживший";
            return string.IsNullOrEmpty(n) ? "Выживший" : n;
        }

        void SetStatus(string s) { Status = s; Changed?.Invoke(); }

        // ================================================================== lobby: host / join / leave
        public bool StartHost(string nick, out string error)
        {
            Shutdown(null);
            _myNick = Clean(nick);
            _host = new NetHost();
            if (!_host.Start(NetConst.TcpPort, out var e))
            {
                _host = null;
                error = "Не удалось открыть порт TCP " + NetConst.TcpPort + " (" + e + ").\nВозможно, игра уже запущена хостом на этом ПК.";
                return false;
            }
            _disc = new NetDiscovery();
            if (!_disc.StartResponder(NetConst.UdpPort, out var e2)) { Debug.LogWarning("[Severny] LAN discovery off: " + e2); _disc = null; }
            Role = NetRole.Host; MyId = 0; _nextPid = 1;
            Peers.Clear();
            Peers.Add(new Peer { pid = 0, nick = _myNick, welcomed = true, state = StLobby });
            HostNick = _myNick;
            InstallHooks();
            var ips = NetDiscovery.LocalAddresses();
            SetStatus("Игра создана. Друзья находят её через «Найти игры» или по адресу: " + (ips.Count > 0 ? string.Join(", ", ips) : "127.0.0.1"));
            UpdateDiscoveryInfo();
            Debug.Log("[Severny] LAN host started on TCP " + NetConst.TcpPort);
            error = null;
            return true;
        }

        public void Join(string address, string nick)
        {
            Shutdown(null);
            _myNick = Clean(nick);
            address = (address ?? "").Trim();
            int port = NetConst.TcpPort;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out var p)) { port = p; address = address.Substring(0, colon); }
            if (address.Length == 0) address = "127.0.0.1";
            _client = new NetClient();
            Role = NetRole.Client; MyId = -1; Connecting = true;
            Peers.Clear();
            InstallHooks();
            SetStatus("Подключение к " + address + "…");
            _client.Connect(address, port);
        }

        /// <summary>Leave the lobby / stop hosting (in a raid: the raid goes on offline for this player).</summary>
        public void Leave()
        {
            Shutdown("Вы покинули лобби");
            Changed?.Invoke();
        }

        void Shutdown(string why)
        {
            if (Role == NetRole.None && _host == null && _client == null) return;
            try
            {
                var bye = new NetWriter(Msg.Bye).Str(why ?? "").ToArray();
                foreach (var p in Peers) if (p.conn != null) { p.conn.Send(bye); p.conn.CloseAfterSend(); }
                if (_client != null && _client.Conn != null) { _client.Conn.Send(bye); _client.Conn.CloseAfterSend(); }
            }
            catch (Exception e) { Debug.LogWarning("[Severny] LAN shutdown: " + e.Message); }
            _host?.Stop(); _host = null;
            _disc?.Stop(); _disc = null;
            _client = null;
            Role = NetRole.None; MyId = -1; Connecting = false;
            Peers.Clear();
            RemoveHooks();
            _worldReady = false; _localInRaid = false; PendingWorld = null; WorldFires = null;
            BotTargets.All.RemoveAll(t => t is NetAvatar);
        }

        static string Clean(string n)
        {
            n = (n ?? "").Replace("|", "").Trim();
            if (n.Length > 20) n = n.Substring(0, 20);
            return n.Length == 0 ? "Выживший" : n;
        }

        Peer PeerById(int pid) { foreach (var p in Peers) if (p.pid == pid) return p; return null; }
        Peer PeerByConn(NetConn c) { foreach (var p in Peers) if (p.conn == c) return p; return null; }
        public string NickOf(int pid) { var p = PeerById(pid); return p != null ? p.nick : "Игрок " + pid; }
        public int ClientsInRaid { get { int n = 0; foreach (var p in Peers) if (p.pid != MyId && (p.state == StRaid || p.state == StLoading)) n++; return n; } }

        void UpdateDiscoveryInfo()
        {
            if (_disc == null) return;
            int n = 0; foreach (var p in Peers) if (p.welcomed) n++;
            bool raid = GameBootstrap.I != null && (GameBootstrap.I.InRaid || GameBootstrap.I.Busy);
            _disc.Info = NetConst.Protocol + "|" + _myNick + "|" + n + "|" + NetConst.MaxPlayers + "|" + NetConst.TcpPort + "|" + (raid ? "в рейде" : "в лобби");
        }

        // ================================================================== poll
        void Update()
        {
            if (_host != null) while (_host != null && _host.Events.TryDequeue(out var e)) SafeHandle(() => HostEvent(e));
            if (_client != null) while (_client != null && _client.Events.TryDequeue(out var e)) SafeHandle(() => ClientEvent(e));
            if (Role == NetRole.None) return;
            float now = Time.unscaledTime;
            // dead connections (cable pulled, PC froze)
            if (IsHost) foreach (var p in Peers.ToArray()) if (p.conn != null && !p.conn.Closed && p.conn.SecondsSinceRecv > NetConst.TimeoutSeconds) p.conn.Close("нет ответа " + (int)NetConst.TimeoutSeconds + " с");
            if (IsClient && _client != null && _client.Conn != null && !_client.Conn.Closed && _client.Conn.SecondsSinceRecv > NetConst.TimeoutSeconds) _client.Conn.Close("нет ответа от хоста");
            if (IsHost && now >= _nextDiscInfo) { _nextDiscInfo = now + 1f; UpdateDiscoveryInfo(); }
            SafeHandle(RaidTick);
        }

        static void SafeHandle(Action a)
        {
            try { a(); }
            catch (Exception ex) { Debug.LogError("[Severny] LAN: " + ex); }
        }

        // ================================================================== host side
        void HostEvent(NetEvent e)
        {
            switch (e.type)
            {
                case NetEv.Connected:
                    Peers.Add(new Peer { conn = e.conn, nick = e.conn.remote });
                    break;
                case NetEv.Disconnected:
                    {
                        var p = PeerByConn(e.conn);
                        if (p == null) break;
                        Peers.Remove(p);
                        if (p.welcomed)
                        {
                            Hud.Notify(p.nick + " отключился" + (string.IsNullOrEmpty(e.info) || e.info == "local" ? "" : " (" + e.info + ")"), 4f);
                            UIManager.I?.Toast(p.nick + " отключился");
                            RemoveAvatar(p.pid);
                            if (_localInRaid) BroadcastRaid(new NetWriter(Msg.PlayerLeft).I32(p.pid).U8((int)RaidManager.Outcome.Left).ToArray(), -1);
                            BroadcastLobby();
                        }
                        break;
                    }
                case NetEv.Data:
                    {
                        var p = PeerByConn(e.conn);
                        if (p != null) HostMessage(p, e.data);
                        break;
                    }
            }
        }

        void HostMessage(Peer p, byte[] data)
        {
            var r = new NetReader(data);
            byte type = r.Type;
            if (!p.welcomed && type != Msg.Hello) return;
            switch (type)
            {
                case Msg.Hello:
                    {
                        if (p.welcomed) return;
                        int proto = r.I32(); string ver = r.Str(); string nick = Clean(r.Str());
                        int count = 0; foreach (var q in Peers) if (q.welcomed) count++;
                        string reject = proto != NetConst.Protocol ? "Разные версии игры (хост: " + Application.version + ", протокол " + NetConst.Protocol + "; у вас: " + ver + ", протокол " + proto + ")."
                                      : count >= NetConst.MaxPlayers ? "Лобби заполнено (максимум " + NetConst.MaxPlayers + " игрока)." : null;
                        if (reject != null) { p.conn.Send(new NetWriter(Msg.Reject).Str(reject).ToArray()); p.conn.CloseAfterSend(); Peers.Remove(p); return; }
                        string unique = nick; int k = 2;
                        while (Peers.Exists(q => q.welcomed && q.nick == unique)) unique = nick + " " + k++;
                        p.nick = unique; p.pid = _nextPid++; p.welcomed = true; p.state = StLobby;
                        p.conn.pid = p.pid;
                        p.conn.Send(new NetWriter(Msg.Welcome).I32(p.pid).Str(_myNick).Str(unique).ToArray());
                        Hud.Notify(unique + " подключился", 3f);
                        UIManager.I?.Toast(unique + " подключился");
                        BroadcastLobby();
                        break;
                    }
                case Msg.Loaded:
                    p.loaded = true; p.state = StLoading;
                    BroadcastLobby();
                    break;
                case Msg.Ready:
                    p.state = StLobby; p.loaded = false; p.worldSent = false;
                    BroadcastLobby();
                    break;
                case Msg.Bye:
                    p.conn.Close("local");
                    break;
                default:
                    if (!_localInRaid || !_worldReady || !p.worldSent) return;
                    RaidMessage(r, data, p.pid);
                    if (IsRelayed(type)) BroadcastRaid(data, p.pid);
                    if (type == Msg.PlayerDied || type == Msg.PlayerLeft) { p.state = StResults; BroadcastLobby(); }
                    break;
            }
        }

        static bool IsRelayed(byte t) => t == Msg.PState || t == Msg.Shot || t == Msg.Container || t == Msg.ItemDrop || t == Msg.ItemTake || t == Msg.PlayerDied || t == Msg.PlayerLeft;

        /// <summary>Host: to every client that is in the running raid (except one).</summary>
        void BroadcastRaid(byte[] msg, int exceptPid)
        {
            foreach (var p in Peers) if (p.conn != null && p.welcomed && p.worldSent && p.pid != exceptPid) p.conn.Send(msg);
        }

        void BroadcastAll(byte[] msg) { foreach (var p in Peers) if (p.conn != null && p.welcomed) p.conn.Send(msg); }

        void BroadcastLobby()
        {
            if (!IsHost) { Changed?.Invoke(); return; }
            var w = new NetWriter(Msg.Lobby);
            int n = 0; foreach (var p in Peers) if (p.welcomed) n++;
            w.U8(n);
            foreach (var p in Peers) if (p.welcomed) w.I32(p.pid).Str(p.nick).U8(p.state);
            bool raid = GameBootstrap.I != null && (GameBootstrap.I.InRaid || GameBootstrap.I.Busy);
            w.Bool(raid).Str(raid && GameBootstrap.I.CurrentMap != null ? GameBootstrap.I.CurrentMap.title : "");
            BroadcastAll(w.ToArray());
            UpdateDiscoveryInfo();
            Changed?.Invoke();
        }

        /// <summary>Host pressed «НАЧАТЬ РЕЙД»: everyone in the lobby loads the same raid.</summary>
        public void HostStartRaid(MapDef m, int bots, int tod, bool winter, int weather)
        {
            if (!IsHost || GameBootstrap.I == null || GameBootstrap.I.Busy || GameBootstrap.I.InRaid) return;
            int seed = (Environment.TickCount & 0x7FFFFFFF) | 1;
            var msg = new NetWriter(Msg.RaidStart).Str(m.id).U8(bots).U8(tod).Bool(winter).U8(weather).I32(seed).ToArray();
            foreach (var p in Peers)
            {
                if (p.conn == null || !p.welcomed) continue;
                if (p.state != StLobby) continue;                 // still on their results screen: they skip this raid
                p.loaded = false; p.worldSent = false; p.state = StLoading;
                p.conn.Send(msg);
            }
            BeginRaidState();
            var me = PeerById(0); if (me != null) me.state = StLoading;
            BroadcastLobby();
            GameBootstrap.I.StartRaid(m, bots, tod, winter, weather, seed);
        }

        void BeginRaidState()
        {
            _localInRaid = true; _worldReady = false; _sentLeft = false; RaidAborted = false; AbortReason = "";
            PendingWorld = null; WorldFires = null;
            _containers.Clear(); _bots.Clear(); _contSig.Clear(); _deferred.Clear();
            _lastOpen = null; _dropCounter = 0;
        }

        void SendWorld(Peer p)
        {
            var w = NetWorld.Capture(RaidManager.I);
            p.conn.Send(w.Write());
            p.worldSent = true; p.state = StRaid;
            Debug.Log("[Severny] LAN world sent to " + p.nick + ": containers " + w.containers.Count + ", items " + w.items.Count + ", bots " + w.bots.Count);
            BroadcastLobby();
        }

        // ================================================================== client side
        void ClientEvent(NetEvent e)
        {
            switch (e.type)
            {
                case NetEv.Connected:
                    SetStatus("Соединение установлено, вход в лобби…");
                    e.conn.Send(new NetWriter(Msg.Hello).I32(NetConst.Protocol).Str(Application.version).Str(_myNick).ToArray());
                    break;
                case NetEv.ConnectFailed:
                    {
                        string why = e.info;
                        Shutdown(null);
                        SetStatus("Не удалось подключиться: " + why);
                        UIManager.I?.ShowMessage("Нет подключения", "Не удалось подключиться к хосту: " + why +
                            "\n\nПроверьте адрес, что хост создал игру, и что брандмауэр Windows на ПК хоста разрешает игре доступ к сети (TCP " + NetConst.TcpPort + ").");
                        break;
                    }
                case NetEv.Disconnected:
                    {
                        string why = string.IsNullOrEmpty(AbortReason) ? e.info : AbortReason;
                        bool inRaid = _localInRaid;
                        bool wasWelcomed = MyId >= 0;
                        Shutdown(null);
                        RaidAborted = true; AbortReason = "Связь с хостом потеряна";
                        if (inRaid && RaidManager.I != null) RaidManager.I.EndByHost("Связь с хостом потеряна — вы эвакуированы со снаряжением");
                        SetStatus("Отключено от хоста" + (string.IsNullOrEmpty(why) || why == "local" ? "" : ": " + why));
                        if (!inRaid && UIManager.I != null && GameBootstrap.I != null && !GameBootstrap.I.InRaid && !GameBootstrap.I.Busy)
                        {
                            if (UIManager.I.Current != UIScreen.Lobby) UIManager.I.ShowMainMenu();
                            if (wasWelcomed) UIManager.I.ShowMessage("Лобби", "Соединение с хостом закрыто" + (string.IsNullOrEmpty(why) || why == "local" ? "." : ": " + why));
                        }
                        break;
                    }
                case NetEv.Data:
                    ClientMessage(e.data);
                    break;
            }
        }

        void ClientMessage(byte[] data)
        {
            var r = new NetReader(data);
            switch (r.Type)
            {
                case Msg.Welcome:
                    MyId = r.I32(); HostNick = r.Str(); _myNick = r.Str(); Connecting = false;
                    SetStatus("Вы в лобби игрока " + HostNick + ". Ожидание начала рейда…");
                    UIManager.I?.Toast("Подключено к " + HostNick);
                    break;
                case Msg.Reject:
                    {
                        string why = r.Str();
                        Shutdown(null);
                        SetStatus("Отказ: " + why);
                        UIManager.I?.ShowMessage("Хост отклонил подключение", why);
                        break;
                    }
                case Msg.Lobby:
                    {
                        Peers.Clear();
                        int n = r.U8();
                        for (int i = 0; i < n; i++) Peers.Add(new Peer { pid = r.I32(), nick = r.Str(), state = (byte)r.U8(), welcomed = true });
                        HostInRaid = r.Bool(); LobbyMap = r.Str();
                        Changed?.Invoke();
                        break;
                    }
                case Msg.RaidStart:
                    {
                        string mapId = r.Str(); int bots = r.U8(), tod = r.U8(); bool winter = r.Bool(); int weather = r.U8(); int seed = r.I32();
                        if (GameBootstrap.I == null || GameBootstrap.I.Busy || GameBootstrap.I.InRaid) { Debug.LogWarning("[Severny] LAN: raid start ignored (busy)"); return; }
                        BeginRaidState();
                        if (UIManager.I != null) UIManager.I.CloseModalIfOpen();
                        GameBootstrap.I.StartRaidNet(MapCatalog.Get(mapId), bots, tod, winter, weather, seed);
                        break;
                    }
                case Msg.World:
                    if (!_localInRaid) return;
                    PendingWorld = NetWorld.Read(r);
                    WorldFires = PendingWorld.fires;
                    break;
                case Msg.RaidEnd:
                    {
                        string why = r.Str();
                        if (!_localInRaid) return;
                        RaidAborted = true; AbortReason = why;
                        if (_worldReady && RaidManager.I != null) RaidManager.I.EndByHost(why);
                        break;
                    }
                case Msg.Bye:
                    AbortReason = r.Str();
                    break;
                case Msg.Toast:
                    Hud.Notify(r.Str(), 3f);
                    break;
                default:
                    if (!_localInRaid) return;
                    if (!_worldReady) { if (PendingWorld != null) _deferred.Add(data); return; }   // world arrives, not built yet
                    RaidMessage(r, data, -1);
                    break;
            }
        }

        // ================================================================== raid messages (both sides)
        void RaidMessage(NetReader r, byte[] data, int fromPid)
        {
            var rm = RaidManager.I;
            if (rm == null) return;
            switch (r.Type)
            {
                case Msg.PState:
                    {
                        int pid = r.I32(); if (fromPid >= 0) pid = fromPid;
                        Vector3 pos = r.V3(); float yaw = r.F32(), pitch = r.F32(); byte flags = (byte)r.U8(); string wid = r.Str();
                        if (pid == MyId) return;
                        if (!NetAvatar.All.TryGetValue(pid, out var a) || a == null)
                        {
                            a = NetAvatar.Create(pid, NickOf(pid), rm.runtimeRoot, pos, yaw);
                            a.health.Forward = di => OnAvatarHit(a, di);
                        }
                        a.ApplyState(pos, yaw, pitch, flags, wid);
                        break;
                    }
                case Msg.Shot:
                    {
                        int pid = r.I32(); if (fromPid >= 0) pid = fromPid;
                        Vector3 o = r.V3(), d = r.V3(); var ammo = ItemDB.Get(r.Str()); var w = ItemDB.Get(r.Str());
                        if (NetAvatar.All.TryGetValue(pid, out var a) && a != null) a.Shot(o, d, ammo, w);
                        break;
                    }
                case Msg.BotStates:
                    {
                        int n = r.I16();
                        for (int i = 0; i < n; i++)
                        {
                            int nid = r.I32(); Vector3 pos = r.V3(); float yaw = r.F32(), ax = r.F32(), ay = r.F32(); Vector3 vel = r.V3(); bool combat = r.Bool();
                            if (NetBotProxy.All.TryGetValue(nid, out var b) && b != null) b.ApplyState(pos, yaw, Quaternion.Euler(ax, ay, 0f), vel, combat);
                        }
                        break;
                    }
                case Msg.BotShot:
                    {
                        int nid = r.I32(); Vector3 o = r.V3(), d = r.V3();
                        if (NetBotProxy.All.TryGetValue(nid, out var b) && b != null) b.Shot(o, d);
                        break;
                    }
                case Msg.BotDie:
                    {
                        int nid = r.I32(), killer = r.I32(); Vector3 pt = r.V3(), dir = r.V3(); var loot = r.Items();
                        if (NetBotProxy.All.TryGetValue(nid, out var b) && b != null)
                        {
                            b.Die(pt, dir, loot);
                            var lc = b.GetComponent<LootContainer>(); if (lc != null) _containers[lc.nid] = lc;
                        }
                        if (killer == MyId && killer >= 0) rm.OnBotKilled(null, true);
                        else if (killer >= 0 && rm.Running) Hud.Notify(NickOf(killer) + ": противник нейтрализован", 1.5f);
                        break;
                    }
                case Msg.HitBot:
                    {
                        if (!IsHost) return;
                        int nid = r.I32(); var part = (BodyPart)r.U8(); float dmg = r.F32(), pen = r.F32(); Vector3 pt = r.V3(), dir = r.V3();
                        var bot = FindBot(nid);
                        if (bot == null || bot.IsDead) return;
                        NetAvatar.All.TryGetValue(fromPid, out var a);
                        bot.health.ApplyDamage(new DamageInfo { part = part, damage = dmg, penetration = pen, point = pt, dir = dir, attacker = a != null ? a.gameObject : null, bullet = true, cause = "bullet" });
                        if (!bot.IsDead)
                            SFX.PlayAt(bot.health.LastHitArmored ? SFX.Pick(SFX.ArmorHits, SFX.Thud) : SFX.Pick(SFX.FleshHits, SFX.Hit), pt, 0.8f, 2f, 60f, 0.1f);
                        break;
                    }
                case Msg.HitPlayer:
                    {
                        var part = (BodyPart)r.U8(); float dmg = r.F32(), pen = r.F32(); Vector3 pt = r.V3(), dir = r.V3(); bool bullet = r.Bool(); string cause = r.Str(); int botNid = r.I32();
                        var pc = PlayerController.Local;
                        if (pc == null || pc.health == null || pc.health.IsDead || !rm.Running) return;
                        NetBotProxy.All.TryGetValue(botNid, out var b);
                        pc.health.ApplyDamage(new DamageInfo { part = part, damage = dmg, penetration = pen, point = pt, dir = dir, bullet = bullet, cause = cause, attacker = b != null ? b.gameObject : null });
                        break;
                    }
                case Msg.Container:
                    {
                        int cnid = r.I32(); var items = r.Items();
                        var lc = FindContainer(cnid);
                        if (lc == null) return;
                        lc.ApplyRemote(items);
                        _contSig[cnid] = NetIO.Signature(lc.grid);
                        break;
                    }
                case Msg.ItemDrop:
                    {
                        int inid = r.I32(); var it = r.Item(); Vector3 pos = r.V3(); float yaw = r.F32();
                        foreach (var x in WorldItem.All) if (x != null && x.nid == inid) return;
                        WorldItem.SuppressNet = true;
                        try
                        {
                            var wi = WorldItem.Drop(it, pos, rm.runtimeRoot);
                            if (wi != null) { wi.nid = inid; wi.transform.position = pos; wi.transform.rotation = Quaternion.Euler(0f, yaw, 0f); }
                        }
                        finally { WorldItem.SuppressNet = false; }
                        break;
                    }
                case Msg.ItemTake:
                    {
                        int inid = r.I32();
                        foreach (var x in WorldItem.All.ToArray()) if (x != null && x.nid == inid) { x.gameObject.SetActive(false); Destroy(x.gameObject); }
                        break;
                    }
                case Msg.PlayerDied:
                    {
                        int pid = r.I32(); if (fromPid >= 0) pid = fromPid;
                        Vector3 pos = r.V3(); var items = r.Items();
                        if (pid == MyId) return;
                        int cnid = NetWorld.PlayerCorpseNid(pid);
                        LootContainer lc;
                        if (NetAvatar.All.TryGetValue(pid, out var a) && a != null) lc = a.MakeCorpse(items, cnid);
                        else
                        {
                            lc = LootContainer.Create(ItemDB.LootTable.Bag, pos, Quaternion.identity, rm.runtimeRoot, 1);
                            lc.title = "Вещи: " + NickOf(pid); lc.nid = cnid; lc.ApplyRemote(items);
                        }
                        _containers[cnid] = lc;
                        _contSig[cnid] = NetIO.Signature(lc.grid);
                        Hud.Notify(NickOf(pid) + " погиб", 4f);
                        break;
                    }
                case Msg.PlayerLeft:
                    {
                        int pid = r.I32(); if (fromPid >= 0) pid = fromPid;
                        var o = (RaidManager.Outcome)r.U8();
                        if (pid == MyId) return;
                        RemoveAvatar(pid);
                        Hud.Notify(NickOf(pid) + (o == RaidManager.Outcome.Survived ? " эвакуировался" : o == RaidManager.Outcome.MIA ? " пропал без вести" : " покинул рейд"), 4f);
                        break;
                    }
                case Msg.Time:
                    if (!IsHost && rm.Running) { rm.timeLeft = r.F32(); }
                    break;
            }
        }

        void RemoveAvatar(int pid)
        {
            if (NetAvatar.All.TryGetValue(pid, out var a) && a != null)
            {
                NetAvatar.All.Remove(pid);
                if (a.Dead && a.GetComponent<LootContainer>() != null) return;   // keep the lootable corpse
                a.gameObject.SetActive(false);
                Destroy(a.gameObject);
            }
        }

        LootContainer FindContainer(int cnid)
        {
            if (_containers.TryGetValue(cnid, out var lc) && lc != null) return lc;
            if (RaidManager.I == null) return null;
            _containers.Clear();
            foreach (var c in RaidManager.I.runtimeRoot.GetComponentsInChildren<LootContainer>(true)) if (c.nid != 0) _containers[c.nid] = c;
            return _containers.TryGetValue(cnid, out lc) ? lc : null;
        }

        BotController FindBot(int nid)
        {
            if (_bots.TryGetValue(nid, out var b) && b != null) return b;
            foreach (var x in BotController.All) if (x != null && x.nid == nid) { _bots[nid] = x; return x; }
            return null;
        }

        // ================================================================== raid lifecycle (called by GameBootstrap)
        /// <summary>Client: the map is loaded, the host may send the world.</summary>
        public void SendLoaded()
        {
            if (IsClient && _client != null && _client.Conn != null) _client.Conn.Send(new NetWriter(Msg.Loaded).ToArray());
        }

        public NetWorld TakeWorld() { var w = PendingWorld; PendingWorld = null; return w; }

        /// <summary>The local raid is running (host: ids assigned, worlds go out; client: world built, queued messages replayed).</summary>
        public void OnLocalRaidBegun()
        {
            if (!Active) return;
            var rm = RaidManager.I;
            if (rm == null) return;
            if (!_localInRaid) BeginRaidState();
            if (IsHost)
            {
                NetWorld.AssignIds(rm.runtimeRoot);
                foreach (var b in BotController.All) if (b != null && b.nid != 0) _bots[b.nid] = b;
                foreach (var c in rm.runtimeRoot.GetComponentsInChildren<LootContainer>(true)) if (c.nid != 0) _containers[c.nid] = c;
                var me = PeerById(0); if (me != null) me.state = StRaid;
                BroadcastLobby();
            }
            _worldReady = true;
            _nextState = _nextBots = _nextTime = _nextPoll = 0f;
            if (IsClient && _deferred.Count > 0)
            {
                var list = new List<byte[]>(_deferred); _deferred.Clear();
                foreach (var d in list) SafeHandle(() => RaidMessage(new NetReader(d), d, -1));
            }
        }

        /// <summary>The local raid result is final (extracted / killed / MIA / left).</summary>
        void OnLocalRaidEnding(RaidManager.Outcome o)
        {
            if (!_localInRaid || !_worldReady || _sentLeft) return;
            _sentLeft = true;
            var pc = PlayerController.Local;
            byte[] msg;
            if (o == RaidManager.Outcome.Killed)
                msg = new NetWriter(Msg.PlayerDied).I32(MyId).V3(pc != null ? pc.transform.position : Vector3.zero).Items(NetIO.DroppedOnDeath(pc != null ? pc.loadout : SaveSystem.Current.loadout)).ToArray();
            else
                msg = new NetWriter(Msg.PlayerLeft).I32(MyId).U8((int)o).ToArray();
            Send(msg);
            if (pc != null && o != RaidManager.Outcome.Killed) BotTargets.Remove(pc);    // extracted: the bots stop caring
            if (IsHost) { var me = PeerById(0); if (me != null) me.state = StResults; BroadcastLobby(); }
        }

        /// <summary>The local raid world is being destroyed (back to the menu / lobby).</summary>
        public void OnLocalRaidCleanup()
        {
            if (!_localInRaid) return;
            if (_worldReady && !_sentLeft) { _sentLeft = true; Send(new NetWriter(Msg.PlayerLeft).I32(MyId).U8((int)RaidManager.Outcome.Left).ToArray()); }
            if (IsHost)
            {
                if (ClientsInRaid > 0) BroadcastAll(new NetWriter(Msg.RaidEnd).Str("Хост завершил рейд — вы эвакуированы со всем снаряжением").ToArray());
                foreach (var p in Peers) { p.loaded = false; p.worldSent = false; if (p.pid == 0) p.state = StLobby; }
            }
            if (IsClient && _client != null && _client.Conn != null) _client.Conn.Send(new NetWriter(Msg.Ready).ToArray());
            _localInRaid = false; _worldReady = false; _sentLeft = false;
            PendingWorld = null; WorldFires = null;
            _containers.Clear(); _bots.Clear(); _contSig.Clear(); _deferred.Clear();
            NetAvatar.All.Clear(); NetBotProxy.All.Clear();
            BotTargets.All.RemoveAll(t => t is NetAvatar);
            if (IsHost) BroadcastLobby(); else Changed?.Invoke();
        }

        /// <summary>Client: the raid start failed locally (or was cancelled).</summary>
        public void OnLocalRaidFailed()
        {
            if (IsHost && _localInRaid) BroadcastAll(new NetWriter(Msg.RaidEnd).Str("Хост не смог запустить рейд").ToArray());
            OnLocalRaidCleanup();
        }

        void Send(byte[] msg)
        {
            if (IsHost) BroadcastRaid(msg, -1);
            else if (_client != null && _client.Conn != null) _client.Conn.Send(msg);
        }

        // ================================================================== per-frame raid streaming
        void RaidTick()
        {
            if (!_localInRaid) return;
            var rm = RaidManager.I;
            if (IsHost && _worldReady && rm != null && (WeatherFX.I == null || WeatherFX.I.FiresPlaced))
                foreach (var p in Peers) if (p.conn != null && p.welcomed && p.loaded && !p.worldSent) SendWorld(p);
            if (!_worldReady || rm == null) return;
            float now = Time.unscaledTime;
            if (now >= _nextState && !_sentLeft) { _nextState = now + 0.05f; SendMyState(); }
            if (IsHost && now >= _nextBots) { _nextBots = now + 0.1f; SendBotStates(); }
            if (IsHost && now >= _nextTime && rm.Running) { _nextTime = now + 5f; BroadcastRaid(new NetWriter(Msg.Time).F32(rm.timeLeft).ToArray(), -1); }
            if (now >= _nextPoll) { _nextPoll = now + 0.25f; PollContainers(); }
        }

        void SendMyState()
        {
            var pc = PlayerController.Local;
            if (pc == null || pc.health == null) return;
            byte f = 0;
            if (pc.Crouched) f |= NetAvatar.FCrouch;
            if (pc.IsSprinting) f |= NetAvatar.FSprint;
            if (pc.TorchOn) f |= NetAvatar.FTorch;
            if (pc.health.IsDead) f |= NetAvatar.FDead;
            if (pc.weapon != null && pc.weapon.IsAiming) f |= NetAvatar.FAim;
            if (pc.loadout.Get(EquipSlot.Armor) != null) f |= NetAvatar.FArmor;
            if (pc.loadout.Get(EquipSlot.Helmet) != null) f |= NetAvatar.FHelmet;
            float pitch = pc.camT.eulerAngles.x; if (pitch > 180f) pitch -= 360f;
            var wd = pc.weapon != null ? pc.weapon.Def : null;
            Send(new NetWriter(Msg.PState).I32(MyId).V3(pc.transform.position).F32(pc.transform.eulerAngles.y).F32(pitch).U8(f).Str(wd != null ? wd.id : "").ToArray());
        }

        void SendBotStates()
        {
            int n = 0;
            foreach (var b in BotController.All) if (b != null && b.nid != 0 && !b.IsDead) n++;
            if (n == 0) return;
            var w = new NetWriter(Msg.BotStates).I16(n);
            foreach (var b in BotController.All)
            {
                if (b == null || b.nid == 0 || b.IsDead) continue;
                var e = b.AimRotation.eulerAngles;
                w.I32(b.nid).V3(b.transform.position).F32(b.transform.eulerAngles.y).F32(e.x).F32(e.y).V3(b.Velocity).Bool(b.InCombat);
            }
            BroadcastRaid(w.ToArray(), -1);
        }

        void PollContainers()
        {
            var open = UIManager.I != null ? UIManager.I.OpenContainer : null;
            if (_lastOpen != null && _lastOpen != open) CheckContainer(_lastOpen);
            _lastOpen = open;
            if (open != null) CheckContainer(open);
        }

        void CheckContainer(LootContainer lc)
        {
            if (lc == null || lc.nid == 0 || lc.grid == null || !lc.Generated) return;
            string sig = NetIO.Signature(lc.grid);
            if (_contSig.TryGetValue(lc.nid, out var old) && old == sig) return;
            _contSig[lc.nid] = sig;
            _containers[lc.nid] = lc;
            Send(new NetWriter(Msg.Container).I32(lc.nid).Items(lc.grid.items).ToArray());
        }

        // ================================================================== game hooks
        void InstallHooks()
        {
            if (_hooks) return;
            _hooks = true;
            BotController.AnyShot += OnBotShot;
            BotController.AnyDied += OnBotDied;
            PlayerWeapon.LocalShot += OnLocalShot;
            RaidManager.LocalRaidEnding += OnLocalRaidEnding;
            WorldItem.NetDropped = OnItemDropped;
            WorldItem.NetTaken = OnItemTaken;
        }

        void RemoveHooks()
        {
            if (!_hooks) return;
            _hooks = false;
            BotController.AnyShot -= OnBotShot;
            BotController.AnyDied -= OnBotDied;
            PlayerWeapon.LocalShot -= OnLocalShot;
            RaidManager.LocalRaidEnding -= OnLocalRaidEnding;
            WorldItem.NetDropped = null;
            WorldItem.NetTaken = null;
        }

        void OnBotShot(BotController b, Vector3 from, Vector3 dir)
        {
            if (!IsHost || !_worldReady || b.nid == 0) return;
            BroadcastRaid(new NetWriter(Msg.BotShot).I32(b.nid).V3(from).V3(dir).ToArray(), -1);
        }

        void OnBotDied(BotController b, DamageInfo d)
        {
            if (!IsHost || !_worldReady || b.nid == 0) return;
            var lc = b.GetComponent<LootContainer>();
            if (lc != null) { lc.nid = NetWorld.BotCorpseNid(b.nid); _containers[lc.nid] = lc; }
            int killer = -1;
            if (d.attacker != null)
            {
                if (PlayerController.Local != null && d.attacker == PlayerController.Local.gameObject) killer = MyId;
                else { var a = d.attacker.GetComponent<NetAvatar>(); if (a != null) killer = a.pid; }
            }
            BroadcastRaid(new NetWriter(Msg.BotDie).I32(b.nid).I32(killer).V3(d.point).V3(d.dir).Items(lc != null && lc.grid != null ? lc.grid.items : null).ToArray(), -1);
            if (lc != null) _contSig[lc.nid] = NetIO.Signature(lc.grid);
        }

        void OnLocalShot(Vector3 origin, Vector3 dir, ItemDef ammo, ItemDef weapon)
        {
            if (!_localInRaid || !_worldReady || _sentLeft) return;
            Send(new NetWriter(Msg.Shot).I32(MyId).V3(origin).V3(dir).Str(ammo != null ? ammo.id : "").Str(weapon != null ? weapon.id : "").ToArray());
        }

        void OnItemDropped(WorldItem wi)
        {
            if (!_localInRaid || !_worldReady || wi == null || ItemInstance.IsEmpty(wi.item)) return;
            wi.nid = ((MyId + 1) << 24) | (++_dropCounter & 0xFFFFFF);
            Send(new NetWriter(Msg.ItemDrop).I32(wi.nid).Item(wi.item).V3(wi.transform.position).F32(wi.transform.eulerAngles.y).ToArray());
        }

        void OnItemTaken(WorldItem wi)
        {
            if (!_localInRaid || !_worldReady || wi == null || wi.nid == 0) return;
            Send(new NetWriter(Msg.ItemTake).I32(wi.nid).ToArray());
        }

        /// <summary>Client: our bullet hit a copy of a host bot.</summary>
        public void SendHitBot(int nid, DamageInfo di)
        {
            if (!IsClient || !_worldReady || _client == null || _client.Conn == null) return;
            if (PlayerController.Local == null || di.attacker != PlayerController.Local.gameObject) return;   // only our own shots count
            _client.Conn.Send(new NetWriter(Msg.HitBot).I32(nid).U8((int)di.part).F32(di.damage).F32(di.penetration).V3(di.point).V3(di.dir).ToArray());
        }

        /// <summary>Host: something hit another player's avatar — a bot bullet goes to that player (no friendly fire).</summary>
        void OnAvatarHit(NetAvatar a, DamageInfo di)
        {
            if (!IsHost || a == null || a.Dead || di.attacker == null) return;
            var bot = di.attacker.GetComponent<BotController>();
            if (bot == null) return;
            var p = PeerById(a.pid);
            if (p == null || p.conn == null) return;
            p.conn.Send(new NetWriter(Msg.HitPlayer).U8((int)di.part).F32(di.damage).F32(di.penetration).V3(di.point).V3(di.dir).Bool(di.bullet).Str(di.cause ?? "").I32(bot.nid).ToArray());
        }

        // ================================================================== name tags
        void OnGUI()
        {
            if (!_localInRaid || !_worldReady || NetAvatar.All.Count == 0) return;
            if (UIManager.I == null || UIManager.I.Current != UIScreen.Hud) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(12, Screen.height / 60), fontStyle = FontStyle.Bold };
            }
            foreach (var a in NetAvatar.All.Values)
            {
                if (a == null || a.Dead) continue;
                Vector3 p = a.Head + Vector3.up * 0.35f;
                float d = Vector3.Distance(cam.transform.position, p);
                if (d > 80f) continue;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                var rect = new Rect(sp.x - 120f, Screen.height - sp.y - 14f, 240f, 28f);
                string label = a.nick + (d > 25f ? "  " + Mathf.RoundToInt(d) + " м" : "");
                _tagStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), label, _tagStyle);
                _tagStyle.normal.textColor = new Color(0.55f, 1f, 0.6f, 0.95f);
                GUI.Label(rect, label, _tagStyle);
            }
        }
    }
}
