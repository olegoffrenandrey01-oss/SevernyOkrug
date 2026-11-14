// LAN lobby: nickname, create / find / join a game, players in the lobby, the host starts the raid for everyone.
// v11: restyled (cards, player rows with role icons and state, found-game rows with version / slots badges); same flow and logic.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    public partial class UIManager
    {
        InputField _lbNick, _lbIp;
        Text _lbStatus, _lbFoundTitle, _lbPlayersTitle, _lbInfo, _lbHint;
        RectTransform _lbJoinGroup, _lbSessionGroup, _lbHintCard, _lbFoundEmpty, _lbInfoCard;
        Image _lbStatusIcon, _lbStatusBar, _lbSearchIcon;
        Button _lbStart, _lbLeave, _lbCancel, _lbSearchBtn;
        readonly List<Button> _lbFoundBtns = new List<Button>();
        readonly List<Text> _lbPlayerRows = new List<Text>();
        readonly List<Image> _lbPlayerIcons = new List<Image>(), _lbPlayerDots = new List<Image>(), _lbPlayerBgs = new List<Image>();
        readonly List<Text> _lbPlayerStates = new List<Text>();
        readonly List<RectTransform> _lbPlayerTags = new List<RectTransform>();
        readonly List<NetDiscovery.Found> _lbFound = new List<NetDiscovery.Found>();
        readonly HashSet<string> _lbFoundKeys = new HashSet<string>();
        readonly ConcurrentQueue<NetDiscovery.Found> _lbQueue = new ConcurrentQueue<NetDiscovery.Found>();
        List<string> _lbLocalIps = new List<string>();
        volatile bool _lbSearchDone;
        bool _lbSearching, _lbSearchedOnce, _lbSubscribed;
        float _lbNextRefresh;
        const int LobbyFoundRows = 6;

        const string LobbyHint =
            "• Все компьютеры — в одной локальной сети (один роутер или Wi-Fi), у всех одна и та же сборка игры.\n" +
            "• Один игрок нажимает «Создать игру». Остальные — «Найти игры» или вводят IP-адрес хоста.\n" +
            "• Windows при первом запуске спросит про доступ к сети — разрешите (частные сети). Если игра не находится: " +
            "разрешите игру в брандмауэре или откройте TCP 27015 и UDP 27016 на ПК хоста.\n" +
            "• У каждого свой профиль и схрон. Боты и лут — на стороне хоста. Огонь по своим выключен.\n" +
            "• Проверка на одном ПК: запустите две копии игры (вторая получит отдельный профиль) и во второй подключитесь к 127.0.0.1.";

        const float LbL = 64f, LbR = 992f, LbW = 864f;

        // ------------------------------------------------------------------ build
        void BuildLobby()
        {
            var s = NewScreen(UIScreen.Lobby);
            Background(s, 0.86f);
            var f = UIFactory.Center(s);
            ScreenHeader(f, "Главное меню  /  По сети", "Игра по локальной сети");
            var sub = UIFactory.T(f, "Кооператив до " + NetConst.MaxPlayers + " игроков: общий рейд, общие боты и лут, у каждого свой профиль и схрон.",
                                  856, 80, 1000, 40, UITheme.Body, 17, UITheme.Text1, TextAnchor.MiddleRight, "subtitle");

            // nickname (always visible)
            UIFactory.Header(f, "Позывной", LbL, 176, LbW);
            _lbNick = MakeInput(f, LbL, 222, LbW, 56, "", "ваш ник в игре", 20, v => { GameSettings.Nick = (v ?? "").Trim(); PlayerPrefs.Save(); }, "user");

            // ---- left: create / find / join (only when not in a session)
            _lbJoinGroup = UIFactory.Stretch(UIFactory.Node(f, "JoinGroup"));
            UIFactory.Header(_lbJoinGroup, "Создать игру", LbL, 306, LbW);
            var create = UIFactory.Button(_lbJoinGroup, "СОЗДАТЬ ИГРУ", LbL, 352, LbW, 68, LobbyCreate, BtnStyle.Primary, "network", 26);
            UIFactory.ButtonSub(create, "вы станете хостом · порт TCP " + NetConst.TcpPort, 13);

            UIFactory.Header(_lbJoinGroup, "Присоединиться", LbL, 450, LbW);
            _lbSearchBtn = UIFactory.Button(_lbJoinGroup, "НАЙТИ ИГРЫ", LbL, 496, 260, 48, LobbySearch, BtnStyle.Secondary, "search", 18);
            _lbSearchIcon = UIFactory.Icon(_lbJoinGroup, "search", LbL + 280, 510, 20, UITheme.Amber, "searchPulse");
            _lbSearchIcon.gameObject.AddComponent<UIPulse>().graphic = _lbSearchIcon;
            _lbFoundTitle = UIFactory.T(_lbJoinGroup, "", LbL + 280, 496, LbW - 280, 48, UITheme.BodyMedium, 17, UITheme.AmberHi, TextAnchor.MiddleLeft, "foundTitle");
            _lbFoundEmpty = UIFactory.Node(_lbJoinGroup, "FoundEmpty"); UIFactory.Place(_lbFoundEmpty, LbL, 556, LbW, 150);
            {
                var eb = UIFactory.Img(_lbFoundEmpty, "bg", UITheme.A(UITheme.Bg1, 0.7f)); UIFactory.Stretch(eb.rectTransform);
                UIFactory.Pattern(_lbFoundEmpty, "hatch", UIAssets.Hatch, UITheme.A(Color.white, 0.025f));
                UIFactory.Border(_lbFoundEmpty, UITheme.Line0);
                UIFactory.Icon(_lbFoundEmpty, "network", LbW * 0.5f - 14f, 34, 28, UITheme.Text3, "icon");
                UIFactory.T(_lbFoundEmpty, "Список игр пуст", 0, 72, LbW, 26, UITheme.BodySemi, 18, UITheme.Text1, TextAnchor.MiddleCenter, "title");
                UIFactory.T(_lbFoundEmpty, "Нажмите «Найти игры» или введите IP-адрес хоста ниже", 0, 98, LbW, 22, UITheme.Body, 15, UITheme.Text2, TextAnchor.MiddleCenter, "hint");
            }
            for (int i = 0; i < LobbyFoundRows; i++)
            {
                int idx = i;
                var b = UIFactory.Button(_lbJoinGroup, "", LbL, 556 + i * 52, LbW, 46, () => LobbyJoinFound(idx), BtnStyle.Row, "network", 18);
                b.name = "Found_" + i;
                var fx = b.GetComponent<UIButtonFX>();
                if (fx != null && fx.label != null) { fx.label.font = UITheme.BodySemi; fx.label.supportRichText = false; }
                var pl = UIFactory.Mono(b.transform, "", LbW - 330, 0, 70, 46, 15, UITheme.Text0, TextAnchor.MiddleRight, true); pl.name = "players";
                var ad = UIFactory.Mono(b.transform, "", LbW - 250, 0, 236, 46, 14, UITheme.Text2, TextAnchor.MiddleRight); ad.name = "addr";
                var st = UIFactory.T(b.transform, "", 300, 0, 260, 46, UITheme.Body, 15, UITheme.Text2, TextAnchor.MiddleLeft, "state"); st.supportRichText = false;
                var warn = UIFactory.Chip(b.transform, "мест нет", 0, 12, UITheme.Red, "warn", 22f); warn.name = "warn"; warn.gameObject.SetActive(false);
                b.gameObject.SetActive(false);
                _lbFoundBtns.Add(b);
            }
            UIFactory.Header(_lbJoinGroup, "Подключение по IP", LbL, 870, LbW);
            _lbIp = MakeInput(_lbJoinGroup, LbL, 908, 560, 52, "", "например 192.168.1.10", 64, null, "pin");
            UIFactory.Button(_lbJoinGroup, "ПОДКЛЮЧИТЬСЯ", LbL + 576, 908, LbW - 576, 52, () => LobbyJoin(_lbIp.text), BtnStyle.Primary, "chev_r", 19);

            // ---- right: status, players, actions
            UIFactory.Header(f, "Состояние", LbR, 176, LbW);
            var stc = UIFactory.PanelBox(f, "StatusCard", LbR, 222, LbW, 110, 0.9f);
            _lbStatusBar = UIFactory.Box(stc, "bar", 0, 0, 3, 110, UITheme.Text3);
            _lbStatusIcon = UIFactory.Icon(stc, "network", 22, 20, 24, UITheme.Text2, "icon");
            _lbStatus = UIFactory.T(stc, "", 62, 14, LbW - 84, 84, UITheme.Body, 17, UITheme.Text0, TextAnchor.UpperLeft, "status");
            _lbStatus.horizontalOverflow = HorizontalWrapMode.Wrap; _lbStatus.lineSpacing = 1.1f;
            _lbCancel = UIFactory.Button(f, "ОТМЕНА", LbR, 350, 260, 48, () => { NetSession.I?.Leave(); RefreshLobby(); }, BtnStyle.Secondary, "close", 18);

            _lbSessionGroup = UIFactory.Stretch(UIFactory.Node(f, "SessionGroup"));
            UIFactory.Header(_lbSessionGroup, "Игроки", LbR, 350, LbW);
            _lbPlayersTitle = _lbSessionGroup.Find("Header_Игроки").GetComponent<Text>();
            for (int i = 0; i < NetConst.MaxPlayers; i++)
            {
                float y = 396 + i * 56;
                var row = UIFactory.Node(_lbSessionGroup, "PlayerRow"); UIFactory.Place(row, LbR, y, LbW, 50);
                var bg = UIFactory.Img(row, "bg", UITheme.A(UITheme.Bg2, 0.88f)); UIFactory.Stretch(bg.rectTransform);
                UIFactory.Border(row, UITheme.Line0);
                _lbPlayerBgs.Add(bg);
                _lbPlayerIcons.Add(UIFactory.Icon(row, "user", 18, 14, 22, UITheme.Text2, "icon"));
                var nick = UIFactory.T(row, "", 56, 0, 420, 50, UITheme.BodySemi, 20, UITheme.Text0, TextAnchor.MiddleLeft, "nick");
                nick.horizontalOverflow = HorizontalWrapMode.Overflow; nick.supportRichText = false;
                _lbPlayerRows.Add(nick);
                var tag = UIFactory.Chip(row, "хост", 0, 14, UITheme.Amber, "crown", 22f); tag.gameObject.SetActive(false);
                _lbPlayerTags.Add(tag);
                var tag2 = UIFactory.Chip(row, "вы", 0, 14, UITheme.Text1, null, 22f); tag2.name = "you"; tag2.gameObject.SetActive(false);
                _lbPlayerTags.Add(tag2);
                _lbPlayerStates.Add(UIFactory.T(row, "", LbW - 260, 0, 236, 50, UITheme.BodyMedium, 16, UITheme.Text1, TextAnchor.MiddleRight, "state"));
                var dot = UIFactory.Img(row, "dot", UITheme.Green); dot.sprite = UIAssets.Circle; UIFactory.Place(dot.rectTransform, LbW - 18, 21, 8, 8);
                _lbPlayerDots.Add(dot);
            }
            _lbInfoCard = UIFactory.Node(_lbSessionGroup, "InfoCard"); UIFactory.Place(_lbInfoCard, LbR, 626, LbW, 82);
            {
                var ib = UIFactory.Img(_lbInfoCard, "bg", UITheme.A(UITheme.Mul(UITheme.Amber, 0.12f), 0.9f)); UIFactory.Stretch(ib.rectTransform);
                UIFactory.Border(_lbInfoCard, UITheme.A(UITheme.Amber, 0.3f));
                UIFactory.Icon(_lbInfoCard, "info", 18, 31, 20, UITheme.Amber, "icon");
            }
            _lbInfo = UIFactory.T(_lbInfoCard, "", 52, 6, LbW - 70, 70, UITheme.Body, 17, UITheme.AmberHi, TextAnchor.MiddleLeft, "info");
            _lbInfo.lineSpacing = 1.1f;
            _lbStart = UIFactory.Button(_lbSessionGroup, "ПАРАМЕТРЫ И СТАРТ РЕЙДА", LbR, 724, LbW, 76, LobbyOpenRaidSetup, BtnStyle.Primary, "crosshair", 28);
            UIFactory.ButtonSub(_lbStart, "карта и условия — рейд начнётся у всех игроков в лобби", 13);
            float hw = (LbW - 12f) * 0.5f;
            UIFactory.Button(_lbSessionGroup, "СНАРЯЖЕНИЕ", LbR, 816, hw, 52, () => OpenStash(false), BtnStyle.Secondary, "backpack", 19);
            UIFactory.Button(_lbSessionGroup, "СКУПЩИК", LbR + hw + 12f, 816, hw, 52, () => OpenStash(true), BtnStyle.Secondary, "rouble", 19);
            UIFactory.Button(_lbSessionGroup, "НАСТРОЙКИ", LbR, 880, hw, 52, () => OpenSettings(UIScreen.MainMenu), BtnStyle.Secondary, "gear", 19);
            _lbLeave = UIFactory.Button(_lbSessionGroup, "ПОКИНУТЬ ЛОББИ", LbR + hw + 12f, 880, hw, 52, LobbyLeaveAsk, BtnStyle.Danger, "exit", 19);

            // help card (moves to the left column while in a session)
            _lbHintCard = UIFactory.PanelBox(f, "HelpCard", LbR, 350, LbW, 400, 0.86f);
            UIFactory.Icon(_lbHintCard, "info", 20, 20, 18, UITheme.Text2, "icon");
            UIFactory.Caps(_lbHintCard, "Как играть по сети", 48, 14, LbW - 60, 30, 15, UITheme.Text1, TextAnchor.MiddleLeft, 2.2f);
            UIFactory.Box(_lbHintCard, "line", 20, 52, LbW - 40, 1, UITheme.Line0);
            _lbHint = UIFactory.T(_lbHintCard, LobbyHint, 20, 66, LbW - 44, 320, UITheme.Body, 16, UITheme.Text1, TextAnchor.UpperLeft, "hint");
            _lbHint.lineSpacing = 1.18f;

            BackButton(f, LobbyBack);
        }

        /// <summary>Code-built single-line InputField (uGUI): dark field, hairline edge (amber while typing), optional glyph.</summary>
        static InputField MakeInput(Transform parent, float x, float y, float w, float h, string value, string placeholder, int maxLen, Action<string> onEnd, string glyph = null)
        {
            var box = UIFactory.Frame(parent, "Input", x, y, w, h, UITheme.A(UITheme.Bg0, 0.92f), UITheme.Line1);
            box.raycastTarget = true;
            var fill = box.transform.Find("fill").GetComponent<Image>(); fill.raycastTarget = true;
            float pad = 16f;
            Image icon = null;
            if (!string.IsNullOrEmpty(glyph)) { icon = UIFactory.Icon(box.transform, glyph, 16, (h - 18f) * 0.5f, 18, UITheme.Text2, "icon"); pad = 46f; }
            var text = UIFactory.Label(box.transform, "", 20, TextAnchor.MiddleLeft, UITheme.Text0, false, "Text");
            text.font = UITheme.BodyMedium;
            UIFactory.Stretch(text.rectTransform, pad, 4, 14, 4);
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = UIFactory.Label(box.transform, placeholder, 18, TextAnchor.MiddleLeft, UITheme.Text3, false, "Placeholder");
            UIFactory.Stretch(ph.rectTransform, pad, 4, 14, 4);
            ph.supportRichText = false;
            box.gameObject.SetActive(false);                                        // OnEnable must see the text component (caret, label)
            var f = box.gameObject.AddComponent<InputField>();
            f.textComponent = text; f.placeholder = ph; f.targetGraphic = fill;
            f.characterLimit = maxLen; f.lineType = InputField.LineType.SingleLine;
            f.caretColor = UITheme.AmberHi; f.customCaretColor = true; f.caretWidth = 2;
            f.selectionColor = UITheme.A(UITheme.Amber, 0.35f);
            f.transition = Selectable.Transition.None;
            var nav = f.navigation; nav.mode = Navigation.Mode.None; f.navigation = nav;
            f.text = value ?? "";
            if (onEnd != null) f.onEndEdit.AddListener(v => onEnd(v));
            var fx = box.gameObject.AddComponent<UIInputFX>();
            fx.field = f; fx.edge = box; fx.fill = fill; fx.icon = icon;
            box.gameObject.SetActive(true);
            return f;
        }

        // ------------------------------------------------------------------ open / refresh
        void OpenLobby()
        {
            NetSession.Ensure();
            if (!_lbSubscribed) { NetSession.Changed += OnNetChanged; _lbSubscribed = true; }
            if (!NetSession.Active || string.IsNullOrEmpty(_lbNick.text)) _lbNick.text = NetSession.DefaultNick();
            if (string.IsNullOrEmpty(_lbIp.text)) _lbIp.text = GameSettings.LastJoinIp;
            Show(UIScreen.Lobby);
            RefreshLobby();
            if (!NetSession.Active && !_lbSearching && !_lbSearchedOnce) LobbySearch();   // first visit: look for games right away
        }

        void OnNetChanged()
        {
            if (Current == UIScreen.Lobby) RefreshLobby();
        }

        void TickLobby()
        {
            bool changed = false;
            while (_lbQueue.TryDequeue(out var f)) changed |= AddFound(f);
            if (_lbSearching && _lbSearchDone) { _lbSearching = false; changed = true; }
            if (changed || Time.unscaledTime >= _lbNextRefresh) RefreshLobby();
            if (GameInput.Down(GKey.Pause) && !AnyInputFocused()) LobbyBack();
        }

        bool AnyInputFocused() => (_lbNick != null && _lbNick.isFocused) || (_lbIp != null && _lbIp.isFocused);

        bool AddFound(NetDiscovery.Found f)
        {
            bool local = f.address == "127.0.0.1" || _lbLocalIps.Contains(f.address);
            string key = local ? "local:" + f.name + ":" + f.port : f.address + ":" + f.port;
            if (!_lbFoundKeys.Add(key)) return false;
            if (local) f.address = "127.0.0.1";
            f.status = (f.status ?? "") + (local ? " · этот ПК" : "");
            _lbFound.Add(f);
            return true;
        }

        void RefreshLobby()
        {
            if (_lbStatus == null) return;
            _lbNextRefresh = Time.unscaledTime + 0.5f;
            var ns = NetSession.I;
            bool active = NetSession.Active, host = NetSession.IsHost, connecting = ns != null && ns.Connecting;
            bool busy = GameBootstrap.I != null && (GameBootstrap.I.Busy || GameBootstrap.I.InRaid);
            _lbJoinGroup.gameObject.SetActive(!active);
            _lbSessionGroup.gameObject.SetActive(active && !connecting);
            _lbCancel.gameObject.SetActive(connecting);
            _lbNick.interactable = !active;
            // help card: right column below the status when idle, left column (instead of create / join) in a session
            float hintH = Mathf.Clamp(Mathf.Ceil(66f + _lbHint.preferredHeight + 24f), 160f, 400f);   // card hugs its text
            if (active) UIFactory.Place(_lbHintCard, LbL, 306, LbW, hintH);
            else UIFactory.Place(_lbHintCard, LbR, connecting ? 414 : 350, LbW, hintH);

            string st = ns != null ? ns.Status : "";
            if (string.IsNullOrEmpty(st) || !active && st == "Вы покинули лобби") st = "Вы не в лобби. Создайте игру или присоединитесь к игре друга.";
            if (SaveSystem.Slot > 1) st += "\n" + UITheme.Tint("Это копия игры №" + SaveSystem.Slot + " — у неё отдельный профиль.", UITheme.AmberHi);
            _lbStatus.text = st;
            Color sc = connecting ? UITheme.Amber : active ? UITheme.Green : UITheme.Text2;
            _lbStatusIcon.color = sc; _lbStatusBar.color = connecting || active ? sc : UITheme.Line1;

            // found games
            _lbSearchBtn.interactable = !_lbSearching;
            _lbSearchIcon.gameObject.SetActive(_lbSearching);
            UIFactory.Place(_lbFoundTitle.rectTransform, LbL + (_lbSearching ? 312 : 280), 496, LbW - 312, 48);
            _lbFoundTitle.text = _lbSearching ? "Поиск игр в сети…" : _lbFound.Count > 0 ? "Найдено игр: " + _lbFound.Count : _lbSearchedOnce ? "Игры не найдены — введите IP вручную" : "";
            _lbFoundTitle.color = !_lbSearching && _lbFound.Count == 0 && _lbSearchedOnce ? UITheme.Text1 : UITheme.AmberHi;
            _lbFoundEmpty.gameObject.SetActive(_lbFound.Count == 0);
            for (int i = 0; i < _lbFoundBtns.Count; i++)
            {
                var b = _lbFoundBtns[i];
                bool on = i < _lbFound.Count;
                b.gameObject.SetActive(on);
                if (!on) continue;
                var f = _lbFound[i];
                bool sameVer = f.protocol == NetConst.Protocol, full = f.max > 0 && f.players >= f.max;
                UIFactory.SetButtonText(b, Esc(f.name));
                var fx = b.GetComponent<UIButtonFX>();
                float nameW = fx != null && fx.label != null ? UIFactory.TextWidth(fx.label) : 200f;
                var stt = b.transform.Find("state").GetComponent<Text>();
                stt.text = Esc(f.status);
                UIFactory.Place(stt.rectTransform, Mathf.Min(48f + nameW + 18f, 360f), 0, 230, 46);
                b.transform.Find("players").GetComponent<Text>().text = f.players + "/" + f.max;
                b.transform.Find("addr").GetComponent<Text>().text = f.address;
                var warn = (RectTransform)b.transform.Find("warn");
                bool bad = !sameVer || full;
                warn.gameObject.SetActive(bad);
                if (bad) { float ww = UIFactory.SetChip(warn, !sameVer ? "другая версия игры" : "мест нет", UITheme.Red, "warn"); warn.anchoredPosition = new Vector2(LbW - 340f - ww, -12f); }
                b.interactable = sameVer && !full;
            }

            // players + actions
            if (active && !connecting && ns != null)
            {
                int n = 0;
                foreach (var p in ns.Peers)
                {
                    if (!p.welcomed || n >= _lbPlayerRows.Count) continue;
                    string state = p.state < NetSession.StateNames.Length ? NetSession.StateNames[p.state] : "?";
                    bool isHost = p.pid == 0, me = p.pid == ns.MyId;
                    SetPlayerRow(n++, Esc(p.nick), isHost, me, state, p.state == 0 ? UITheme.Green : p.state == 1 ? UITheme.Amber : p.state == 2 ? UITheme.Orange : UITheme.Steel);
                }
                for (int i = n; i < _lbPlayerRows.Count; i++) SetPlayerRow(i, null, false, false, null, UITheme.Text3);
                _lbPlayersTitle.text = ("Игроки: " + n + " / " + NetConst.MaxPlayers).ToUpperInvariant();

                var L = SaveSystem.Current.loadout;
                bool noWeapon = L.Get(EquipSlot.Primary) == null && L.Get(EquipSlot.Holster) == null;
                string info;
                if (host) info = busy ? "Идёт рейд." : n <= 1 ? "Ждём друзей… Можно начать и одному — подключившиеся попадут в следующий рейд." :
                                 "Все в сборе? Выберите карту и параметры — рейд начнётся у всех игроков в лобби.";
                else info = ns.HostInRaid ? "Хост сейчас в рейде" + (string.IsNullOrEmpty(ns.LobbyMap) ? "" : " («" + ns.LobbyMap + "»)") + " — вы попадёте в следующий рейд." :
                            "Рейд запускает хост. Пока ждёте — проверьте снаряжение.";
                if (noWeapon) info += "\n" + UITheme.Tint("У вас нет оружия! Возьмите его в «Снаряжении» или купите у скупщика.", UITheme.Red);
                _lbInfo.text = info;
                _lbStart.gameObject.SetActive(host);
                _lbStart.interactable = host && !busy;
                UIFactory.SetButtonText(_lbLeave, host ? "ЗАКРЫТЬ ЛОББИ" : "ПОКИНУТЬ ЛОББИ");
            }
        }

        void SetPlayerRow(int i, string nick, bool isHost, bool me, string state, Color stateColor)
        {
            bool used = nick != null;
            var t = _lbPlayerRows[i];
            t.text = used ? nick : "свободно";
            t.font = used ? UITheme.BodySemi : UITheme.Body;
            t.color = used ? UITheme.Text0 : UITheme.Text3;
            _lbPlayerBgs[i].color = used ? UITheme.A(UITheme.Bg2, 0.9f) : UITheme.A(UITheme.Bg1, 0.55f);
            var ic = _lbPlayerIcons[i];
            UIFactory.SetIcon(ic, !used ? "dot" : isHost ? "crown" : "user");
            ic.color = !used ? UITheme.Text3 : isHost ? UITheme.Amber : me ? UITheme.Text0 : UITheme.Text1;
            float x = 56f + (used ? UIFactory.TextWidth(t) : 0f) + 14f;
            var hostTag = _lbPlayerTags[i * 2]; var youTag = _lbPlayerTags[i * 2 + 1];
            hostTag.gameObject.SetActive(used && isHost);
            if (used && isHost) { hostTag.anchoredPosition = new Vector2(x, -14f); x += hostTag.sizeDelta.x + 8f; }
            youTag.gameObject.SetActive(used && me);
            if (used && me) youTag.anchoredPosition = new Vector2(x, -14f);
            _lbPlayerStates[i].text = used ? state : "";
            _lbPlayerStates[i].color = UITheme.Mul(stateColor, 1.05f);
            _lbPlayerDots[i].enabled = used;
            _lbPlayerDots[i].color = stateColor;
        }

        static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");

        // ------------------------------------------------------------------ actions
        string LobbyNick()
        {
            var n = (_lbNick.text ?? "").Trim();
            if (n.Length == 0) n = NetSession.DefaultNick();
            GameSettings.Nick = n; PlayerPrefs.Save();
            return n;
        }

        void LobbyCreate()
        {
            var ns = NetSession.Ensure();
            if (!ns.StartHost(LobbyNick(), out var err)) { ShowMessage("Не удалось создать игру", err); RefreshLobby(); return; }
            RefreshLobby();
        }

        void LobbySearch()
        {
            if (_lbSearching) return;
            _lbSearching = true; _lbSearchDone = false; _lbSearchedOnce = true;
            _lbFound.Clear(); _lbFoundKeys.Clear();
            while (_lbQueue.TryDequeue(out _)) { }
            _lbLocalIps = NetDiscovery.LocalAddresses();
            NetDiscovery.Search(NetConst.UdpPort, 1800, _lbQueue, () => _lbSearchDone = true);
            RefreshLobby();
        }

        void LobbyJoinFound(int i)
        {
            if (i < 0 || i >= _lbFound.Count) return;
            var f = _lbFound[i];
            string addr = f.address + (f.port > 0 && f.port != NetConst.TcpPort ? ":" + f.port : "");
            _lbIp.text = addr;
            LobbyJoin(addr);
        }

        void LobbyJoin(string addr)
        {
            addr = (addr ?? "").Trim();
            if (addr.Length == 0 || addr.EndsWith(".")) { ShowMessage("Адрес хоста", "Введите IP-адрес компьютера хоста — он показан у хоста на экране «Игра по сети» (например 192.168.1.10)."); return; }
            GameSettings.LastJoinIp = addr; PlayerPrefs.Save();
            NetSession.Ensure().Join(addr, LobbyNick());
            RefreshLobby();
        }

        void LobbyOpenRaidSetup()
        {
            if (!NetSession.IsHost) return;
            RefreshRaidSetup(); Show(UIScreen.RaidSetup);
        }

        void LobbyLeaveAsk()
        {
            if (!NetSession.Active) { RefreshLobby(); return; }
            int others = 0; foreach (var p in NetSession.I.Peers) if (p.welcomed && p.pid != NetSession.I.MyId) others++;
            if (NetSession.IsHost && others > 0) ShowConfirm("Закрыть лобби", "Все игроки (" + others + ") будут отключены. Закрыть лобби?", () => { NetSession.I.Leave(); RefreshLobby(); });
            else { NetSession.I.Leave(); RefreshLobby(); }
        }

        void LobbyBack()
        {
            if (!NetSession.Active) { ShowMainMenu(); return; }
            ShowConfirm(NetSession.IsHost ? "Закрыть лобби" : "Покинуть лобби",
                        NetSession.IsHost ? "Выйти в главное меню? Лобби будет закрыто, все игроки отключатся." : "Выйти в главное меню? Вы покинете лобби.",
                        () => { NetSession.I.Leave(); ShowMainMenu(); });
        }
    }
}
