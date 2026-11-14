// Menus: main, raid setup, loading, settings, character (stats + controls), pause, results, modal dialogs.
// v11: unified design system (UITheme / UIFactory), Volna key art in the main menu, map cards, stat tiles.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    public partial class UIManager
    {
        // main menu
        Text _mmNick, _mmXp, _mmMoney, _mmSlot;
        Text[] _mmStats = new Text[3];
        Image _mmXpBar; RectTransform _mmLevel, _mmCard;
        // raid setup
        Text _setupLoadout, _setupWarn, _setupInfo, _setupMulBots, _setupMulLoot, _setupSub, _startRaidSub;
        RectTransform _setupWarnBox;
        int _setupMap = -1;
        readonly List<Button> _mapCards = new List<Button>();
        readonly List<GameObject> _mapSelChips = new List<GameObject>();
        Button[] _segBots, _segTod, _segWeather, _segSeason;
        Button _startRaidBtn, _benchBtn;
        class SlotTile { public Image icon, glyph; public Text name; }
        readonly SlotTile[] _setupTiles = new SlotTile[4];
        static readonly EquipSlot[] TileSlots = { EquipSlot.Primary, EquipSlot.Holster, EquipSlot.Armor, EquipSlot.Helmet };
        // loading
        Text _loadingTitle, _loadingStatus, _loadingTip, _loadingPct;
        Image _loadingBar; RawImage _loadingImg; RectTransform _loadChipTime, _loadChipBots;
        float _loadingTarget, _loadingShown;
        // character (former "base")
        Text _chNick, _chXp, _chMoney, _chFile, _chSlot;
        Image _chXpBar; RectTransform _chLevel, _chOutcome; Text[] _chLegend; Image[] _chLegendDot;
        readonly Text[] _chVals = new Text[8], _chSubs = new Text[8];
        // pause
        Text _pauseSub, _pauseNote;
        // results
        Text _resultTitle, _resultBody, _resultSub, _resultLevel, _resultMoney;
        Image _resultBand, _resultLine;
        readonly Text[] _resultCaps = new Text[5], _resultVals = new Text[5], _resultSubs = new Text[5];

        static readonly string[] Tips =
        {
            "Выходы с карты указаны в списке (O). Не все выходы открыты в каждом рейде.",
            "Кровотечение убивает медленно, но верно: бинт (H или ПКМ в инвентаре) останавливает его.",
            "Звук выстрела слышен далеко. Боты идут на шум.",
            "Подсумок не теряется при гибели. Храните там самое ценное.",
            "Магазины снаряжаются в инвентаре: перетащите патроны на магазин.",
            "Ctrl+клик быстро перекладывает предметы между схроном/контейнером и снаряжением.",
            "Присед (C) снижает шум шагов и отдачу.",
            "При переломе ноги нельзя бегать — помогут обезболивающие.",
        };

        // ------------------------------------------------------------------ shared building blocks
        /// <summary>Full-screen key art (cover), optional darkening, extra overlays and a vignette. Returns the art image.
        /// v11.1: no film grain (in Linear colour space it showed up as grey specks over the dark areas).</summary>
        RawImage Background(RectTransform parent, float darken, Texture tex = null, bool drift = false, Action<RectTransform> overlays = null)
        {
            var holder = UIFactory.Stretch(UIFactory.Node(parent, "Backdrop"));
            var block = UIFactory.Img(holder, "base", UITheme.Bg0, true); UIFactory.Stretch(block.rectTransform);   // also blocks clicks
            var bg = tex != null ? tex : GameAssets.MenuBackground;
            RawImage ri = null;
            if (bg != null)
            {
                ri = UIFactory.Cover(holder, bg, "bg", 0.6f, 0.5f);
                if (drift) { var kb = ri.gameObject.AddComponent<UIKenBurns>(); kb.period = 60f; kb.zoom = 0.05f; kb.pan = new Vector2(-14f, 5f); }
            }
            if (darken > 0f) { var dim = UIFactory.Img(holder, "dim", UITheme.A(UITheme.Bg0, darken)); UIFactory.Stretch(dim.rectTransform); }
            overlays?.Invoke(holder);
            var vig = UIFactory.Img(holder, "vignette", UITheme.A(Color.black, 0.6f)); UIFactory.SetSoft(vig, "vign", vig.color); UIFactory.Stretch(vig.rectTransform);
            return ri;
        }

        void Logo(RectTransform parent, float x, float y, float w)
        {
            var logo = GameAssets.Logo;
            if (logo != null)
            {
                var ri = UIFactory.Node(parent, "logo").gameObject.AddComponent<RawImage>();
                ri.texture = logo; ri.raycastTarget = false;
                UIFactory.Place(ri.rectTransform, x, y, w, w * logo.height / Mathf.Max(1f, logo.width));
            }
            else
            {
                var t = UIFactory.T(parent, "СЕВЕРНЫЙ ОКРУГ", x, y, w, 100, UITheme.DisplayBold, 76, UITheme.Text0, TextAnchor.MiddleLeft, "logoText");
                UIFactory.Track(t, 3f);
                UIFactory.Caps(parent, "заброшенный город", x + 4, y + 98, w, 30, 20, UITheme.Amber, TextAnchor.MiddleLeft, 6f);
            }
        }

        /// <summary>Breadcrumb + large title + hairline (top of the 1920x1080 frame).</summary>
        static void ScreenHeader(RectTransform f, string crumb, string title)
        {
            UIFactory.Box(f, "titleBar", 64, 76, 4, 40, UITheme.Amber);
            UIFactory.Caps(f, crumb, 84, 44, 1200, 22, 14, UITheme.Text2, TextAnchor.MiddleLeft, 2.4f);
            var t = UIFactory.T(f, title.ToUpperInvariant(), 82, 66, 1400, 60, UITheme.Display, 46, UITheme.Text0, TextAnchor.MiddleLeft, "Title");
            UIFactory.Track(t, 2f);
            UIFactory.Box(f, "hairTop", 64, 148, 1792, 1, UITheme.Line0);
        }

        /// <summary>Bottom bar: hairline, НАЗАД (left) and the Esc hint.</summary>
        static Button BackButton(RectTransform f, Action back, string text = "НАЗАД")
        {
            UIFactory.Box(f, "hairBottom", 64, 968, 1792, 1, UITheme.Line0);
            var b = UIFactory.Button(f, text, 64, 992, 220, 56, back, BtnStyle.Secondary, "chev_l", 19);
            UIFactory.KeyHint(f, "ESC", "назад", 302, 1007, 26, 15, UITheme.Text2);
            return b;
        }

        /// <summary>Stat tile: caption, big mono value, small sub line.</summary>
        static Text StatTile(RectTransform parent, string caption, float x, float y, float w, float h, out Text sub, int valueSize = 34)
        {
            var t = UIFactory.PanelBox(parent, "Stat_" + caption, x, y, w, h, 0.9f);
            UIFactory.Caps(t, caption, 18, 12, w - 36, 20, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            var v = UIFactory.Mono(t, "", 18, 34, w - 36, valueSize + 12, valueSize, UITheme.Text0, TextAnchor.MiddleLeft);
            sub = UIFactory.T(t, "", 18, h - 30, w - 36, 20, UITheme.Body, 14, UITheme.Text2, TextAnchor.MiddleLeft, "sub");
            return v;
        }

        // ------------------------------------------------------------------ main menu
        void BuildMainMenu()
        {
            var s = NewScreen(UIScreen.MainMenu);
            Background(s, 0f, null, true, h =>
            {
                UIFactory.Gradient(h, "fadeL", 2, 1250, UITheme.A(UITheme.Bg0, 0.96f));
                UIFactory.Gradient(h, "fadeB", 1, 380, UITheme.A(UITheme.Bg0, 0.92f));
                UIFactory.Gradient(h, "fadeT", 0, 220, UITheme.A(UITheme.Bg0, 0.7f));
            });

            // ---- left column: logo + menu (pinned to the left edge, vertically centred)
            var col = UIFactory.Node(s, "MenuColumn"); UIFactory.PlaceAt(col, 0f, 0.5f, 0, 0, 760, 1080);
            Logo(col, 88, 84, 540);
            UIFactory.Caps(col, "Экстракшн-шутер · прототип v12", 92, 222, 540, 20, 13, UITheme.Text2, TextAnchor.MiddleLeft, 2.6f);

            var raid = UIFactory.Button(col, "В РЕЙД", 88, 292, 452, 84, () => { RefreshRaidSetup(); Show(UIScreen.RaidSetup); }, BtnStyle.Primary, "crosshair", 32);
            UIFactory.ButtonSub(raid, "ВЫБОР ЛОКАЦИИ И УСЛОВИЙ", 13);

            string[] names = { "ПО СЕТИ (LAN)", "ОБУЧЕНИЕ", "СНАРЯЖЕНИЕ", "СКУПЩИК", "ПЕРСОНАЖ", "НАСТРОЙКИ", "ВЫХОД" };
            Action[] acts =
            {
                OpenLobby, AskTutorial, () => OpenStash(false), () => OpenStash(true),
                () => { RefreshBase(); Show(UIScreen.Base); }, () => OpenSettings(UIScreen.MainMenu),
                () => ShowConfirm("Выход", "Выйти из игры?", Quit),
            };
            float y = 404;
            for (int i = 0; i < names.Length; i++)
            {
                if (i == names.Length - 1) { UIFactory.Box(col, "sep", 88, y + 8, 452, 1, UITheme.Line0); y += 18; }
                var b = UIFactory.Button(col, names[i], 88, y, 452, 56, acts[i], BtnStyle.Menu, null, 27);
                var fx = b.GetComponent<UIButtonFX>(); if (fx != null && fx.sub != null) fx.sub.text = (i + 1).ToString("00");
                y += 58;
            }

            float hx = 92f + UIFactory.KeyHint(col, "F8", "бенчмарк", 92, 1006, 26, 15, UITheme.Text2) + 22f;
            hx += UIFactory.KeyHint(col, "F7", "фототочки", hx, 1006, 26, 15, UITheme.Text2) + 22f;   // v13: screenshots of «ТЦ «Волна»»
            UIFactory.Mono(col, "UNITY 6 · URP", hx, 1006, 200, 26, 12, UITheme.Text3);

            // ---- operator card (top right)
            _mmCard = UIFactory.Node(s, "Operator"); UIFactory.PlaceAt(_mmCard, 1f, 0f, 1452, 48, 420, 226);
            UIFactory.PanelBox(_mmCard, "panel", 0, 0, 420, 226, 0.86f);
            UIFactory.Box(_mmCard, "accent", 0, 0, 420, 2, UITheme.Amber);
            UIFactory.Caps(_mmCard, "Оператор", 20, 14, 200, 20, 13, UITheme.Text2, TextAnchor.MiddleLeft, 2.4f);
            _mmLevel = UIFactory.Chip(_mmCard, "Ур. 1", 340, 12, UITheme.Amber, "star", 24);
            _mmNick = UIFactory.T(_mmCard, "", 20, 36, 380, 48, UITheme.Display, 34, UITheme.Text0, TextAnchor.MiddleLeft, "nick");
            _mmXpBar = UIFactory.Bar(_mmCard, 20, 92, 380, 4, UITheme.Amber);
            _mmXp = UIFactory.Mono(_mmCard, "", 20, 100, 380, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft);
            UIFactory.Divider(_mmCard, 20, 128, 380);
            _mmMoney = UIFactory.Mono(_mmCard, "", 20, 136, 380, 36, 26, UITheme.AmberHi, TextAnchor.MiddleLeft, true);
            string[] cap = { "Рейдов", "Выживаемость", "Убийств" };
            for (int i = 0; i < 3; i++)
            {
                UIFactory.Caps(_mmCard, cap[i], 20 + i * 130, 178, 126, 16, 11, UITheme.Text2, TextAnchor.MiddleLeft, 1.4f);
                _mmStats[i] = UIFactory.Mono(_mmCard, "", 20 + i * 130, 194, 126, 24, 18, UITheme.Text0, TextAnchor.MiddleLeft);
            }
            _mmSlot = UIFactory.T(s, "", 0, 0, 420, 40, UITheme.BodyMedium, 15, UITheme.AmberHi, TextAnchor.UpperRight, "slot");
            UIFactory.PlaceAt(_mmSlot.rectTransform, 1f, 0f, 1452, 284, 420, 40);

            // ---- location tag (bottom right)
            var tag = UIFactory.Node(s, "Location"); UIFactory.PlaceAt(tag, 1f, 1f, 1452, 984, 420, 52);
            UIFactory.Icon(tag, "pin", 396, 4, 18, UITheme.Amber);
            UIFactory.Caps(tag, "Локация на фоне", 0, 0, 388, 24, 12, UITheme.Text2, TextAnchor.MiddleRight, 2.2f);
            UIFactory.T(tag, "ТЦ «Волна» · Северный округ", 0, 22, 420, 30, UITheme.BodyMedium, 18, UITheme.Text1, TextAnchor.MiddleRight, "place");
        }

        void RefreshMainMenu()
        {
            var p = SaveSystem.Current;
            int lvl = p.Level, lo = (lvl - 1) * (lvl - 1) * 1000, hi = lvl * lvl * 1000;
            _mmNick.text = p.nickname;
            float lw = UIFactory.SetChip(_mmLevel, "Ур. " + lvl);
            _mmLevel.anchoredPosition = new Vector2(400 - lw, _mmLevel.anchoredPosition.y);
            UIFactory.SetBar(_mmXpBar, (p.xp - lo) / (float)Mathf.Max(1, hi - lo));
            _mmXp.text = "ОПЫТ  " + UITheme.Num(p.xp) + " / " + UITheme.Num(hi);
            _mmMoney.text = UITheme.Rub(p.roubles);
            _mmStats[0].text = p.raids.ToString();
            _mmStats[1].text = p.raids > 0 ? Mathf.RoundToInt(100f * p.survived / p.raids) + "%" : "—";
            _mmStats[2].text = p.kills.ToString();
            _mmSlot.text = SaveSystem.Slot > 1 ? "Копия игры №" + SaveSystem.Slot + " — отдельный профиль" : "";
        }

        void AskTutorial()
        {
            var p = SaveSystem.Current;
            ShowConfirm("Обучение", "Учебный полигон: вас высадят без снаряжения. Вы научитесь двигаться, обыскивать ящики, " +
                        "снаряжать магазины, стрелять, лечиться и выходить с карты.\n\n" +
                        (p.tutorialDone ? "Обучение уже пройдено — награды не будет, но потренироваться можно." :
                                          "Награда за первое прохождение: " + UIFactory.Money(RaidManager.TutorialReward) + " и набор снаряжения в схрон.") +
                        "\n\nВ обучении ничего не теряется. Начать?", () => GameBootstrap.I.StartTutorial());
        }

        /// <summary>First launch (or first launch of v08 with an old profile): offer the tutorial once.</summary>
        void MaybeOfferTutorial()
        {
            var p = SaveSystem.Current;
            if (p == null || p.tutorialDone || p.tutorialOffered || InRaid) return;
            p.tutorialOffered = true;
            SaveSystem.Save();
            ShowConfirm("Добро пожаловать в Северный округ", (p.raids == 0 ? "Похоже, вы здесь впервые. " : "Новое в этой версии — обучение. ") +
                        "Пройти короткое обучение? Вас высадят на учебный полигон без снаряжения — всё нужное найдёте по пути.\n\n" +
                        "Награда: " + UIFactory.Money(RaidManager.TutorialReward) + " и набор снаряжения в схрон.\n" +
                        "Позже обучение можно запустить из главного меню.", () => GameBootstrap.I.StartTutorial());
        }

        static void Quit()
        {
            SaveSystem.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ raid setup
        void BuildRaidSetup()
        {
            var s = NewScreen(UIScreen.RaidSetup);
            Background(s, 0.86f);
            var f = UIFactory.Center(s);
            ScreenHeader(f, "Главное меню  /  Рейд", "Подготовка к рейду");
            _setupSub = UIFactory.T(f, "", 1056, 84, 800, 30, UITheme.BodyMedium, 17, UITheme.AmberHi, TextAnchor.MiddleRight, "lan");

            // ---- locations (cards with key art)
            UIFactory.Header(f, "Локация", 64, 176, 1132);
            int n = MapCatalog.All.Length;
            float cw = Mathf.Min(560f, (1132f - 12f * (n - 1)) / Mathf.Max(1, n)), ch = 456f;
            for (int i = 0; i < n; i++)
            {
                var m = MapCatalog.All[i]; int idx = i;
                var b = UIFactory.Button(f, "", 64 + i * (cw + 12), 222, cw, ch, () => { _setupMap = idx; RefreshRaidSetup(); }, BtnStyle.Card);
                b.name = "MapCard_" + m.id;
                var fx = b.GetComponent<UIButtonFX>();
                var art = UIFactory.Node(b.transform, "art"); UIFactory.Place(art, 1, 1, cw - 2, 300);
                art.gameObject.AddComponent<RectMask2D>();
                Texture tex = UIAssets.MapImage(m.id);
                if (tex == null) tex = GameAssets.MenuBackground;
                if (tex != null) fx.extra = UIFactory.Cover(art, tex, "img");
                UIFactory.Gradient(art, "shade", 1, 170, UITheme.A(UITheme.Bg0, 0.94f));
                var title = UIFactory.T(b.transform, m.title.ToUpperInvariant(), 22, 214, cw - 44, 48, UITheme.Display, 36, UITheme.Text0, TextAnchor.MiddleLeft, "title");
                UIFactory.Track(title, 1.5f);
                UIFactory.Icon(b.transform, "clock", 22, 268, 16, UITheme.Text1);
                UIFactory.Caps(b.transform, m.minutes + " мин", 44, 264, 120, 24, 14, UITheme.Text1, TextAnchor.MiddleLeft, 1.4f);
                UIFactory.Icon(b.transform, "users", 128, 268, 16, UITheme.Text1);
                UIFactory.Caps(b.transform, "Противники " + m.bots[0] + "–" + m.bots[2], 150, 264, 260, 24, 14, UITheme.Text1, TextAnchor.MiddleLeft, 1.4f);
                bool ok = m.Playable && m.Placed;
                UIFactory.Chip(b.transform, !m.Playable ? "Сцена не найдена" : !m.Placed ? "Город не размещён" : "Доступна", 16, 16,
                               !m.Playable ? UITheme.Red : !m.Placed ? UITheme.Orange : UITheme.Green, !ok ? "warn" : "check", 24);
                var sel = UIFactory.Chip(b.transform, "Выбрано", cw - 120, 16, UITheme.Amber, "check", 24, true);
                sel.anchoredPosition = new Vector2(cw - 16 - sel.sizeDelta.x, -16);
                _mapSelChips.Add(sel.gameObject);
                var desc = UIFactory.T(b.transform, m.desc, 22, 316, cw - 44, 92, UITheme.Body, 15, UITheme.Text1, TextAnchor.UpperLeft, "desc");
                desc.verticalOverflow = VerticalWrapMode.Truncate;
                UIFactory.T(b.transform, !m.Playable ? "Нужна сцена: меню Severny > 2 и 3" : !m.Placed ? "Разместите город: меню Severny > 3 и 4" : "",
                            22, ch - 38, cw - 44, 24, UITheme.BodySemi, 14, !m.Playable ? UITheme.Red : UITheme.Orange, TextAnchor.MiddleLeft, "state");
                if (fx.edge != null) fx.edge.transform.SetAsLastSibling();
                if (fx.accent != null) fx.accent.transform.SetAsLastSibling();
                _mapCards.Add(b);
            }

            // ---- loadout summary
            UIFactory.Header(f, "Снаряжение", 64, 700, 1132);
            string[] cap = { "Основное оружие", "Пистолет", "Бронежилет", "Шлем" };
            string[] glyph = { "rifle", "pistol", "vest", "helmet" };
            float tw = (1132f - 36f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var t = UIFactory.PanelBox(f, "Tile_" + TileSlots[i], 64 + i * (tw + 12), 744, tw, 124, 0.9f);
                UIFactory.Caps(t, cap[i], 14, 10, tw - 28, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
                var st = new SlotTile();
                st.glyph = UIFactory.Icon(t, glyph[i], tw * 0.5f - 26, 34, 52, UITheme.A(UITheme.Text3, 0.55f), "empty");
                st.icon = UIFactory.Img(t, "icon", Color.white); st.icon.preserveAspect = true; UIFactory.Place(st.icon.rectTransform, 14, 32, tw - 28, 60);
                st.name = UIFactory.T(t, "", 14, 94, tw - 28, 24, UITheme.BodySemi, 16, UITheme.Text0, TextAnchor.MiddleLeft, "name");
                _setupTiles[i] = st;
            }
            _setupLoadout = UIFactory.T(f, "", 64, 880, 1132, 26, UITheme.Body, 16, UITheme.Text1, TextAnchor.MiddleLeft, "summary");
            UIFactory.Icon(f, "skull", 64, 913, 16, UITheme.Text2);
            UIFactory.T(f, "При гибели теряется всё, кроме подсумка.", 88, 908, 1100, 26, UITheme.Body, 15, UITheme.Text2, TextAnchor.MiddleLeft, "rule");

            // ---- raid conditions (right)
            float rx = 1256, rw = 600;
            UIFactory.Header(f, "Условия рейда", rx, 176, rw);
            Button[] Group(string title, float gy, string[] labels, string[] glyphs, Action<int> pick)
            {
                UIFactory.Caps(f, title, rx, gy, rw, 20, 13, UITheme.Text2, TextAnchor.MiddleLeft, 2f);
                return UIFactory.Segmented(f, rx, gy + 26, rw, 46, labels, glyphs, pick, 15);
            }
            _segBots = Group("Противники", 222, new[] { "МАЛО", "НОРМАЛЬНО", "МНОГО" }, new[] { "user", "users", "skull" },
                             k => { GameSettings.BotAmount = k; RefreshConditions(); RefreshRaidInfo(); });
            _segTod = Group("Время суток", 314, new[] { "УТРО", "ДЕНЬ", "ВЕЧЕР", "НОЧЬ" }, new[] { "sunset", "sun", "sunset", "moon" },
                            k => { GameSettings.TimeOfDay = k; RefreshConditions(); RefreshRaidInfo(); });
            // v13: 5 segments — «мокро» = after rain (value 4) sits next to «ясно»
            UIFactory.Caps(f, "Погода", rx, 406, rw, 20, 13, UITheme.Text2, TextAnchor.MiddleLeft, 2f);
            _segWeather = UIFactory.Segmented(f, rx, 432, rw, 46, new[] { "СЛУЧАЙНАЯ", "ЯСНО", "МОКРО", "ОСАДКИ", "ТУМАН" }, new[] { "rotate", "sun", "drop", "rain", "fog" },
                                              k => { GameSettings.Weather = WeatherOfSegment[k]; RefreshConditions(); RefreshRaidInfo(); }, 13);
            _segSeason = Group("Сезон", 498, new[] { "ОСЕНЬ", "ЗИМА" }, new[] { "leaf", "snow" },
                               k => { GameSettings.Winter = k == 1; RefreshConditions(); RefreshRaidInfo(); });

            var info = UIFactory.PanelBox(f, "RaidInfo", rx, 594, rw, 176, 0.9f);
            UIFactory.Icon(info, "info", 18, 18, 20, UITheme.Amber);
            _setupInfo = UIFactory.T(info, "", 50, 14, rw - 68, 80, UITheme.Body, 16, UITheme.Text1, TextAnchor.UpperLeft, "text");
            UIFactory.Divider(info, 18, 102, rw - 36);
            UIFactory.Caps(info, "Противники", 18, 112, 260, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _setupMulBots = UIFactory.Mono(info, "", 18, 132, 260, 30, 24, UITheme.AmberHi, TextAnchor.MiddleLeft, true);
            UIFactory.Caps(info, "Ценность лута", 300, 112, 260, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _setupMulLoot = UIFactory.Mono(info, "", 300, 132, 260, 30, 24, UITheme.AmberHi, TextAnchor.MiddleLeft, true);

            _setupWarnBox = UIFactory.Node(f, "Warn"); UIFactory.Place(_setupWarnBox, rx, 790, rw, 78);
            var wb = UIFactory.Img(_setupWarnBox, "bg", UITheme.A(UITheme.RedLo, 0.72f)); UIFactory.Stretch(wb.rectTransform);
            UIFactory.Border(_setupWarnBox, UITheme.A(UITheme.Red, 0.55f));
            UIFactory.Icon(_setupWarnBox, "warn", 18, 16, 22, UITheme.Red);
            _setupWarn = UIFactory.T(_setupWarnBox, "", 52, 8, rw - 68, 62, UITheme.BodyMedium, 16, UITheme.H(0xF0A08A), TextAnchor.MiddleLeft, "text");

            // ---- bottom bar
            BackButton(f, ShowMainMenu);
            UIFactory.Button(f, "СНАРЯЖЕНИЕ", 1176, 992, 250, 56, () => OpenStash(false), BtnStyle.Secondary, "backpack", 19);
            _startRaidBtn = UIFactory.Button(f, "НАЧАТЬ РЕЙД", 1456, 984, 400, 72, StartSelectedRaid, BtnStyle.Primary, null, 28);
            _startRaidSub = UIFactory.ButtonSub(_startRaidBtn, "", 13);
        }

        void RefreshRaidSetup()
        {
            if (_setupMap < 0 || _setupMap >= MapCatalog.All.Length)
            {   // first visit: the city if it is placed, otherwise the always-working procedural map
                _setupMap = 0;
                for (int i = 0; i < MapCatalog.All.Length; i++) if (MapCatalog.All[i].Playable && MapCatalog.All[i].Placed) { _setupMap = i; break; }
                if (!MapCatalog.All[_setupMap].Placed) _setupMap = System.Array.IndexOf(MapCatalog.All, MapCatalog.Polygon);
            }
            if (!MapCatalog.All[_setupMap].Playable) for (int i = 0; i < MapCatalog.All.Length; i++) if (MapCatalog.All[i].Playable) { _setupMap = i; break; }
            for (int i = 0; i < _mapCards.Count; i++) { UIFactory.SetSelected(_mapCards[i], i == _setupMap); _mapSelChips[i].SetActive(i == _setupMap); }
            var L = SaveSystem.Current.loadout;
            for (int i = 0; i < _setupTiles.Length; i++)
            {
                var st = _setupTiles[i]; if (st == null) continue;
                var it = L.Get(TileSlots[i]);
                st.name.text = it != null ? it.Def.name : "—";
                st.name.color = it != null ? UITheme.Text0 : UITheme.Text3;
                var sp = it != null ? UIAssets.ItemIcon(it.id, it.Def.type == ItemType.Weapon && it.ammo + it.chamber <= 0) : null;
                st.icon.sprite = sp; st.icon.enabled = sp != null;
                st.glyph.enabled = sp == null && st.glyph.sprite != null;
            }
            int mags = 0; foreach (var g in L.QuickGrids()) foreach (var it in g.items) if (it.Def != null && it.Def.type == ItemType.Magazine) mags++;
            _setupLoadout.text = "Магазинов в разгрузке/карманах: " + mags + "     ·     Вес " + L.TotalWeight().ToString("0.0") + " кг     ·     Стоимость " + UIFactory.Money(L.TotalValue());
            bool noWeapon = L.Get(EquipSlot.Primary) == null && L.Get(EquipSlot.Holster) == null;
            var sel = MapCatalog.All[_setupMap];
            string warn = noWeapon ? "Нет оружия! Возьмите его в «Снаряжении» или купите у скупщика." : "";
            if (sel.Playable && !sel.Placed) warn += (warn.Length > 0 ? "\n" : "") + "Город ещё не размещён (Severny > 3 и 4) — выберите «Полигон».";
            _setupWarn.text = warn;
            _setupWarnBox.gameObject.SetActive(warn.Length > 0);
            _startRaidBtn.interactable = MapCatalog.All[_setupMap].Playable;
            UIFactory.SetButtonText(_startRaidBtn, NetSession.IsHost ? "СТАРТ ДЛЯ ВСЕХ" : "НАЧАТЬ РЕЙД");
            if (_startRaidSub != null) _startRaidSub.text = sel.title.ToUpperInvariant() + "  ·  " + sel.minutes + " МИН";
            _setupSub.text = NetSession.IsHost ? "LAN-лобби: рейд начнётся у всех игроков" : "";
            RefreshConditions();
            RefreshRaidInfo();
        }

        void RefreshConditions()
        {
            UIFactory.SelectSegment(_segBots, GameSettings.BotAmount);
            UIFactory.SelectSegment(_segTod, GameSettings.TimeOfDay);
            UIFactory.SelectSegment(_segWeather, System.Array.IndexOf(WeatherOfSegment, GameSettings.Weather));
            UIFactory.SelectSegment(_segSeason, GameSettings.Winter ? 1 : 0);
        }

        /// <summary>What the chosen time of day / season / weather mean for the raid.</summary>
        void RefreshRaidInfo()
        {
            if (_setupInfo == null) return;
            int tod = GameSettings.TimeOfDay; bool w = GameSettings.Winter;
            float bots = tod == RaidOptions.TodNight ? 1.6f : w ? 1.1f : 1f;
            float luck = tod == RaidOptions.TodNight ? (w ? 1.9f : 1.75f) : w ? 1.4f : 1.25f;
            string s = "";
            if (tod == RaidOptions.TodNight) s += "Ночь: темно, у врагов фонари, они видят вас хуже (если ваш фонарь выключен). ";
            if (w) s += "Зима: снег, метели, мороз — следы и шаги по снегу слышнее. ";
            if (GameSettings.Weather == RaidOptions.WeatherAfterRain && !w) s += "После дождя: солнце, мокрый асфальт и лужи. ";
            else if (GameSettings.Weather == RaidOptions.WeatherAfterRain) s += "После дождя зимой = ясно. ";
            if (s.Length == 0) s = "Обычный рейд. ";
            _setupInfo.text = s.TrimEnd();
            _setupMulBots.text = "×" + bots.ToString("0.0#");
            _setupMulLoot.text = "×" + (luck / 1.25f).ToString("0.0#");
            _setupMulBots.color = bots > 1.01f ? UITheme.Orange : UITheme.AmberHi;
        }

        void StartSelectedRaid()
        {
            var m = MapCatalog.All[_setupMap];
            if (!m.Playable) { ShowMessage("Карта недоступна", "Сцена «" + m.scene + "» не найдена в Build Settings. В Unity выполните меню Severny > 2 и Severny > 3."); return; }
            PlayerPrefs.Save();
            if (NetSession.IsHost) { NetSession.I.HostStartRaid(m, GameSettings.BotAmount, GameSettings.TimeOfDay, GameSettings.Winter, GameSettings.Weather); return; }
            GameBootstrap.I.StartRaid(m, GameSettings.BotAmount, GameSettings.TimeOfDay, GameSettings.Winter, GameSettings.Weather);
        }

        /// <summary>Weather value (RaidOptions) of each segment of the «Погода» row.</summary>
        static readonly int[] WeatherOfSegment = { RaidOptions.WeatherRandom, RaidOptions.WeatherClear, RaidOptions.WeatherAfterRain, RaidOptions.WeatherPrecip, RaidOptions.WeatherFog };

        /// <summary>v13: screenshots of «ТЦ «Волна»» from the 16 shot points (ShotTool).</summary>
        void StartShots()
        {
            if (!PolygonKit.Available) { ShowMessage("Фототочки", "Карта «ТЦ «Волна»» (Resources/PolygonKit) не найдена."); return; }
            ShowConfirm("Фототочки", "Снять 16 фототочек карты «ТЦ «Волна»» (утро / день / вечер «после дождя» и «до» — свет v12)?\n" +
                        "Карта загрузится 4 раза (несколько минут), PNG 1920×1080 + маски неба, дальности и сводка сохранятся в папку shots.",
                        () => GameBootstrap.I.StartShots(false, false));
        }

        void StartBenchmark()
        {
            var m = MapCatalog.Severny.Playable && MapCatalog.Severny.Placed ? MapCatalog.Severny : MapCatalog.Polygon;
            ShowConfirm("Бенчмарк", "Запустить тест производительности на карте «" + m.title + "»?\nКамера пройдёт фиксированный маршрут (до 2 минут), результаты сохранятся в файл.", () => GameBootstrap.I.StartBenchmark(m));
        }

        // ------------------------------------------------------------------ loading
        void BuildLoading()
        {
            var s = NewScreen(UIScreen.Loading);
            _loadingImg = Background(s, 0f, null, true, h =>
            {
                UIFactory.Gradient(h, "fadeB", 1, 560, UITheme.A(UITheme.Bg0, 0.97f));
                UIFactory.Gradient(h, "fadeL", 2, 1000, UITheme.A(UITheme.Bg0, 0.65f));
                UIFactory.Gradient(h, "fadeT", 0, 180, UITheme.A(UITheme.Bg0, 0.6f));
            });
            var info = UIFactory.Node(s, "Info"); UIFactory.PlaceAt(info, 0f, 1f, 0, 0, 1400, 1080);
            UIFactory.Caps(info, "Загрузка локации", 96, 676, 800, 24, 15, UITheme.Amber, TextAnchor.MiddleLeft, 4f);
            _loadingTitle = UIFactory.T(info, "", 90, 700, 1300, 110, UITheme.DisplayBold, 86, UITheme.Text0, TextAnchor.MiddleLeft, "title");
            UIFactory.Track(_loadingTitle, 3f);
            _loadChipTime = UIFactory.Chip(info, "40 мин", 96, 820, UITheme.Text1, "clock", 26);
            _loadChipBots = UIFactory.Chip(info, "Противники", 200, 820, UITheme.Text1, "users", 26);
            UIFactory.Caps(info, "Совет", 96, 872, 200, 20, 13, UITheme.Amber, TextAnchor.MiddleLeft, 2.4f);
            _loadingTip = UIFactory.T(info, "", 96, 894, 1100, 50, UITheme.Body, 19, UITheme.Text1, TextAnchor.UpperLeft, "tip");
            var dot = UIFactory.Box(info, "pulse", 96, 997, 8, 8, UITheme.Amber);
            var pulse = dot.gameObject.AddComponent<UIPulse>(); pulse.graphic = dot; pulse.speed = 1.1f; pulse.min = 0.2f;
            _loadingStatus = UIFactory.T(info, "", 114, 988, 1200, 26, UITheme.Body, 16, UITheme.Text1, TextAnchor.MiddleLeft, "status");

            _loadingPct = UIFactory.Mono(s, "0%", 0, 0, 300, 40, 30, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            UIFactory.PlaceAt(_loadingPct.rectTransform, 1f, 1f, 1524, 978, 300, 40);
            var track = UIFactory.Img(s, "track", UITheme.A(UITheme.Line1, 0.9f));
            var trt = track.rectTransform; trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0); trt.pivot = new Vector2(0.5f, 0);
            trt.offsetMin = new Vector2(96, 46); trt.offsetMax = new Vector2(-96, 50);
            var glow = UIFactory.SetSoft(UIFactory.Sliced(track.transform, "glow", UIAssets.Glow, UITheme.A(UITheme.Amber, 0.25f)), "glow", UITheme.A(UITheme.Amber, 0.25f));
            _loadingBar = UIFactory.Img(track.transform, "fill", UITheme.Amber);
            var frt = _loadingBar.rectTransform; frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1); frt.pivot = new Vector2(0, 0.5f); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            glow.transform.SetParent(_loadingBar.transform, false); UIFactory.Stretch(glow.rectTransform, -10, -10, -10, -10);
        }

        static MapDef MapByTitle(string t)
        {
            foreach (var m in new[] { MapCatalog.Severny, MapCatalog.Polygon, MapCatalog.Tutorial }) if (m.title == t) return m;
            return null;
        }

        public void ShowLoading(string title)
        {
            _loadingTitle.text = title.ToUpperInvariant();
            _loadingTip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            _loadingTarget = _loadingShown = 0f;
            UIFactory.SetBar(_loadingBar, 0f);
            _loadingPct.text = "0%";
            var m = MapByTitle(title);
            Texture tex = m != null ? UIAssets.MapImage(m.id) : null;
            if (tex == null) tex = GameAssets.MenuBackground;
            if (_loadingImg != null && tex != null) { _loadingImg.texture = tex; var cv = _loadingImg.GetComponent<UICover>(); if (cv != null) cv.Fit(); }
            bool show = m != null && m.bots != null && m.bots.Length > 2;
            _loadChipTime.gameObject.SetActive(m != null);
            _loadChipBots.gameObject.SetActive(show && m.bots[2] > 0);
            if (m != null)
            {
                float w = UIFactory.SetChip(_loadChipTime, m.minutes + " мин");
                if (show) { UIFactory.SetChip(_loadChipBots, "Противники " + m.bots[0] + "–" + m.bots[2]); _loadChipBots.anchoredPosition = new Vector2(96 + w + 8, _loadChipBots.anchoredPosition.y); }
            }
            Show(UIScreen.Loading);
        }

        public void SetLoading(float t, string status) { _loadingTarget = Mathf.Clamp01(t); if (status != null) _loadingStatus.text = status; }

        void UpdateLoading()
        {
            _loadingShown = Mathf.MoveTowards(_loadingShown, _loadingTarget, Time.unscaledDeltaTime * 1.5f);
            UIFactory.SetBar(_loadingBar, _loadingShown);
            _loadingPct.text = Mathf.RoundToInt(_loadingShown * 100f) + "%";
        }

        // ------------------------------------------------------------------ settings
        void BuildSettings()
        {
            var s = NewScreen(UIScreen.Settings);
            Background(s, 0.9f);
            var f = UIFactory.Center(s);
            ScreenHeader(f, "Меню  /  Настройки", "Настройки");
            int[] caps = { 0, 60, 90, 120, 144, 165 };
            Func<float, float, float, float> clamp = (v, a, b) => Mathf.Clamp(v, a, b);
            const float lx = 64, rx = 992, cw = 864, rh = 66, gap = 6;

            float y = 176;
            UIFactory.Header(f, "Управление", lx, y, cw); y += 46;
            UIFactory.Stepper(f, "Чувствительность мыши", "Скорость поворота камеры", lx, y, cw, rh, () => GameSettings.Sensitivity.ToString("0.0"),
                d => { GameSettings.Sensitivity = clamp(Mathf.Round((GameSettings.Sensitivity + d * 0.1f) * 10f) / 10f, 0.1f, 5f); Changed(); },
                () => (GameSettings.Sensitivity - 0.1f) / 4.9f); y += rh + gap;
            UIFactory.Stepper(f, "Инверсия мыши по Y", "Мышь вверх — взгляд вниз", lx, y, cw, rh, () => GameSettings.InvertY ? "Да" : "Нет",
                d => { GameSettings.InvertY = !GameSettings.InvertY; Changed(); }, null); y += rh + gap + 22;
            UIFactory.Header(f, "Изображение", lx, y, cw); y += 46;
            UIFactory.Stepper(f, "Поле зрения (FOV)", "Угол обзора по вертикали, 56–96°", lx, y, cw, rh, () => GameSettings.Fov.ToString("0"),
                d => { GameSettings.Fov = clamp(GameSettings.Fov + d * 2f, 56f, 96f); Changed(); }, () => (GameSettings.Fov - 56f) / 40f); y += rh + gap + 22;
            UIFactory.Header(f, "Звук", lx, y, cw); y += 46;
            UIFactory.Stepper(f, "Громкость", "Общая громкость игры", lx, y, cw, rh, () => Mathf.RoundToInt(GameSettings.Volume * 100f) + "%",
                d => { GameSettings.Volume = clamp(GameSettings.Volume + d * 0.05f, 0f, 1f); Changed(); }, () => GameSettings.Volume);

            y = 176;
            UIFactory.Header(f, "Графика и производительность", rx, y, cw); y += 46;
            UIFactory.Stepper(f, "Масштаб рендера", "Ниже — выше FPS, но картинка мягче", rx, y, cw, rh, () => Mathf.RoundToInt(GameSettings.RenderScale * 100f) + "%",
                d => { GameSettings.RenderScale = clamp(Mathf.Round((GameSettings.RenderScale + d * 0.05f) * 20f) / 20f, 0.5f, 1f); Changed(); },
                () => (GameSettings.RenderScale - 0.5f) / 0.5f); y += rh + gap;
            // v12: graphics preset + anti-aliasing (GfxQuality); reflections / street lights / effects follow on the next raid
            UIFactory.Stepper(f, "Качество графики", "Тени, AO, отражения, фонари, эффекты", rx, y, cw, rh, () => GfxQuality.Names[GfxQuality.Level],
                d => { GameSettings.GraphicsPreset = (GameSettings.GraphicsPreset + d + 4) % 4; Changed(); },
                () => GameSettings.GraphicsPreset / 3f); y += rh + gap;
            UIFactory.Stepper(f, "Сглаживание", "SMAA — чётко, TAA — мягче, без лесенок", rx, y, cw, rh, () => GfxQuality.AANames[GfxQuality.AA],
                d => { GameSettings.AntiAliasing = (GameSettings.AntiAliasing + d + 4) % 4; Changed(); }, null); y += rh + gap;
            UIFactory.Stepper(f, "Вертикальная синхронизация", "Убирает разрывы кадра", rx, y, cw, rh, () => GameSettings.VSync ? "Вкл" : "Выкл",
                d => { GameSettings.VSync = !GameSettings.VSync; Changed(); }, null); y += rh + gap;
            UIFactory.Stepper(f, "Ограничение FPS", "Действует при выключенной синхронизации", rx, y, cw, rh, () => GameSettings.FpsCap <= 0 ? "Нет" : GameSettings.FpsCap.ToString(), d =>
            {
                int i = Array.IndexOf(caps, GameSettings.FpsCap); if (i < 0) i = 0;
                GameSettings.FpsCap = caps[(i + d + caps.Length) % caps.Length]; Changed();
            }, null); y += rh + gap;
            UIFactory.Stepper(f, "Показывать FPS", "Счётчик кадров в углу экрана", rx, y, cw, rh, () => GameSettings.ShowFps ? "Да" : "Нет",
                d => { GameSettings.ShowFps = !GameSettings.ShowFps; Changed(); }, null); y += rh + gap + 22;

            var tip = UIFactory.PanelBox(f, "Tip", rx, y, cw, 84, 0.88f);
            UIFactory.Icon(tip, "info", 20, 31, 22, UITheme.Amber);
            UIFactory.T(tip, "Совет: при нехватке FPS снизьте «Качество графики» или «Масштаб рендера» (ниже 100% — апскейл FSR). Отражения, дым и туман меняются со следующего рейда. F3 — статистика.", 56, 12, cw - 76, 60,
                        UITheme.Body, 16, UITheme.Text1, TextAnchor.MiddleLeft, "text");

            BackButton(f, CloseSettings);
            _benchBtn = UIFactory.Button(f, "БЕНЧМАРК (F8)", 1576, 992, 280, 56, () => { CloseSettings(); StartBenchmark(); }, BtnStyle.Secondary, "bars", 19);
        }

        static void Changed() { GameSettings.Apply(); PlayerPrefs.Save(); }

        public void OpenSettings(UIScreen from)
        {
            _settingsReturn = from;
            _benchBtn.gameObject.SetActive(from == UIScreen.MainMenu);
            Show(UIScreen.Settings);
        }

        void CloseSettings()
        {
            if (_settingsReturn == UIScreen.Pause && InRaid) ShowPause(); else ShowMainMenu();
        }

        // ------------------------------------------------------------------ character ("base": profile, statistics, controls)
        static readonly string[][] Controls =
        {
            new[] { "W", "A", "S", "D", "Движение" }, new[] { "SHIFT", "Бег" }, new[] { "ALT", "Тихий шаг" }, new[] { "C", "Присед" },
            new[] { "ПРОБЕЛ", "Прыжок" }, new[] { "Q", "E", "Наклон влево / вправо" }, new[] { "ЛКМ", "Огонь" }, new[] { "ПКМ", "Прицел" },
            new[] { "B", "Режим огня" }, new[] { "R", "Перезарядка: магазины из разгрузки и карманов, иначе по патрону" }, new[] { "T", "Проверить магазин" },
            new[] { "1", "2", "Основное оружие / пистолет" }, new[] { "H", "Быстрое лечение" }, new[] { "L", "Фонарь" },
            new[] { "F", "Взаимодействие: подобрать, обыскать" }, new[] { "TAB", "Инвентарь" }, new[] { "O", "Выходы и время" },
            new[] { "ESC", "Пауза" }, new[] { "F3", "F9", "Статистика / NavTester (проверка карты)" },
        };

        void BuildBase()
        {
            var s = NewScreen(UIScreen.Base);
            Background(s, 0.88f);
            var f = UIFactory.Center(s);
            ScreenHeader(f, "Главное меню  /  Персонаж", "Персонаж");
            UIFactory.Button(f, "ЗАДАНИЯ / F6", 1360, 90, 450, 56, OpenQuests);

            // ---- dossier
            var d = UIFactory.PanelBox(f, "Dossier", 64, 176, 600, 764, 0.9f);
            UIFactory.Box(d, "accent", 0, 0, 600, 2, UITheme.Amber);
            UIFactory.Caps(d, "Досье оператора", 24, 18, 400, 20, 13, UITheme.Text2, TextAnchor.MiddleLeft, 2.4f);
            var av = UIFactory.Box(d, "Avatar", 24, 52, 150, 150, UITheme.Bg0);
            UIFactory.Pattern(av.transform, "hatch", UIAssets.Hatch, UITheme.A(UITheme.Line1, 0.35f));
            UIFactory.Border(av.transform, UITheme.Line1);
            var ai = UIFactory.Icon(av.transform, "user", 27, 27, 96, UITheme.Text2);
            var br = UIFactory.Sliced(av.transform, "brackets", UIAssets.Brackets, UITheme.Amber); UIFactory.Stretch(br.rectTransform, -4, -4, -4, -4);
            _chNick = UIFactory.T(d, "", 196, 52, 380, 56, UITheme.Display, 40, UITheme.Text0, TextAnchor.MiddleLeft, "nick");
            _chLevel = UIFactory.Chip(d, "Уровень 1", 198, 114, UITheme.Amber, "star", 26);
            UIFactory.Caps(d, "Опыт", 196, 160, 120, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _chXp = UIFactory.Mono(d, "", 196, 160, 380, 18, 13, UITheme.Text1, TextAnchor.MiddleRight);
            _chXpBar = UIFactory.Bar(d, 196, 184, 380, 6, UITheme.Amber);
            UIFactory.Divider(d, 24, 224, 552);
            UIFactory.Caps(d, "На счету", 24, 240, 300, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _chMoney = UIFactory.Mono(d, "", 24, 260, 552, 54, 42, UITheme.AmberHi, TextAnchor.MiddleLeft, true);
            UIFactory.Divider(d, 24, 330, 552);
            UIFactory.Caps(d, "Исходы рейдов", 24, 346, 300, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _chOutcome = UIFactory.Box(d, "Outcomes", 24, 374, 552, 10, UITheme.A(UITheme.Bg0, 0.9f)).rectTransform;
            foreach (var n in new[] { "survived", "kia", "mia" }) UIFactory.Img(_chOutcome, n, n == "survived" ? UITheme.Green : n == "kia" ? UITheme.Red : UITheme.Orange);
            // legend: colored squares are images (the UI fonts have no "■" glyph), laid out after the counts are known
            _chLegend = new Text[3]; _chLegendDot = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                _chLegendDot[i] = UIFactory.Box(d, "legendDot" + i, 24, 400, 9, 9, i == 0 ? UITheme.Green : i == 1 ? UITheme.Red : UITheme.Orange);
                _chLegend[i] = UIFactory.T(d, "", 40, 392, 240, 24, UITheme.Body, 15, UITheme.Text1, TextAnchor.MiddleLeft, "legend" + i);
            }
            UIFactory.Divider(d, 24, 436, 552);
            UIFactory.Caps(d, "Правило рейда", 24, 452, 300, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            UIFactory.T(d, "Цель: найти ценное и уйти через один из выходов до конца времени. Погибли — потеряли всё, кроме подсумка.",
                        24, 476, 552, 64, UITheme.Body, 17, UITheme.Text0, TextAnchor.UpperLeft, "goal");
            UIFactory.Caps(d, "Файл профиля", 24, 636, 300, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _chFile = UIFactory.Mono(d, "", 24, 658, 552, 54, 12, UITheme.Text2, TextAnchor.UpperLeft);
            _chSlot = UIFactory.T(d, "", 24, 718, 552, 30, UITheme.BodyMedium, 15, UITheme.AmberHi, TextAnchor.MiddleLeft, "slot");

            // ---- statistics tiles
            UIFactory.Header(f, "Статистика", 704, 176, 552);
            string[] cap = { "Рейдов", "Выжил", "Погиб", "Пропал / покинул", "Выживаемость", "Убито противников", "Продано скупщику", "Схрон" };
            for (int i = 0; i < 8; i++)
            {
                float x = 704 + (i % 2) * 284, y = 222 + (i / 2) * 152;
                _chVals[i] = StatTile(f, cap[i], x, y, 268, 136, out _chSubs[i], i >= 6 ? 26 : 34);
            }
            var tipBox = UIFactory.PanelBox(f, "StatsNote", 704, 830, 552, 110, 0.86f);
            UIFactory.Icon(tipBox, "info", 18, 14, 20, UITheme.Amber);
            UIFactory.T(tipBox, "Опыт начисляется за выживание, убийства и вынесенную добычу. Деньги и схрон общие для всех рейдов; " +
                        "снаряжение, надетое на персонажа, уходит с вами в рейд.", 50, 14, 484, 88, UITheme.Body, 16, UITheme.Text1, TextAnchor.UpperLeft, "text");

            // ---- controls
            UIFactory.Header(f, "Управление", 1296, 176, 560);
            float cy = 220;
            foreach (var row in Controls)
            {
                float kx = 1296;
                for (int k = 0; k < row.Length - 1; k++) kx += UIFactory.Keycap(f, row[k], kx, cy, 26) + 4;
                UIFactory.T(f, row[row.Length - 1], 1296 + 136, cy - 2, 424, 30, UITheme.Body, 16, UITheme.Text1, TextAnchor.MiddleLeft, "ctl");
                cy += 33;
            }

            BackButton(f, ShowMainMenu);
            UIFactory.Button(f, "СБРОСИТЬ ПРОФИЛЬ", 1556, 992, 300, 56, () => ShowConfirm("Сброс профиля", "Удалить прогресс, деньги и схрон и начать заново?", () => { SaveSystem.ResetProfile(); RefreshBase(); }), BtnStyle.Danger, "warn", 18);
        }

        void RefreshBase()
        {
            var p = SaveSystem.Current;
            int lvl = p.Level, lo = (lvl - 1) * (lvl - 1) * 1000;
            int lvlXp = (p.Level) * (p.Level) * 1000;
            _chNick.text = p.nickname;
            UIFactory.SetChip(_chLevel, "Уровень " + lvl);
            _chXp.text = UITheme.Num(p.xp) + " / " + UITheme.Num(lvlXp);
            UIFactory.SetBar(_chXpBar, (p.xp - lo) / (float)Mathf.Max(1, lvlXp - lo));
            _chMoney.text = UITheme.Rub(p.roubles);
            float tot = Mathf.Max(1, p.raids), x = 0f;
            int[] parts = { p.survived, p.kia, p.mia };
            for (int i = 0; i < 3; i++)
            {
                var rt = (RectTransform)_chOutcome.GetChild(i);
                float w = p.raids > 0 ? 552f * parts[i] / tot : 0f;
                UIFactory.Place(rt, x, 0, w, 10); x += w;
            }
            string[] legend = { "Выжил " + p.survived, "Погиб " + p.kia, "Пропал / покинул " + p.mia };
            float lx = 24f;
            for (int i = 0; i < 3; i++)
            {
                _chLegend[i].text = legend[i];
                float lw = Mathf.Ceil(UIFactory.TextWidth(_chLegend[i])) + 2f;
                UIFactory.Place(_chLegendDot[i].rectTransform, lx, 400, 9, 9);
                UIFactory.Place(_chLegend[i].rectTransform, lx + 16, 392, lw, 24);
                lx += 16 + lw + 26;
            }
            _chVals[0].text = p.raids.ToString(); _chSubs[0].text = "всего вылазок";
            _chVals[1].text = p.survived.ToString(); _chSubs[1].text = "успешная эвакуация";
            _chVals[2].text = p.kia.ToString(); _chSubs[2].text = "снаряжение потеряно";
            _chVals[3].text = p.mia.ToString(); _chSubs[3].text = "пропал без вести / покинул";
            _chVals[4].text = p.raids > 0 ? Mathf.RoundToInt(100f * p.survived / p.raids) + "%" : "—"; _chSubs[4].text = "доля успешных рейдов";
            _chVals[5].text = p.kills.ToString(); _chSubs[5].text = "за все рейды";
            _chVals[6].text = UITheme.Rub(Math.Min(int.MaxValue, p.lootSoldTotal)); _chSubs[6].text = "выручка за всё время";
            _chVals[7].text = UITheme.Rub(p.stash.Value()); _chSubs[7].text = p.stash.items.Count + " предметов в схроне";
            _chVals[4].color = p.raids > 0 && p.survived * 2 >= p.raids ? UITheme.Green : UITheme.Text0;
            _chFile.text = SaveSystem.PathFile;
            _chSlot.text = SaveSystem.Slot > 1 ? "Копия игры №" + SaveSystem.Slot + " — отдельный профиль" : "";
        }

        // ------------------------------------------------------------------ pause
        void BuildPause()
        {
            var s = NewScreen(UIScreen.Pause);
            var dim = UIFactory.Img(s, "dim", UITheme.A(UITheme.Bg0, 0.78f), true); UIFactory.Stretch(dim.rectTransform);
            var vig = UIFactory.Img(s, "vignette", UITheme.A(Color.black, 0.6f)); UIFactory.SetSoft(vig, "vign", vig.color); UIFactory.Stretch(vig.rectTransform);
            var f = UIFactory.Center(s);
            var p = UIFactory.PanelBox(f, "panel", 690, 280, 540, 520, 0.97f);
            var sh = UIFactory.SetSoft(UIFactory.Sliced(p, "shadow", UIAssets.Glow, UITheme.A(Color.black, 0.6f)), "glow", UITheme.A(Color.black, 0.6f)); UIFactory.Stretch(sh.rectTransform, -30, -30, -30, -30); sh.transform.SetAsFirstSibling();
            UIFactory.Box(p, "accent", 0, 0, 540, 3, UITheme.Amber);
            UIFactory.Caps(p, "Рейд", 40, 30, 300, 20, 13, UITheme.Text2, TextAnchor.MiddleLeft, 3f);
            var t = UIFactory.T(p, "ПАУЗА", 38, 48, 460, 64, UITheme.Display, 50, UITheme.Text0, TextAnchor.MiddleLeft, "title");
            UIFactory.Track(t, 3f);
            _pauseSub = UIFactory.T(p, "", 40, 112, 460, 26, UITheme.BodyMedium, 17, UITheme.Text1, TextAnchor.MiddleLeft, "sub");
            UIFactory.Divider(p, 40, 152, 460);
            UIFactory.Button(p, "ПРОДОЛЖИТЬ", 40, 176, 460, 64, ResumeFromPause, BtnStyle.Primary, "tri_r", 24);
            UIFactory.Button(p, "НАСТРОЙКИ", 40, 252, 460, 56, () => OpenSettings(UIScreen.Pause), BtnStyle.Secondary, "gear", 20);
            UIFactory.Button(p, "ПОКИНУТЬ РЕЙД", 40, 320, 460, 56, () => ShowConfirm("Покинуть рейд", "Покинуть рейд? Всё снаряжение, кроме подсумка, будет потеряно.", () =>
            {
                Time.timeScale = 1f; AudioListener.pause = false;
                if (RaidManager.I != null) RaidManager.I.LeaveRaid();
            }), BtnStyle.Danger, "exit", 20);
            _pauseNote = UIFactory.T(p, "Игра на паузе (одиночный режим)", 40, 396, 460, 48, UITheme.Body, 15, UITheme.Text2, TextAnchor.UpperLeft, "note");
            UIFactory.KeyHint(p, "ESC", "продолжить", 40, 462, 26, 15, UITheme.Text2);
        }

        void ShowPause()
        {
            var rm = RaidManager.I;
            if (_pauseSub != null)
            {
                string t = rm != null && rm.map != null ? rm.map.title : "";
                if (rm != null)
                {
                    int mm = Mathf.FloorToInt(Mathf.Max(0f, rm.timeLeft) / 60f), ss = Mathf.FloorToInt(Mathf.Max(0f, rm.timeLeft) % 60f);
                    t += (t.Length > 0 ? "   ·   " : "") + "осталось " + mm.ToString("00") + ":" + ss.ToString("00");
                }
                _pauseSub.text = t;
                _pauseNote.text = NetSession.Active ? "Сетевая игра: мир не останавливается, ваш персонаж остаётся в рейде." : "Игра на паузе (одиночный режим).";
            }
            Show(UIScreen.Pause);
        }

        void ResumeFromPause()
        {
            if (InRaid && RaidManager.I.Running) Show(UIScreen.Hud); else ShowMainMenu();
        }

        // ------------------------------------------------------------------ results
        void BuildResults()
        {
            var s = NewScreen(UIScreen.Results);
            Background(s, 0.84f, null, false, h => { _resultBand = UIFactory.Gradient(h, "band", 0, 520, UITheme.A(UITheme.Green, 0.16f)); });
            var f = UIFactory.Center(s);
            UIFactory.Caps(f, "Итоги рейда", 0, 112, 1920, 24, 16, UITheme.Text2, TextAnchor.MiddleCenter, 5f);
            _resultTitle = UIFactory.T(f, "", 0, 134, 1920, 136, UITheme.DisplayBold, 110, UITheme.Green, TextAnchor.MiddleCenter, "title");
            UIFactory.Track(_resultTitle, 5f);
            _resultLine = UIFactory.Box(f, "line", 900, 280, 120, 3, UITheme.Green);
            _resultSub = UIFactory.T(f, "", 160, 296, 1600, 34, UITheme.BodyMedium, 21, UITheme.Text1, TextAnchor.MiddleCenter, "sub");
            const float tw = 296, th = 136, gap = 16; float x0 = (1920 - (5 * tw + 4 * gap)) * 0.5f;
            for (int i = 0; i < 5; i++)
            {
                var t = UIFactory.PanelBox(f, "ResultTile" + i, x0 + i * (tw + gap), 376, tw, th, 0.9f);
                _resultCaps[i] = UIFactory.Caps(t, "", 20, 16, tw - 40, 20, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
                _resultVals[i] = UIFactory.Mono(t, "", 20, 42, tw - 40, 52, 38, UITheme.Text0, TextAnchor.MiddleLeft, true);
                _resultSubs[i] = UIFactory.T(t, "", 20, th - 34, tw - 40, 22, UITheme.Body, 14, UITheme.Text2, TextAnchor.MiddleLeft, "sub");
            }
            var prof = UIFactory.PanelBox(f, "Profile", 560, 544, 800, 96, 0.9f);
            UIFactory.Caps(prof, "Профиль", 24, 16, 200, 18, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _resultLevel = UIFactory.T(prof, "", 24, 38, 360, 44, UITheme.Display, 30, UITheme.Text0, TextAnchor.MiddleLeft, "level");
            UIFactory.Caps(prof, "На счету", 400, 16, 376, 18, 12, UITheme.Text2, TextAnchor.MiddleRight, 1.8f);
            _resultMoney = UIFactory.Mono(prof, "", 400, 38, 376, 44, 30, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            _resultBody = UIFactory.T(f, "", 360, 664, 1200, 60, UITheme.Body, 18, UITheme.Text1, TextAnchor.UpperCenter, "note");
            UIFactory.Button(f, "ДАЛЕЕ", 790, 930, 340, 76, () =>
            {
                if (NetSession.IsHost && NetSession.I.ClientsInRaid > 0)
                {
                    ShowConfirm("Другие игроки ещё в рейде", "Мир рейда работает на вашем ПК. Если выйти сейчас, рейд закончится и для остальных " +
                                "(их снаряжение сохранится, как при эвакуации).\n\nВыйти в лобби?", ResultsContinue);
                    return;
                }
                ResultsContinue();
            }, BtnStyle.Primary, "chev_r", 28);
        }

        void ResultsContinue()
        {
            {
                var p = SaveSystem.Current;
                bool kit = p.GiveInsuranceKitIfBroke();
                if (kit) SaveSystem.Save();
                GameBootstrap.I.ReturnToMenu(kit ? "Страховка" : null, kit ? "У вас не осталось оружия. Выдан бесплатный комплект: пистолет и патроны." : null);
            }
        }

        public void ShowResults(RaidResult r)
        {
            string title; Color c;
            switch (r.outcome)
            {
                case RaidManager.Outcome.Survived: title = "ВЫЖИЛ"; c = UITheme.Green; break;
                case RaidManager.Outcome.Killed: title = "ПОГИБ"; c = UITheme.Red; break;
                case RaidManager.Outcome.MIA: title = "ПРОПАЛ БЕЗ ВЕСТИ"; c = UITheme.Orange; break;
                default: title = "ПОКИНУЛ РЕЙД"; c = UITheme.Text1; break;
            }
            bool survived = r.outcome == RaidManager.Outcome.Survived;
            _resultTitle.text = title; _resultTitle.color = c;
            _resultLine.color = c; UIFactory.SetGradient(_resultBand, 0, UITheme.A(c, 0.16f));
            _resultSub.text = r.map + (survived && !string.IsNullOrEmpty(r.exitName) ? " · выход: " + r.exitName : "") + (string.IsNullOrEmpty(r.cause) ? "" : " · " + r.cause);
            var p = SaveSystem.Current;
            int m = Mathf.FloorToInt(r.seconds / 60f), sec = Mathf.FloorToInt(r.seconds % 60f);
            void Tile(int i, string cap, string val, string sub, Color vc) { _resultCaps[i].text = cap.ToUpperInvariant(); _resultVals[i].text = val; _resultSubs[i].text = sub; _resultVals[i].color = vc; }
            Tile(0, "Время в рейде", m + ":" + sec.ToString("00"), "мин : сек", UITheme.Text0);
            Tile(1, "Убито противников", r.kills.ToString(), "", UITheme.Text0);
            Tile(2, "Точность", r.shots > 0 ? Mathf.RoundToInt(100f * r.hits / r.shots) + "%" : "—", "попаданий " + r.hits + " из " + r.shots + " выстрелов", UITheme.Text0);
            Tile(3, "Опыт", "+" + r.xp, "уровень " + p.Level, UITheme.AmberHi);
            if (survived) Tile(4, "Вынесено добычи", UITheme.Rub(r.foundValue), "в вашем снаряжении", UITheme.Green);
            else Tile(4, "Потеряно снаряжения", UITheme.Rub(Mathf.Max(0, r.lostValue)), "подсумок сохранён", UITheme.Red);
            _resultLevel.text = "Уровень " + p.Level;
            _resultMoney.text = UITheme.Rub(p.roubles);
            _resultBody.text = survived ? "Добыча теперь в вашем снаряжении — переложите её в схрон или продайте скупщику." : "Подсумок сохранён.";
            Show(UIScreen.Results);
        }

        // ------------------------------------------------------------------ modal (top canvas)
        RectTransform _modal; Text _modalTitle, _modalText; Button _modalOk, _modalYes, _modalNo; Action _modalAction; bool _modalOpen;
        RectTransform _modalTextContent, _modalPanel, _modalScrollRoot; ScrollRect _modalScroll; Image _modalIcon;
        const float ModalW = 900f;

        void BuildModal()
        {
            _modal = UIFactory.Stretch(UIFactory.Node(_topRoot, "Modal"));
            _modal.gameObject.AddComponent<UIFadeIn>().duration = 0.12f;
            var dim = UIFactory.Img(_modal, "dim", UITheme.A(UITheme.Bg0, 0.8f), true); UIFactory.Stretch(dim.rectTransform);
            var vig = UIFactory.Img(_modal, "vignette", UITheme.A(Color.black, 0.5f)); UIFactory.SetSoft(vig, "vign", vig.color); UIFactory.Stretch(vig.rectTransform);
            _modalPanel = UIFactory.Node(_modal, "panel");
            _modalPanel.anchorMin = _modalPanel.anchorMax = _modalPanel.pivot = new Vector2(0.5f, 0.5f); _modalPanel.sizeDelta = new Vector2(ModalW, 400);
            var sh = UIFactory.SetSoft(UIFactory.Sliced(_modalPanel, "shadow", UIAssets.Glow, UITheme.A(Color.black, 0.65f)), "glow", UITheme.A(Color.black, 0.65f)); UIFactory.Stretch(sh.rectTransform, -34, -34, -34, -34);
            var bg = UIFactory.Img(_modalPanel, "bg", UITheme.A(UITheme.Bg1, 0.985f), true); UIFactory.Stretch(bg.rectTransform);
            UIFactory.Border(_modalPanel, UITheme.Line1);
            var top = UIFactory.Img(_modalPanel, "accent", UITheme.Amber); var trt = top.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1); trt.sizeDelta = new Vector2(0, 3); trt.anchoredPosition = Vector2.zero;
            _modalIcon = UIFactory.Icon(_modalPanel, "warn", 36, 36, 26, UITheme.Amber, "icon");
            _modalTitle = UIFactory.T(_modalPanel, "", 76, 28, ModalW - 112, 44, UITheme.Display, 30, UITheme.Text0, TextAnchor.MiddleLeft, "title");
            UIFactory.Track(_modalTitle, 1.4f);
            UIFactory.Divider(_modalPanel, 36, 88, ModalW - 72);
            _modalTextContent = UIFactory.ScrollView(_modalPanel, 36, 108, ModalW - 72, 300, out _modalScroll);
            _modalScrollRoot = (RectTransform)_modalScroll.transform;
            _modalScrollRoot.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            _modalText = UIFactory.T(_modalTextContent, "", 0, 2, ModalW - 92, 300, UITheme.Body, 19, UITheme.Text1, TextAnchor.UpperLeft, "text");
            _modalText.lineSpacing = 1.06f;
            var foot = UIFactory.Node(_modalPanel, "foot");
            foot.anchorMin = new Vector2(0, 0); foot.anchorMax = new Vector2(1, 0); foot.pivot = new Vector2(0.5f, 0); foot.sizeDelta = new Vector2(0, 96); foot.anchoredPosition = Vector2.zero;
            UIFactory.Divider(foot, 36, 0, ModalW - 72);
            UIFactory.KeyHint(foot, "ESC", "отмена", 36, 35, 26, 15, UITheme.Text2);
            _modalOk = UIFactory.Button(foot, "OK", ModalW - 36 - 220, 20, 220, 56, () => CloseModal(true), BtnStyle.Primary, null, 21);
            _modalYes = UIFactory.Button(foot, "ДА", ModalW - 36 - 220, 20, 220, 56, () => CloseModal(true), BtnStyle.Primary, null, 21);
            _modalNo = UIFactory.Button(foot, "НЕТ", ModalW - 36 - 220 - 14 - 200, 20, 200, 56, () => CloseModal(false), BtnStyle.Secondary, null, 21);
            _modal.gameObject.SetActive(false);
        }

        public void ShowMessage(string title, string text) => OpenModal(title, text, null, false);
        public void CloseModalIfOpen() { if (_modalOpen) CloseModal(false); }
        public void ShowConfirm(string title, string text, Action yes) => OpenModal(title, text, yes, true);

        void OpenModal(string title, string text, Action action, bool confirm)
        {
            _modalTitle.text = (title ?? "").ToUpperInvariant(); _modalText.text = text ?? "";
            _modalText.rectTransform.sizeDelta = new Vector2(ModalW - 92, 300);
            float th = Mathf.Max(30f, _modalText.preferredHeight + 6f), view = Mathf.Clamp(th, 40f, 460f);
            _modalText.rectTransform.sizeDelta = new Vector2(ModalW - 92, th);
            _modalTextContent.sizeDelta = new Vector2(0, th + 8);
            _modalScrollRoot.sizeDelta = new Vector2(ModalW - 72, view);
            _modalPanel.sizeDelta = new Vector2(ModalW, 108 + view + 28 + 96);
            _modalScroll.verticalNormalizedPosition = 1f;
            UIFactory.SetIcon(_modalIcon, confirm ? "warn" : "info");
            _modalAction = action;
            _modalOk.gameObject.SetActive(!confirm); _modalYes.gameObject.SetActive(confirm); _modalNo.gameObject.SetActive(confirm);
            _modal.gameObject.SetActive(true); _modal.SetAsLastSibling();
            _modalOpen = true;
            if (Current == UIScreen.Hud) { GameInput.GameplayEnabled = false; GameInput.SetCursorLocked(false); }
        }

        void CloseModal(bool ok)
        {
            _modal.gameObject.SetActive(false);
            _modalOpen = false;
            var a = _modalAction; _modalAction = null;
            if (Current == UIScreen.Hud && !AutoBenchmark.Running) { GameInput.GameplayEnabled = true; GameInput.SetCursorLocked(true); }
            if (ok) a?.Invoke();
        }
    }
}
