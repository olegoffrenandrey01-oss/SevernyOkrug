// v11 inventory / stash / trader (EFT-like): equipment slots with rendered icons, rig / pockets / backpack / pouch grids,
// loot container (search progress), stash (scroll), trader offers by category, item card with stats + actions, hover tooltips.
// Mouse: drag & drop (R rotates while dragging), Ctrl+click = quick move, right click = use / unload, click = info.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    public class InventoryUI
    {
        public enum Mode { Raid, Stash, Trader }

        struct Loc
        {
            public InventoryGrid grid; public bool isSlot; public EquipSlot slot;
            public static Loc Grid(InventoryGrid g) => new Loc { grid = g };
            public static Loc Slot(EquipSlot s) => new Loc { isSlot = true, slot = s };
            public bool Valid => isSlot || grid != null;
        }

        class SlotView
        {
            public EquipSlot slot; public string glyph; public float w, h;
            public RectTransform rt, chip;
            public Image fill, edge, icon, glow, empty, accept, sel, cond, foot;
            public Text name, info, emptyText;
        }

        struct Stat { public string k, v; public Color c; public Stat(string k, string v, Color c) { this.k = k; this.v = v; this.c = c; } }

        class OfferRow { public ItemDef d; public RectTransform rt; public Image bg, accent; public Text price; public int cat; }

        public RectTransform root;
        public Mode mode;
        public LootContainer container;
        public bool IsOpen => root.gameObject.activeSelf;

        readonly UIManager _ui;
        RectTransform _frame, _top;
        GridView _rig, _pockets, _backpack, _secure, _right, _containerView;
        readonly List<GridView> _grids = new List<GridView>();
        SlotView[] _slots;
        // backdrop + top bar
        Image _bgBlock; RectTransform _bgArt;
        RectTransform _raidTitle, _raidInfo, _tabsRoot, _moneyRoot, _levelChip;
        Button _tabGear, _tabTrader;
        Text _money, _raidMap, _raidClock; int _clockShown = -1;
        // equipment column
        RectTransform _equipPanel, _healthPanel, _middlePanel, _hintsCard, _overChip;
        readonly Image[] _hpBars = new Image[Health.Parts], _bodyParts = new Image[Health.Parts], _hpDrops = new Image[Health.Parts];
        readonly Text[] _hpTexts = new Text[Health.Parts], _hpNames = new Text[Health.Parts];
        readonly List<RectTransform> _hpChips = new List<RectTransform>();
        Text _hpTotal, _healthNote; int _hpSig = int.MinValue;
        Text _weightVal, _valueVal; Image _weightBar;
        // right column
        RectTransform _rightScrollRoot, _rightScrollContent, _rightRaidRoot, _rightHint, _ground, _searchBarRoot;
        ScrollRect _rightScroll;
        Text _stashInfo; Image _searchBar;
        Image _groundEdge, _groundHatch, _groundIcon; Text _groundText, _groundSub; int _groundState = -1;
        // trader
        RectTransform _traderPanel, _offersContent, _offersRoot, _sellZone;
        readonly List<OfferRow> _offerRows = new List<OfferRow>();
        Button[] _catTabs; Text _offersCount; int _cat;
        Image _sellEdge; Text _sellTitle, _sellName, _sellPrice;
        OfferRow _hoverOffer;
        // item card
        Text _infoTitle, _infoBody, _infoPrice, _infoPriceSub;
        Image _infoIcon, _infoGlow, _infoNotch, _infoEdge;
        RectTransform _actions, _infoChips, _infoStats, _hintsNormal, _hintsTrader, _dragLayer;
        readonly List<GameObject> _actionButtons = new List<GameObject>();
        readonly List<GameObject> _infoDyn = new List<GameObject>();
        // tooltip
        RectTransform _tip; Text _tipName, _tipType, _tipKeys, _tipVals, _tipWeight, _tipPrice, _tipHint; Image _tipStrip, _tipDiv;
        object _hoverKey; float _hoverT; bool _tipShown; float _tipH;

        ItemInstance _selected;
        // drag state
        ItemInstance _pressItem; Loc _pressLoc; Vector2 _pressPos; bool _pressed, _dragging, _dragRot; int _grabX, _grabY;
        RectTransform _dragVisual, _dragSrcTile; SlotView _dragSrcSlot;
        float _tick;

        static readonly string[] BodyGlyphs = { "body_Head", "body_Thorax", "body_Stomach", "body_LeftArm", "body_RightArm", "body_LeftLeg", "body_RightLeg" };
        const float WeightLimit = 30f;

        static PlayerProfile P => SaveSystem.Current;
        // in a raid: the spawned player's loadout (the tutorial uses its own empty one)
        static Loadout L => PlayerController.Local != null && PlayerController.Local.loadout != null ? PlayerController.Local.loadout : SaveSystem.Current.loadout;
        static PlayerController Player => PlayerController.Local;

        public InventoryUI(UIManager ui, Transform parent)
        {
            _ui = ui;
            root = UIFactory.Stretch(UIFactory.Node(parent, "Inventory"));
            BuildBackdrop();
            // top bar contents stick to the top edge; the 1920x1080 body is centred, so the extra height of
            // 16:10 / 5:4 screens is split evenly above and below it (ultrawide: extra width on both sides)
            _top = UIFactory.Node(root, "Top");
            _top.anchorMin = _top.anchorMax = new Vector2(0.5f, 1f); _top.pivot = new Vector2(0.5f, 1f);
            _top.anchoredPosition = Vector2.zero; _top.sizeDelta = new Vector2(1920f, 64f);
            _frame = UIFactory.Node(root, "Frame");
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f); _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.anchoredPosition = Vector2.zero; _frame.sizeDelta = new Vector2(1920f, 1080f);
            BuildTopBar();
            BuildEquipment();
            BuildGrids();
            BuildRaidColumn();
            BuildStashColumn();
            BuildTrader();
            BuildInfo();
            _dragLayer = UIFactory.Stretch(UIFactory.Node(root, "DragLayer"));
            BuildTooltip();
            foreach (var g in _grids) g.selected = () => _selected;
            root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ build: backdrop, top bar
        void BuildBackdrop()
        {
            _bgBlock = UIFactory.Img(root, "bg", UITheme.A(UITheme.Bg0, 0.9f), true); UIFactory.Stretch(_bgBlock.rectTransform);
            _bgArt = UIFactory.Stretch(UIFactory.Node(root, "art"));
            var tex = GameAssets.MenuBackground;
            if (tex != null) UIFactory.Cover(_bgArt, tex, "cover", 0.6f, 0.5f);
            var dim = UIFactory.Img(_bgArt, "dim", UITheme.A(UITheme.Bg0, 0.9f)); UIFactory.Stretch(dim.rectTransform);
            var vig = UIFactory.Img(root, "vignette", UITheme.A(Color.black, 0.5f)); UIFactory.SetSoft(vig, "vign", vig.color); UIFactory.Stretch(vig.rectTransform);
            // top bar spans the whole screen width; its contents live in the frame
            var bar = UIFactory.Img(root, "TopBar", UITheme.A(UITheme.Bg1, 0.97f));
            var brt = bar.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
            brt.sizeDelta = new Vector2(0f, 64f); brt.anchoredPosition = Vector2.zero;
            var line = UIFactory.Img(bar.transform, "line", UITheme.Line1); var lr = line.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1f, 0f); lr.pivot = new Vector2(0.5f, 0f); lr.sizeDelta = new Vector2(0f, 1f); lr.anchoredPosition = Vector2.zero;
            var sh = UIFactory.Img(bar.transform, "shadow", UITheme.A(Color.black, 0.45f)); UIFactory.SetSoft(sh, "fdown", sh.color); var sr = sh.rectTransform;
            sr.anchorMin = Vector2.zero; sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 1f); sr.sizeDelta = new Vector2(0f, 16f); sr.anchoredPosition = Vector2.zero;
        }

        void BuildTopBar()
        {
            var f = _top;
            // raid: title (left), map + raid clock (right)
            _raidTitle = UIFactory.Node(f, "RaidTitle"); UIFactory.Place(_raidTitle, 0, 0, 900, 64);
            UIFactory.Box(_raidTitle, "tick", 32, 22, 3, 20, UITheme.Amber);
            UIFactory.Icon(_raidTitle, "backpack", 48, 20, 24, UITheme.Text1, "icon");
            var tt = UIFactory.T(_raidTitle, "ИНВЕНТАРЬ", 84, 0, 400, 64, UITheme.Display, 26, UITheme.Text0, TextAnchor.MiddleLeft, "Title");
            UIFactory.Track(tt, 2.4f);
            _raidInfo = UIFactory.Node(f, "RaidInfo"); UIFactory.Place(_raidInfo, 900, 0, 660, 64);
            _raidMap = UIFactory.T(_raidInfo, "", 0, 0, 528, 64, UITheme.BodyMedium, 17, UITheme.Text1, TextAnchor.MiddleRight, "map");
            UIFactory.Box(_raidInfo, "sep", 544, 22, 1, 20, UITheme.Line1);
            UIFactory.Icon(_raidInfo, "clock", 558, 23, 18, UITheme.Text2, "clockIcon");
            _raidClock = UIFactory.Mono(_raidInfo, "", 576, 0, 84, 64, 22, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            _raidClock.name = "RaidClock";

            // stash / trader: tabs (left), money + level (right)
            _tabsRoot = UIFactory.Node(f, "Tabs"); UIFactory.Place(_tabsRoot, 0, 0, 900, 64);
            _tabGear = UIFactory.Button(_tabsRoot, "СНАРЯЖЕНИЕ", 32, 0, 236, 64, () => { if (mode != Mode.Stash) _ui.OpenStash(false); }, BtnStyle.Tab, "backpack", 18);
            _tabTrader = UIFactory.Button(_tabsRoot, "СКУПЩИК", 272, 0, 200, 64, () => { if (mode != Mode.Trader) _ui.OpenStash(true); }, BtnStyle.Tab, "rouble", 18);
            _moneyRoot = UIFactory.Node(f, "Money"); UIFactory.Place(_moneyRoot, 900, 0, 660, 64);
            _money = UIFactory.Mono(_moneyRoot, "", 0, 0, 560, 64, 22, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            _money.name = "Money";
            _levelChip = UIFactory.Chip(_moneyRoot, "ур. 1", 580, 21, UITheme.Amber, "star", 22);

            UIFactory.KeyHint(f, "ESC", "закрыть", 1584, 19, 26, 14, UITheme.Text2);
            UIFactory.Button(f, "ЗАКРЫТЬ", 1708, 12, 180, 40, () => _ui.CloseInventory(), BtnStyle.Secondary, "close", 16);
        }

        // ------------------------------------------------------------------ build: equipment (slots, health, totals)
        void BuildEquipment()
        {
            _equipPanel = UIFactory.Stretch(UIFactory.Node(_frame, "Equip"));
            var e = _equipPanel;
            UIFactory.Header(e, "Снаряжение", 32, 72, 520);
            _slots = new[]
            {
                MakeSlot(e, EquipSlot.Primary, "Основное оружие", "rifle", 32, 112, 520, 168),
                MakeSlot(e, EquipSlot.Holster, "Пистолет", "pistol", 32, 292, 254, 132),
                MakeSlot(e, EquipSlot.Helmet, "Шлем", "helmet", 298, 292, 254, 132),
                MakeSlot(e, EquipSlot.Armor, "Бронежилет", "vest", 32, 436, 254, 196),
            };
            BuildHealth(e, 298, 436, 254, 364);
            BuildTotals(e, 32, 644, 254, 156);
        }

        SlotView MakeSlot(Transform parent, EquipSlot s, string title, string glyph, float x, float y, float w, float h)
        {
            var sv = new SlotView { slot = s, glyph = glyph, w = w, h = h };
            var bg = UIFactory.Box(parent, "Slot_" + s, x, y, w, h, UITheme.A(UITheme.Bg1, 0.94f));
            sv.rt = bg.rectTransform; sv.fill = bg;
            var t = bg.transform;
            UIFactory.Icon(t, glyph, 12, 8, 14, UITheme.Text2, "glyph");
            UIFactory.Caps(t, title, 32, 0, w - 44, 30, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            sv.chip = UIFactory.Chip(t, "", w - 60, 6, UITheme.Text1, null, 18);
            sv.chip.gameObject.SetActive(false);
            float ww = w - 16, wh = h - 38;
            var well = UIFactory.Box(t, "well", 8, 30, ww, wh, UITheme.A(UITheme.Bg0, 0.6f));
            var wt = well.transform;
            UIFactory.Pattern(wt, "dots", UIAssets.Dots, UITheme.A(Color.white, 0.03f));
            UIFactory.Border(wt, UITheme.A(UITheme.Line0, 0.9f), "wellEdge");
            sv.glow = UIFactory.Img(wt, "glow", new Color(0f, 0f, 0f, 0f)); sv.glow.sprite = UIAssets.Radial;
            UIFactory.Stretch(sv.glow.rectTransform, ww * 0.12f, 0, ww * 0.12f, 0);
            float es = Mathf.Min(44f, wh * 0.4f);
            sv.empty = UIFactory.Icon(wt, glyph, (ww - es) * 0.5f, (wh - es) * 0.5f - 9f, es, UITheme.A(UITheme.Text3, 0.9f), "emptyGlyph");
            sv.emptyText = UIFactory.Caps(wt, "пусто", 0, (wh + es) * 0.5f - 6f, ww, 18, 11, UITheme.Text3, TextAnchor.MiddleCenter, 2f);
            sv.icon = ItemVisual.FitIcon(wt, "icon", null, null, 8, 5, ww - 16, wh - 10);
            UIFactory.DropShadow(sv.icon, 0.5f, 2f);
            sv.foot = UIFactory.Gradient(wt, "foot", 1, 36, UITheme.A(Color.black, 0.62f));
            sv.name = UIFactory.T(wt, "", 10, wh - 27, ww - 20, 22, UITheme.BodySemi, 15, UITheme.Text0, TextAnchor.MiddleLeft, "name");
            sv.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.DropShadow(sv.name, 0.8f, 1f);
            sv.info = UIFactory.Caps(wt, "", 10, wh - 27, ww - 20, 22, 11, UITheme.Text1, TextAnchor.MiddleRight, 1.2f);
            UIFactory.DropShadow(sv.info, 0.8f, 1f);
            sv.cond = UIFactory.Bar(wt, 10, wh - 5, ww - 20, 2, UITheme.Green);
            sv.cond.transform.parent.GetComponent<Image>().color = UITheme.A(Color.black, 0.55f);
            sv.edge = UIFactory.Border(t, UITheme.Line1, "edge");
            sv.accept = UIFactory.Img(t, "accept", UITheme.A(UITheme.Green, 0.06f)); UIFactory.Stretch(sv.accept.rectTransform);
            var ae = UIFactory.Sliced(sv.accept.transform, "edge", UIAssets.Frame2, UITheme.A(UITheme.Green, 0.55f)); UIFactory.Stretch(ae.rectTransform);
            sv.accept.gameObject.SetActive(false);
            sv.sel = UIFactory.Sliced(t, "sel", UIAssets.Frame2, UITheme.Amber); UIFactory.Stretch(sv.sel.rectTransform);
            sv.sel.gameObject.SetActive(false);
            return sv;
        }

        void BuildHealth(Transform parent, float x, float y, float w, float h)
        {
            _healthPanel = UIFactory.PanelBox(parent, "Health", x, y, w, h, 0.94f);
            var t = _healthPanel;
            UIFactory.Icon(t, "heart", 16, 13, 14, UITheme.Text2, "hIcon");
            UIFactory.Caps(t, "здоровье", 36, 8, 140, 24, 12, UITheme.Text1, TextAnchor.MiddleLeft, 2f);
            _hpTotal = UIFactory.Mono(t, "", 16, 8, w - 32, 24, 13, UITheme.Text0, TextAnchor.MiddleRight, true);
            UIFactory.Box(t, "line", 16, 38, w - 32, 1, UITheme.Line0);
            var body = UIFactory.Node(t, "body"); UIFactory.Place(body, 14, 54, 92, 184);
            var shade = UIFactory.Img(body, "shade", UITheme.A(UITheme.Bg0, 0.7f)); UIFactory.SetSoft(shade, "radial", shade.color); UIFactory.Stretch(shade.rectTransform, -24, -16, -24, -16);
            var outline = UIFactory.Img(body, "outline", UITheme.A(Color.black, 0.75f)); UIFactory.Stretch(outline.rectTransform, -1, -1, -1, -1);
            UIFactory.SetIcon(outline, "body_Outline");
            for (int i = 0; i < Health.Parts; i++)
            {
                var im = UIFactory.Img(body, BodyGlyphs[i], UITheme.Green); UIFactory.Stretch(im.rectTransform);
                UIFactory.SetIcon(im, BodyGlyphs[i]); _bodyParts[i] = im;
                float ry = 48 + i * 34;
                _hpNames[i] = UIFactory.T(t, Health.PartNames[i], 118, ry, 90, 18, UITheme.Body, 13, UITheme.Text1, TextAnchor.MiddleLeft, "part");
                _hpNames[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                float nx = 118 + UIFactory.TextWidth(_hpNames[i]) + 5;
                _hpDrops[i] = UIFactory.Icon(t, "drop", nx, ry + 3, 12, UITheme.Red, "bleed");
                _hpDrops[i].gameObject.SetActive(false);
                _hpTexts[i] = UIFactory.Mono(t, "", 118, ry, w - 118 - 16, 18, 12, UITheme.Text0, TextAnchor.MiddleRight);
                _hpBars[i] = UIFactory.Bar(t, 118, ry + 21, w - 118 - 16, 3, UITheme.Green);
                _hpBars[i].transform.parent.GetComponent<Image>().color = UITheme.A(Color.black, 0.5f);
            }
            for (int i = 0; i < 4; i++)
            {
                var c = UIFactory.Chip(t, "", 14, 296, UITheme.Red, "drop", 22); c.name = "Effect" + i;
                c.gameObject.SetActive(false); _hpChips.Add(c);
            }
            _healthNote = UIFactory.T(t, "Здоровье полностью восстанавливается между рейдами.", 16, 292, w - 32, 56, UITheme.Body, 14, UITheme.Text2, TextAnchor.UpperLeft, "note");
        }

        void BuildTotals(Transform parent, float x, float y, float w, float h)
        {
            var t = UIFactory.PanelBox(parent, "Totals", x, y, w, h, 0.94f);
            UIFactory.Icon(t, "weight", 16, 14, 14, UITheme.Text2, "wIcon");
            UIFactory.Caps(t, "вес снаряжения", 36, 8, 170, 26, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _overChip = UIFactory.Chip(t, "перегруз", 150, 10, UITheme.Red, "warn", 20);
            _overChip.gameObject.SetActive(false);
            _weightVal = UIFactory.Mono(t, "", 16, 36, w - 32, 30, 20, UITheme.Text0, TextAnchor.MiddleLeft, true);
            _weightBar = UIFactory.Bar(t, 16, 70, w - 32, 3, UITheme.Amber);
            _weightBar.transform.parent.GetComponent<Image>().color = UITheme.A(Color.black, 0.5f);
            UIFactory.Icon(t, "coins", 16, 92, 14, UITheme.Text2, "vIcon");
            UIFactory.Caps(t, "стоимость", 36, 86, 170, 26, 12, UITheme.Text2, TextAnchor.MiddleLeft, 1.8f);
            _valueVal = UIFactory.Mono(t, "", 16, 112, w - 32, 30, 20, UITheme.AmberHi, TextAnchor.MiddleLeft, true);
        }

        // ------------------------------------------------------------------ build: loadout grids + controls card
        void BuildGrids()
        {
            _middlePanel = UIFactory.Stretch(UIFactory.Node(_frame, "Grids"));
            _rig = new GridView(_middlePanel, "РАЗГРУЗКА", 584, 112, L.rig);
            _pockets = new GridView(_middlePanel, "КАРМАНЫ", 584, 300, L.pockets);
            _secure = new GridView(_middlePanel, "ПОДСУМОК", 872, 112, L.secure);
            _secure.minHeaderW = 272;
            _secure.info = g => UITheme.Tint("НЕ ТЕРЯЕТСЯ", UITheme.Green) + "   " + g.w + "×" + g.h;
            _backpack = new GridView(_middlePanel, "РЮКЗАК", 584, 424, L.backpack);
            _grids.Add(_rig); _grids.Add(_pockets); _grids.Add(_secure); _grids.Add(_backpack);
            BuildHints(_middlePanel, 936, 260, 208, 484);
        }

        void BuildHints(Transform parent, float x, float y, float w, float h)
        {
            _hintsCard = UIFactory.PanelBox(parent, "Hints", x, y, w, h, 0.9f);
            var t = _hintsCard;
            UIFactory.Caps(t, "управление", 16, 8, w - 32, 24, 12, UITheme.Text2, TextAnchor.MiddleLeft, 2f);
            UIFactory.Box(t, "line", 16, 36, w - 32, 1, UITheme.Line0);
            float ry = 50;
            void Row(string key, string key2, string text)
            {
                float kx = 16f + UIFactory.Keycap(t, key, 16, ry, 24, 11);
                if (key2 != null) { var plus = UIFactory.T(t, "+", kx, ry, 14, 24, UITheme.Body, 13, UITheme.Text3, TextAnchor.MiddleCenter, "plus"); kx += 14f; kx += UIFactory.Keycap(t, key2, kx, ry, 24, 11); }
                UIFactory.T(t, text, kx + 9, ry - 6, w - kx - 9 - 12, 36, UITheme.Body, 13, UITheme.Text1, TextAnchor.MiddleLeft, "hint");
                ry += 40;
            }
            Row("ЛКМ", null, "взять и переложить");
            Row("R", null, "повернуть при переносе");
            Row("CTRL", "ЛКМ", "быстро");
            Row("ПКМ", null, "использовать, разрядить");
            Row("ESC", null, "закрыть");
            ry += 8;
            UIFactory.Caps(t, "редкость", 16, ry, w - 32, 24, 12, UITheme.Text2, TextAnchor.MiddleLeft, 2f);
            UIFactory.Box(t, "line2", 16, ry + 28, w - 32, 1, UITheme.Line0);
            ry += 42;
            for (int r = 1; r <= 5; r++)
            {
                var sw = UIFactory.Box(t, "rar" + r, 16, ry + 1, 18, 18, UITheme.A(UITheme.Bg3, 0.95f));
                UIFactory.Border(sw.transform, r >= 3 ? UITheme.A(UITheme.Rarity(r), 0.6f) : UITheme.A(UITheme.Line2, 0.9f));
                if (r >= 2) { var n = UIFactory.Img(sw.transform, "notch", UITheme.Rarity(r)); n.sprite = UIAssets.CornerTL; UIFactory.Place(n.rectTransform, 1, 1, 9, 9); }
                UIFactory.T(t, UITheme.RarityName(r), 44, ry, w - 60, 20, UITheme.Body, 13, r >= 3 ? UITheme.Rarity(r) : UITheme.Text1, TextAnchor.MiddleLeft, "rarName");
                ry += 27;
            }
            ry += 10;
            UIFactory.T(t, "Патроны — перетащите на магазин, магазин — на оружие.", 16, ry, w - 32, 40, UITheme.Body, 12, UITheme.Text2, TextAnchor.UpperLeft, "tip");
        }

        // ------------------------------------------------------------------ build: raid column (container, ground)
        void BuildRaidColumn()
        {
            _rightRaidRoot = UIFactory.Stretch(UIFactory.Node(_frame, "RightRaid"));
            var r = _rightRaidRoot;
            _containerView = new GridView(r, "КОНТЕЙНЕР", 1176, 112, null);
            _containerView.minHeaderW = 712;
            _containerView.info = SearchInfo;
            _grids.Add(_containerView);
            _searchBar = UIFactory.Bar(r, 1176, 101, 712, 2, UITheme.Amber);
            _searchBarRoot = (RectTransform)_searchBar.transform.parent;
            _searchBarRoot.GetComponent<Image>().color = UITheme.A(UITheme.Line0, 0.8f);
            _searchBarRoot.gameObject.SetActive(false);

            _rightHint = UIFactory.Stretch(UIFactory.Node(r, "NoContainer"));
            UIFactory.Header(_rightHint, "Окружение", 1176, 72, 712);
            var card = UIFactory.PanelBox(_rightHint, "card", 1176, 112, 712, 128, 0.85f);
            UIFactory.Pattern(card, "hatch", UIAssets.Hatch, UITheme.A(UITheme.Line0, 0.45f));
            UIFactory.Icon(card, "search", 30, 40, 44, UITheme.Text3, "icon");
            UIFactory.T(card, "Контейнер не открыт", 100, 26, 580, 30, UITheme.BodySemi, 19, UITheme.Text1, TextAnchor.MiddleLeft, "title");
            float kx = 100f + UIFactory.Keycap(card, "F", 100, 66, 26, 13);
            UIFactory.T(card, "Подойдите к ящику или телу и нажмите F, чтобы обыскать.", kx + 10, 66, 560, 26, UITheme.Body, 15, UITheme.Text2, TextAnchor.MiddleLeft, "sub");

            BuildGround(r, 1176, 920, 712, 128);
        }

        void BuildGround(Transform parent, float x, float y, float w, float h)
        {
            var bg = UIFactory.Box(parent, "Ground", x, y, w, h, UITheme.A(UITheme.Bg1, 0.88f));
            _ground = bg.rectTransform;
            _groundHatch = UIFactory.Pattern(bg.transform, "hatch", UIAssets.Hatch, UITheme.A(UITheme.Line1, 0.4f));
            _groundEdge = UIFactory.Border(bg.transform, UITheme.Line1, "edge");
            _groundIcon = UIFactory.Icon(bg.transform, "ground", 30, (h - 38) * 0.5f, 38, UITheme.Text2, "icon");
            _groundText = UIFactory.Caps(bg.transform, "земля", 90, h * 0.5f - 28, w - 120, 28, 18, UITheme.Text1, TextAnchor.MiddleLeft, 2.6f);
            _groundSub = UIFactory.T(bg.transform, "", 90, h * 0.5f + 2, w - 120, 24, UITheme.Body, 15, UITheme.Text2, TextAnchor.MiddleLeft, "sub");
            SetGround(0);
        }

        /// <summary>Ground drop zone: 0 idle, 1 an item is being dragged, 2 the item is over the zone.</summary>
        void SetGround(int state)
        {
            if (_groundEdge == null || state == _groundState) return;
            _groundState = state;
            _groundEdge.color = state == 2 ? UITheme.AmberHi : state == 1 ? UITheme.A(UITheme.Amber, 0.55f) : UITheme.Line1;
            _groundHatch.color = state == 2 ? UITheme.A(UITheme.Amber, 0.28f) : state == 1 ? UITheme.A(UITheme.Amber, 0.13f) : UITheme.A(UITheme.Line1, 0.4f);
            _groundText.color = state > 0 ? UITheme.AmberHi : UITheme.Text1;
            _groundIcon.color = state > 0 ? UITheme.Amber : UITheme.Text2;
            _groundSub.text = state == 2 ? "Отпустите, чтобы выбросить" : "Перетащите предмет сюда, чтобы выбросить его";
            _groundSub.color = state == 2 ? UITheme.Text0 : UITheme.Text2;
        }

        // ------------------------------------------------------------------ build: stash column
        void BuildStashColumn()
        {
            _rightScrollRoot = UIFactory.Stretch(UIFactory.Node(_frame, "RightStash"));
            _stashInfo = UIFactory.Header(_rightScrollRoot, "Схрон", 1176, 72, 712);
            _rightScrollContent = UIFactory.ScrollView(_rightScrollRoot, 1176, 112, 712, 936, out _rightScroll);
            UIFactory.Border(_rightScroll.transform, UITheme.Line0, "edge");
            _right = new GridView(_rightScrollContent, "СХРОН", 30, 12, P.stash);
            _right.headerVisible = false;
            _right.clip = _rightScroll.viewport;
            _rightScrollContent.sizeDelta = new Vector2(0, P.stash.h * UIFactory.Cell + 24);
            _grids.Add(_right);
        }

        // ------------------------------------------------------------------ build: trader
        static readonly string[] CatNames = { "Всё", "Оружие", "Патроны", "Медицина", "Защита", "Провизия" };
        static readonly string[] CatGlyphs = { "grid", "rifle", "ammo3", "med", "vest", "can" };

        static int CatOf(ItemDef d)
        {
            switch (d.type)
            {
                case ItemType.Weapon: case ItemType.Magazine: return 1;
                case ItemType.Ammo: return 2;
                case ItemType.Medical: return 3;
                case ItemType.Armor: case ItemType.Helmet: return 4;
                case ItemType.Food: return 5;
                default: return 0;
            }
        }

        void BuildTrader()
        {
            _traderPanel = UIFactory.Stretch(UIFactory.Node(_frame, "Trader"));
            var t = _traderPanel;
            var hr = UIFactory.Header(t, "Скупщик  ·  покупка", 32, 72, 1112, "Продажа: перетащите предмет из схрона на список или Ctrl+клик");
            hr.font = UITheme.Body; hr.fontSize = 14; hr.color = UITheme.Text2;
            _catTabs = new Button[CatNames.Length];
            for (int i = 0; i < CatNames.Length; i++)
            {
                int k = i;
                _catTabs[i] = UIFactory.Button(t, CatNames[i].ToUpperInvariant(), 32 + i * 150, 112, 146, 40, () => SetCategory(k), BtnStyle.Tab, CatGlyphs[i], 15);
            }
            _offersCount = UIFactory.Mono(t, "", 32, 112, 1112, 40, 13, UITheme.Text2, TextAnchor.MiddleRight);
            UIFactory.Box(t, "tabLine", 32, 152, 1112, 1, UITheme.Line0);
            _offersContent = UIFactory.ScrollView(t, 32, 160, 1112, 640, out var offersScroll);
            _offersRoot = (RectTransform)offersScroll.transform;
            BuildOffers();

            // sell drop zone over the list (only while a stash item is dragged)
            _sellZone = UIFactory.Node(t, "SellZone"); UIFactory.Place(_sellZone, 32, 160, 1112, 640);
            var sbg = UIFactory.Img(_sellZone, "bg", UITheme.A(UITheme.Bg0, 0.9f)); UIFactory.Stretch(sbg.rectTransform);
            UIFactory.Pattern(_sellZone, "hatch", UIAssets.Hatch, UITheme.A(UITheme.Amber, 0.07f));
            _sellEdge = UIFactory.Sliced(_sellZone, "edge", UIAssets.Frame2, UITheme.A(UITheme.Amber, 0.55f)); UIFactory.Stretch(_sellEdge.rectTransform);
            UIFactory.Icon(_sellZone, "rouble", 556 - 30, 196, 60, UITheme.Amber, "icon");
            _sellTitle = UIFactory.Caps(_sellZone, "продать скупщику", 0, 270, 1112, 40, 26, UITheme.Text0, TextAnchor.MiddleCenter, 3f);
            _sellName = UIFactory.T(_sellZone, "", 0, 314, 1112, 28, UITheme.BodyMedium, 18, UITheme.Text1, TextAnchor.MiddleCenter, "name");
            _sellPrice = UIFactory.Mono(_sellZone, "", 0, 350, 1112, 48, 34, UITheme.AmberHi, TextAnchor.MiddleCenter, true);
            _sellZone.gameObject.SetActive(false);
            SetCategory(0);
        }

        void BuildOffers()
        {
            foreach (var id in Offers)
            {
                var d = ItemDB.Get(id); if (d == null) continue;
                var row = new OfferRow { d = d, cat = CatOf(d) };
                row.bg = UIFactory.Box(_offersContent, "Offer_" + id, 6, 0, 1082, 72, UITheme.A(UITheme.Bg2, 0.9f));
                row.rt = row.bg.rectTransform;
                var rt = row.rt;
                UIFactory.Border(rt, UITheme.Line0, "edge");
                row.accent = UIFactory.Box(rt, "accent", 0, 0, 2, 72, UITheme.A(UITheme.Amber, 0f));
                // icon plate
                int rar = Mathf.Clamp(d.rarity, 1, 5); var rc = UITheme.Rarity(rar);
                var plate = UIFactory.Box(rt, "plate", 10, 6, 124, 60, UITheme.A(ItemVisual.TypeColor(d), 0.92f));
                UIFactory.Gradient(plate.transform, "sheen", 0, 30, UITheme.A(Color.white, 0.05f));
                if (rar >= 3) { var g = UIFactory.Img(plate.transform, "glow", UITheme.A(rc, 0.16f)); UIFactory.SetSoft(g, "radial", g.color); UIFactory.Stretch(g.rectTransform, 10, 4, 10, 4); }
                var ic = ItemVisual.FitIcon(plate.transform, "icon", d, null, 6, 4, 112, 52);
                UIFactory.DropShadow(ic, 0.45f, 2f);
                UIFactory.Border(plate.transform, rar >= 3 ? UITheme.A(rc, 0.5f) : UITheme.A(UITheme.Line2, 0.8f), "plateEdge");
                if (rar >= 2) { var n = UIFactory.Img(plate.transform, "rarity", UITheme.A(rc, 0.95f)); n.sprite = UIAssets.CornerTL; UIFactory.Place(n.rectTransform, 1, 1, 9, 9); }
                // name, details, price, buy
                int pack = PackSize(d);
                var nm = UIFactory.T(rt, d.name + (pack > 1 ? "  " + UITheme.Tint("×" + pack, UITheme.AmberHi) : ""), 152, 9, 540, 28, UITheme.BodySemi, 18, UITheme.Text0, TextAnchor.MiddleLeft, "name");
                nm.horizontalOverflow = HorizontalWrapMode.Overflow;
                var sub = UIFactory.T(rt, UITheme.TypeName(d.type) + "   ·   " + d.w + "×" + d.h + "   ·   " + d.weight.ToString("0.##") + " кг" + (string.IsNullOrEmpty(d.desc) ? "" : "   ·   " + d.desc),
                    152, 38, 540, 22, UITheme.Body, 14, UITheme.Text2, TextAnchor.MiddleLeft, "sub");
                sub.horizontalOverflow = HorizontalWrapMode.Overflow;
                ItemVisual.FitText(sub, 540, 13);
                row.price = UIFactory.Mono(rt, UITheme.Rub(BuyPrice(d)), 700, 9, 208, 32, 21, UITheme.AmberHi, TextAnchor.MiddleRight, true);
                row.price.name = "price";
                UIFactory.T(rt, "за " + pack + " шт.", 700, 40, 208, 20, UITheme.Body, 13, UITheme.Text2, TextAnchor.MiddleRight, "per");
                string cid = id;
                UIFactory.Button(rt, "КУПИТЬ", 926, 14, 144, 44, () => Buy(cid), BtnStyle.Primary, "cart", 16);
                _offerRows.Add(row);
            }
        }

        void SetCategory(int k)
        {
            _cat = k;
            for (int i = 0; i < _catTabs.Length; i++) UIFactory.SetSelected(_catTabs[i], i == k);
            float y = 6f; int n = 0;
            foreach (var r in _offerRows)
            {
                bool on = k == 0 || r.cat == k;
                r.rt.gameObject.SetActive(on);
                if (!on) continue;
                r.rt.anchoredPosition = new Vector2(6f, -y);
                y += 78f; n++;
            }
            _offersContent.sizeDelta = new Vector2(0f, y);
            _offersContent.anchoredPosition = Vector2.zero;
            _offersCount.text = n + " " + Plural(n, "позиция", "позиции", "позиций");
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }

        OfferRow OfferAt(Vector2 mp)
        {
            if (mode != Mode.Trader || !UIFactory.Contains(_offersRoot, mp)) return null;
            foreach (var r in _offerRows) if (r.rt.gameObject.activeSelf && UIFactory.Contains(r.rt, mp)) return r;
            return null;
        }

        void SetOfferHover(OfferRow r)
        {
            if (r == _hoverOffer) return;
            if (_hoverOffer != null) { _hoverOffer.bg.color = UITheme.A(UITheme.Bg2, 0.9f); _hoverOffer.accent.color = UITheme.A(UITheme.Amber, 0f); }
            _hoverOffer = r;
            if (r != null) { r.bg.color = UITheme.A(UITheme.Bg3, 0.96f); r.accent.color = UITheme.Amber; }
        }

        // ------------------------------------------------------------------ build: item card, tooltip
        void BuildInfo()
        {
            var t = UIFactory.PanelBox(_frame, "Info", 32, 812, 1112, 236, 0.95f);
            var prev = UIFactory.Box(t, "preview", 16, 16, 204, 204, UITheme.A(UITheme.Bg0, 0.72f));
            UIFactory.Pattern(prev.transform, "dots", UIAssets.Dots, UITheme.A(Color.white, 0.03f));
            _infoGlow = UIFactory.Img(prev.transform, "glow", new Color(0f, 0f, 0f, 0f)); _infoGlow.sprite = UIAssets.Radial;
            UIFactory.Stretch(_infoGlow.rectTransform, 8, 8, 8, 8);
            _infoIcon = ItemVisual.FitIcon(prev.transform, "icon", null, null, 14, 14, 176, 176);
            UIFactory.DropShadow(_infoIcon, 0.5f, 3f);
            _infoEdge = UIFactory.Border(prev.transform, UITheme.Line1, "edge");
            _infoNotch = UIFactory.Img(prev.transform, "rarity", new Color(0f, 0f, 0f, 0f)); _infoNotch.sprite = UIAssets.CornerTL;
            UIFactory.Place(_infoNotch.rectTransform, 1, 1, 14, 14);

            _infoTitle = UIFactory.T(t, "", 240, 12, 620, 36, UITheme.Display, 26, UITheme.Text0, TextAnchor.MiddleLeft, "title");
            _infoTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            _infoPrice = UIFactory.Mono(t, "", 840, 12, 256, 36, 24, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            _infoPriceSub = UIFactory.T(t, "", 700, 50, 396, 20, UITheme.Body, 14, UITheme.Text2, TextAnchor.MiddleRight, "priceSub");
            _infoChips = UIFactory.Node(t, "chips"); UIFactory.Place(_infoChips, 240, 50, 600, 22);
            _infoBody = UIFactory.T(t, "", 240, 82, 856, 44, UITheme.Body, 15, UITheme.Text1, TextAnchor.UpperLeft, "body");
            _infoStats = UIFactory.Node(t, "stats"); UIFactory.Place(_infoStats, 240, 128, 856, 46);
            _actions = UIFactory.Node(t, "Actions"); UIFactory.Place(_actions, 240, 182, 856, 40);

            _hintsNormal = UIFactory.Node(t, "hintsNormal"); UIFactory.Place(_hintsNormal, 240, 188, 856, 28);
            float x = 0f;
            x += UIFactory.KeyHint(_hintsNormal, "ЛКМ", "перетащить", x, 0, 26, 14, UITheme.Text2) + 22f;
            x += UIFactory.KeyHint(_hintsNormal, "R", "повернуть", x, 0, 26, 14, UITheme.Text2) + 22f;
            x += UIFactory.Keycap(_hintsNormal, "CTRL", x, 0, 26, 12) + 4f;
            x += UIFactory.KeyHint(_hintsNormal, "ЛКМ", "быстро переложить", x, 0, 26, 14, UITheme.Text2) + 22f;
            UIFactory.KeyHint(_hintsNormal, "ПКМ", "использовать / разрядить", x, 0, 26, 14, UITheme.Text2);
            _hintsTrader = UIFactory.Node(t, "hintsTrader"); UIFactory.Place(_hintsTrader, 240, 188, 856, 28);
            x = 0f;
            x += UIFactory.Keycap(_hintsTrader, "CTRL", x, 0, 26, 12) + 4f;
            x += UIFactory.KeyHint(_hintsTrader, "ЛКМ", "быстрая продажа", x, 0, 26, 14, UITheme.Text2) + 22f;
            UIFactory.KeyHint(_hintsTrader, "ЛКМ", "перетащите из схрона на список — продать", x, 0, 26, 14, UITheme.Text2);
        }

        void BuildTooltip()
        {
            _tip = UIFactory.Node(root, "Tooltip"); UIFactory.Place(_tip, 0, 0, 320, 160);
            var cg = _tip.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
            var sh = UIFactory.SetSoft(UIFactory.Sliced(_tip, "shadow", UIAssets.Glow, UITheme.A(Color.black, 0.6f)), "glow", UITheme.A(Color.black, 0.6f)); UIFactory.Stretch(sh.rectTransform, -18, -14, -18, -22);
            var bg = UIFactory.Img(_tip, "bg", UITheme.A(UITheme.Bg1, 0.98f)); UIFactory.Stretch(bg.rectTransform);
            UIFactory.Border(_tip, UITheme.Line2, "edge");
            _tipStrip = UIFactory.Img(_tip, "strip", UITheme.Amber);
            var srt = _tipStrip.rectTransform; srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f); srt.sizeDelta = new Vector2(0f, 2f); srt.anchoredPosition = Vector2.zero;
            _tipName = UIFactory.T(_tip, "", 14, 12, 292, 24, UITheme.BodySemi, 17, UITheme.Text0, TextAnchor.UpperLeft, "name");
            _tipType = UIFactory.Caps(_tip, "", 14, 38, 292, 16, 11, UITheme.Text2, TextAnchor.MiddleLeft, 1.4f);
            _tipKeys = UIFactory.T(_tip, "", 14, 62, 170, 100, UITheme.Body, 14, UITheme.Text2, TextAnchor.UpperLeft, "keys");
            _tipVals = UIFactory.T(_tip, "", 110, 62, 196, 100, UITheme.BodyMedium, 14, UITheme.Text0, TextAnchor.UpperRight, "vals");
            _tipDiv = UIFactory.Box(_tip, "div", 14, 0, 292, 1, UITheme.Line0);
            _tipWeight = UIFactory.T(_tip, "", 14, 0, 150, 22, UITheme.Body, 14, UITheme.Text1, TextAnchor.MiddleLeft, "weight");
            _tipPrice = UIFactory.Mono(_tip, "", 110, 0, 196, 22, 15, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            _tipHint = UIFactory.T(_tip, "", 14, 0, 292, 18, UITheme.Body, 12, UITheme.Text2, TextAnchor.MiddleLeft, "hint");
            _tip.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ open / close
        public void Open(Mode m, LootContainer c)
        {
            mode = m; container = m == Mode.Raid ? c : null;
            if (container != null) container.EnsureGenerated();
            _rig.Bind(L.rig); _pockets.Bind(L.pockets); _secure.Bind(L.secure); _backpack.Bind(L.backpack);
            _right.Bind(P.stash);
            _rightScrollContent.sizeDelta = new Vector2(0, P.stash.h * UIFactory.Cell + 24);
            _containerView.Bind(container != null ? container.grid : null, container);
            if (container != null) _containerView.header.text = container.title.ToUpperInvariant();
            bool trader = m == Mode.Trader, raid = m == Mode.Raid;
            _equipPanel.gameObject.SetActive(!trader);
            _middlePanel.gameObject.SetActive(!trader);
            _traderPanel.gameObject.SetActive(trader);
            _rightRaidRoot.gameObject.SetActive(raid);
            _rightScrollRoot.gameObject.SetActive(!raid);
            _rightHint.gameObject.SetActive(raid && container == null);
            // health: live in a raid, "restored between raids" at the base
            bool live = raid && Player != null && Player.health != null;
            _healthNote.gameObject.SetActive(!live);
            _hpSig = int.MinValue;
            if (!live) ShowRestedHealth();
            // top bar + backdrop
            _raidTitle.gameObject.SetActive(raid); _raidInfo.gameObject.SetActive(raid);
            _tabsRoot.gameObject.SetActive(!raid); _moneyRoot.gameObject.SetActive(!raid);
            UIFactory.SetSelected(_tabGear, m == Mode.Stash); UIFactory.SetSelected(_tabTrader, trader);
            _bgArt.gameObject.SetActive(!raid);
            _bgBlock.color = raid ? UITheme.A(UITheme.Bg0, 0.9f) : UITheme.Bg0;
            _raidMap.text = raid && RaidManager.I != null && RaidManager.I.map != null ? RaidManager.I.map.title : "";
            _clockShown = -1;
            if (trader) SetCategory(_cat);
            _selected = null; CancelDrag(); HideTip(); _hoverKey = null; SetOfferHover(null);
            root.gameObject.SetActive(true);
            MarkAll(); UpdateInfo(); UpdateStatic();
        }

        public void Close()
        {
            CancelDrag(); HideTip();
            root.gameObject.SetActive(false);
            if (mode != Mode.Raid) SaveSystem.Save();
            container = null;
            _containerView.Bind(null);
        }

        void MarkAll() { foreach (var g in _grids) g.dirty = true; }

        // ------------------------------------------------------------------ per frame
        public void Tick()
        {
            if (!IsOpen) return;
            var mp = GameInput.MousePosition();
            if (container != null && !container.SearchDone)
            {
                container.TickSearch(Time.deltaTime);
                _tick += Time.deltaTime; if (_tick > 0.2f) { _tick = 0f; _containerView.dirty = true; }
            }
            UpdateSearch();

            if (GameInput.MouseDown(0))
            {
                var it = HitItem(mp, out var loc, out int cx, out int cy);
                if (it != null)
                {
                    if (GameInput.CtrlHeld()) { QuickMove(it, loc); }
                    else
                    {
                        _pressed = true; _pressItem = it; _pressLoc = loc; _pressPos = mp;
                        _grabX = loc.isSlot ? 0 : Mathf.Clamp(cx - it.x, 0, it.W - 1); _grabY = loc.isSlot ? 0 : Mathf.Clamp(cy - it.y, 0, it.H - 1);
                        Select(it);
                    }
                }
                else if (OverAnyGrid(mp)) Select(null);
            }
            if (_pressed && !_dragging && GameInput.MouseHeld(0) && (mp - _pressPos).sqrMagnitude > 64f) BeginDrag();
            if (_dragging)
            {
                if (GameInput.Down(GKey.Rotate)) { _dragRot = !_dragRot; int t = _grabX; _grabX = _grabY; _grabY = t; RebuildDragVisual(); }
                MoveDragVisual(mp);
                UpdateGhost(mp);
            }
            if (GameInput.MouseUp(0))
            {
                if (_dragging) Drop(mp);
                CancelDrag();
            }
            if (GameInput.MouseDown(1))
            {
                var it = HitItem(mp, out var loc, out _, out _);
                if (it != null) { Select(it); DefaultAction(it, loc); }
            }
            foreach (var g in _grids) g.Refresh();
            UpdateSlotStates(mp);
            UpdateHover(mp);
            UpdateDynamic();
        }

        bool OverAnyGrid(Vector2 mp) { foreach (var g in _grids) if (g.grid != null && g.Contains(mp)) return true; return false; }

        ItemInstance HitItem(Vector2 mp, out Loc loc, out int cx, out int cy)
        {
            loc = default; cx = cy = 0;
            foreach (var g in _grids)
            {
                if (g.grid == null || !g.Contains(mp)) continue;
                if (!g.CellAt(mp, out cx, out cy)) continue;
                var it = g.grid.ItemAt(cx, cy);
                if (it == null || (g.container != null && g.container.IsHidden(it))) return null;
                loc = Loc.Grid(g.grid); return it;
            }
            if (mode != Mode.Trader)
                foreach (var s in _slots)
                    if (UIFactory.Contains(s.rt, mp)) { var it = L.Get(s.slot); if (it != null) { loc = Loc.Slot(s.slot); return it; } }
            return null;
        }

        /// <summary>Search progress (bar under the header, "ОБЫСК… 3/8" in the header).</summary>
        void UpdateSearch()
        {
            bool searching = mode == Mode.Raid && container != null && container.grid != null && !container.SearchDone;
            if (_searchBarRoot.gameObject.activeSelf != searching) _searchBarRoot.gameObject.SetActive(searching);
            if (!searching) return;
            int tot = container.grid.items.Count, hid = 0;
            foreach (var it in container.grid.items) if (container.IsHidden(it)) hid++;
            UIFactory.SetBar(_searchBar, tot > 0 ? (tot - hid) / (float)tot : 1f);
        }

        string SearchInfo(InventoryGrid g)
        {
            if (container != null && g == container.grid && !container.SearchDone)
            {
                int tot = g.items.Count, hid = 0;
                foreach (var it in g.items) if (container.IsHidden(it)) hid++;
                return UITheme.Tint("ОБЫСК…  " + (tot - hid) + "/" + tot, UITheme.AmberHi);
            }
            float w = g.Weight();
            return g.w + "×" + g.h + (w >= 0.05f ? "  ·  " + w.ToString("0.0") + " кг" : "");
        }

        /// <summary>Slot frames: hover, drop targets while dragging (green = fits, red = not for this slot).</summary>
        void UpdateSlotStates(Vector2 mp)
        {
            if (_slots == null || mode == Mode.Trader) return;
            foreach (var s in _slots)
            {
                bool over = UIFactory.Contains(s.rt, mp);
                Color edge = UITheme.Line1; bool accept = false;
                if (_dragging && _pressItem != null)
                {
                    bool ok = Loadout.Accepts(s.slot, _pressItem.Def);
                    accept = ok && !(_pressLoc.isSlot && _pressLoc.slot == s.slot);
                    edge = over ? (ok ? UITheme.Green : UITheme.Red) : accept ? UITheme.A(UITheme.Green, 0.45f) : UITheme.Line1;
                }
                else if (over && L.Get(s.slot) != null) edge = UITheme.Line2;
                if (s.edge.color != edge) s.edge.color = edge;
                if (s.accept.gameObject.activeSelf != accept) s.accept.gameObject.SetActive(accept);
            }
        }

        // ------------------------------------------------------------------ hover: highlight + tooltip
        void UpdateHover(Vector2 mp)
        {
            ItemInstance hit = null; Loc loc = default; OfferRow offer = null;
            if (!_dragging)
            {
                hit = HitItem(mp, out loc, out _, out _);
                if (hit == null) offer = OfferAt(mp);
            }
            foreach (var g in _grids) g.SetHover(hit != null && !loc.isSlot && g.grid == loc.grid ? hit : null);
            SetOfferHover(offer);
            object key = hit != null ? (object)hit : offer;
            if (!ReferenceEquals(key, _hoverKey)) { _hoverKey = key; _hoverT = 0f; HideTip(); }
            if (key == null) return;
            if (_dragging || GameInput.MouseHeld(0) || GameInput.MouseHeld(1)) { _hoverT = 0f; HideTip(); return; }
            _hoverT += Time.unscaledDeltaTime;
            if (!_tipShown && _hoverT >= 0.35f)
            {
                if (hit != null) ShowTip(hit.Def, hit, false); else ShowTip(offer.d, null, true);
            }
            if (_tipShown) PositionTip(mp);
        }

        void ShowTip(ItemDef d, ItemInstance it, bool offer)
        {
            if (d == null) return;
            int rar = Mathf.Clamp(d.rarity, 1, 5); var rc = UITheme.Rarity(rar);
            int pack = offer ? PackSize(d) : 1;
            _tipStrip.color = rar >= 2 ? rc : UITheme.Line2;
            _tipName.fontSize = 17;
            _tipName.text = d.name + (offer && pack > 1 ? "  ×" + pack : it != null && d.Stackable && it.count > 1 ? "  ×" + it.count : "");
            ItemVisual.FitText(_tipName, 292, 15);
            _tipType.text = UITheme.TypeName(d.type).ToUpperInvariant() + "   ·   " + UITheme.Tint(UITheme.RarityName(rar).ToUpperInvariant(), rar >= 2 ? rc : UITheme.Text1);

            var st = Stats(d, it);
            var ks = new System.Text.StringBuilder(); var vs = new System.Text.StringBuilder();
            for (int i = 0; i < st.Count; i++)
            {
                if (i > 0) { ks.Append('\n'); vs.Append('\n'); }
                ks.Append(st[i].k); vs.Append(UITheme.Tint(st[i].v, st[i].c));
            }
            _tipKeys.text = ks.ToString(); _tipVals.text = vs.ToString();
            _tipKeys.gameObject.SetActive(st.Count > 0); _tipVals.gameObject.SetActive(st.Count > 0);
            float y = 62f;
            if (st.Count > 0)
            {
                float sh = Mathf.Max(_tipKeys.preferredHeight, _tipVals.preferredHeight, 18f);
                UIFactory.Place(_tipKeys.rectTransform, 14, y, 180, sh + 2);
                UIFactory.Place(_tipVals.rectTransform, 110, y, 196, sh + 2);
                y += sh + 12f;
            }
            else y += 2f;
            UIFactory.Place(_tipDiv.rectTransform, 14, y, 292, 1);
            y += 9f;
            float weight = it != null ? it.Weight : d.weight * pack;
            _tipWeight.text = Kg(weight);
            UIFactory.Place(_tipWeight.rectTransform, 14, y, 150, 22);
            bool sell = !offer && it != null && mode == Mode.Trader && P.stash.items.Contains(it);
            _tipPrice.text = UITheme.Rub(offer ? BuyPrice(d) : sell ? SellPrice(it) : it != null ? it.Price : d.price);
            UIFactory.Place(_tipPrice.rectTransform, 110, y, 196, 22);
            y += 27f;
            string hint = TipHint(d, it, offer);
            bool hasHint = !string.IsNullOrEmpty(hint);
            _tipHint.gameObject.SetActive(hasHint);
            if (hasHint) { _tipHint.text = hint; UIFactory.Place(_tipHint.rectTransform, 14, y, 292, 18); y += 22f; }
            _tipH = y + 8f;
            UIFactory.Place(_tip, 0, 0, 320, _tipH);
            _tip.SetAsLastSibling();
            if (_tip.GetComponent<UIFadeIn>() == null) { var f = _tip.gameObject.AddComponent<UIFadeIn>(); f.duration = 0.12f; }
            _tip.gameObject.SetActive(true);
            _tipShown = true;
        }

        string TipHint(ItemDef d, ItemInstance it, bool offer)
        {
            if (offer) return "Кнопка «Купить» — товар попадёт в схрон";
            if (it == null) return null;
            var loc = Find(it);
            if (mode == Mode.Trader) return loc.grid == P.stash ? "Скупщик даст столько  ·  Ctrl+клик — продать" : null;
            if (d.type == ItemType.Medical && mode == Mode.Raid && Player != null && !loc.isSlot) return "ПКМ — использовать";
            if ((d.type == ItemType.Magazine || d.type == ItemType.Weapon) && it.ammo > 0) return "ПКМ — разрядить";
            if (d.type == ItemType.Ammo) return "Перетащите на магазин, чтобы снарядить";
            if (d.type == ItemType.Magazine) return "Перетащите на оружие, чтобы вставить";
            return "Ctrl+клик — быстро переложить";
        }

        void PositionTip(Vector2 mp)
        {
            var p = UIFactory.LocalTopLeft(root, mp);
            var size = root.rect.size;
            float w = 320f, h = _tipH;
            float x = p.x + 18f, y = p.y + 20f;
            if (x + w > size.x - 6f) x = p.x - 14f - w;
            if (y + h > size.y - 6f) y = p.y - 12f - h;
            x = Mathf.Clamp(x, 6f, Mathf.Max(6f, size.x - 6f - w));
            y = Mathf.Clamp(y, 6f, Mathf.Max(6f, size.y - 6f - h));
            _tip.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
        }

        void HideTip()
        {
            _tipShown = false;
            if (_tip != null && _tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
        }

        static string Kg(float w) => (w < 0.095f ? w.ToString("0.0##") : w.ToString("0.0#")) + " кг";

        // ------------------------------------------------------------------ drag & drop
        void BeginDrag()
        {
            _dragging = true; _dragRot = _pressItem.rot;
            HideTip();
            SFX.Play2D(SFX.ItemPick, 0.45f, 0.06f);
            RebuildDragVisual();
            DimSource(true);
        }

        /// <summary>The dragged item's own tile / slot icon is faded while it is carried.</summary>
        void DimSource(bool on)
        {
            if (!on)
            {
                if (_dragSrcTile != null) { var c0 = _dragSrcTile.GetComponent<CanvasGroup>(); if (c0 != null) c0.alpha = 1f; }
                _dragSrcTile = null;
                if (_dragSrcSlot != null) { UpdateSlot(_dragSrcSlot); _dragSrcSlot = null; }
                return;
            }
            if (_pressItem == null) return;
            if (_pressLoc.isSlot)
            {
                foreach (var s in _slots) if (s.slot == _pressLoc.slot) { _dragSrcSlot = s; s.icon.color = UITheme.A(s.icon.color, 0.3f); }
                return;
            }
            foreach (var g in _grids)
                if (g.grid != null && g.grid == _pressLoc.grid) { _dragSrcTile = g.TileOf(_pressItem); break; }
            if (_dragSrcTile != null)
            {
                var cg = _dragSrcTile.GetComponent<CanvasGroup>(); if (cg == null) cg = _dragSrcTile.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0.35f;
            }
        }

        void RebuildDragVisual()
        {
            if (_dragVisual != null) Object.Destroy(_dragVisual.gameObject);
            var d = _pressItem.Def; int w = _dragRot ? d.h : d.w, h = _dragRot ? d.w : d.h;
            var holder = UIFactory.Node(_dragLayer, "DragItem");
            UIFactory.Place(holder, 0, 0, w * UIFactory.Cell, h * UIFactory.Cell);
            var sh = UIFactory.SetSoft(UIFactory.Sliced(holder, "shadow", UIAssets.Glow, UITheme.A(Color.black, 0.6f)), "glow", UITheme.A(Color.black, 0.6f)); UIFactory.Stretch(sh.rectTransform, -16, -12, -16, -22);
            ItemVisual.Make(holder, _pressItem, 0, 0, w, h, false, true);
            foreach (var g in holder.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            var cg = holder.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0.9f; cg.blocksRaycasts = false; cg.interactable = false;
            _dragVisual = holder;
        }

        void MoveDragVisual(Vector2 mp)
        {
            if (_dragVisual == null) return;
            var p = UIFactory.LocalTopLeft(_dragLayer, mp);
            _dragVisual.anchoredPosition = new Vector2(p.x - (_grabX + 0.5f) * UIFactory.Cell, -(p.y - (_grabY + 0.5f) * UIFactory.Cell));
        }

        void CancelDrag()
        {
            bool was = _dragging;
            _pressed = false; _dragging = false;
            if (was) DimSource(false);
            if (_dragVisual != null) Object.Destroy(_dragVisual.gameObject);
            _dragVisual = null;
            foreach (var g in _grids) g.HideGhost();
            if (_sellZone != null && _sellZone.gameObject.activeSelf) _sellZone.gameObject.SetActive(false);
            SetGround(0);
            if (_slots != null)
                foreach (var s in _slots)
                {
                    s.edge.color = UITheme.Line1;
                    if (s.accept.gameObject.activeSelf) s.accept.gameObject.SetActive(false);
                }
        }

        int DW => _dragRot ? _pressItem.Def.h : _pressItem.Def.w;
        int DH => _dragRot ? _pressItem.Def.w : _pressItem.Def.h;

        /// <summary>Drop previews: grid ghost (green / red / amber = combine), ground zone (raid), sell zone (trader).</summary>
        void UpdateGhost(Vector2 mp)
        {
            foreach (var g in _grids)
            {
                if (g.grid == null || !g.Contains(mp) || !g.CellAt(mp, out int cx, out int cy)) { g.HideGhost(); continue; }
                int tx = cx - _grabX, ty = cy - _grabY;
                bool blocked = g.container != null && !g.container.SearchDone && g.grid != _pressLoc.grid;
                var other = g.grid.ItemAt(cx, cy);
                if (!blocked && other != null && other != _pressItem && CanCombine(_pressItem, other)) { g.ShowGhost(other.x, other.y, other.W, other.H, true, true); continue; }
                g.ShowGhost(tx, ty, DW, DH, !blocked && g.grid.Fits(tx, ty, DW, DH, _pressItem));
            }
            if (mode == Mode.Raid) SetGround(UIFactory.Contains(_ground, mp) ? 2 : 1);
            if (mode == Mode.Trader) UpdateSellZone(mp);
        }

        void UpdateSellZone(Vector2 mp)
        {
            bool can = _pressItem != null && !_pressLoc.isSlot && _pressLoc.grid == P.stash;
            if (_sellZone.gameObject.activeSelf != can) _sellZone.gameObject.SetActive(can);
            if (!can) return;
            bool over = UIFactory.Contains(_offersRoot, mp);
            _sellEdge.color = over ? UITheme.AmberHi : UITheme.A(UITheme.Amber, 0.45f);
            _sellTitle.text = over ? "ОТПУСТИТЕ — ПРОДАТЬ" : "ПРОДАТЬ СКУПЩИКУ";
            _sellTitle.color = over ? UITheme.AmberHi : UITheme.Text0;
            _sellName.text = _pressItem.Def.name + (_pressItem.count > 1 ? "  ×" + _pressItem.count : "");
            _sellPrice.text = UITheme.Rub(SellPrice(_pressItem));
        }

        void Drop(Vector2 mp)
        {
            var it = _pressItem; var from = _pressLoc;
            foreach (var s in _slots) s.edge.color = UITheme.Line1;
            if (mode != Mode.Trader)
                foreach (var s in _slots)
                    if (UIFactory.Contains(s.rt, mp)) { Equip(it, from, s.slot); SFX.Play2D(SFX.ItemDrop, 0.45f, 0.06f); return; }
            foreach (var g in _grids)
            {
                if (g.grid == null || !g.Contains(mp) || !g.CellAt(mp, out int cx, out int cy)) continue;
                if (g.container != null && !g.container.SearchDone && g.grid != from.grid) { Hud.Notify("Сначала дождитесь окончания поиска", 1.5f); return; }
                var other = g.grid.ItemAt(cx, cy);
                if (other != null && other != it && TryCombine(it, from, other)) { Changed(true); return; }
                MoveTo(it, from, g.grid, cx - _grabX, cy - _grabY, _dragRot);
                SFX.Play2D(SFX.ItemDrop, 0.45f, 0.06f);
                return;
            }
            if (mode == Mode.Raid && UIFactory.Contains(_ground, mp)) { DropToGround(it, from); return; }
            if (mode == Mode.Trader && UIFactory.Contains(_offersRoot, mp) && from.grid == P.stash) { Sell(it); return; }
        }

        void RemoveFrom(ItemInstance it, Loc l)
        {
            if (l.isSlot) L.Set(l.slot, null); else l.grid?.Remove(it);
        }

        void PutBack(ItemInstance it, Loc l, int ox, int oy, bool orot)
        {
            if (l.isSlot) { L.Set(l.slot, it); return; }
            if (l.grid == null) return;
            if (!l.grid.TryPlaceAt(it, ox, oy, orot) && !l.grid.AutoPlace(it)) Debug.LogWarning("[Severny] item could not be returned: " + it.id);
        }

        void MoveTo(ItemInstance it, Loc from, InventoryGrid target, int tx, int ty, bool rot)
        {
            if (!from.isSlot && from.grid == target) { if (target.TryPlaceAt(it, tx, ty, rot)) Changed(false); return; }
            int w = rot ? it.Def.h : it.Def.w, h = rot ? it.Def.w : it.Def.h;
            if (!target.Fits(tx, ty, w, h)) return;
            RemoveFrom(it, from);
            if (!target.TryPlaceAt(it, tx, ty, rot)) { PutBack(it, from, it.x, it.y, it.rot); return; }
            Changed(from.isSlot);
        }

        void Equip(ItemInstance it, Loc from, EquipSlot slot)
        {
            if (from.isSlot && from.slot == slot) return;
            if (!Loadout.Accepts(slot, it.Def)) { Hud.Notify("Этот предмет нельзя надеть в этот слот", 1.5f); _ui.Toast("Этот предмет нельзя надеть в этот слот"); return; }
            var occupant = L.Get(slot);
            int ox = it.x, oy = it.y; bool orot = it.rot;
            RemoveFrom(it, from);
            if (occupant != null)
            {
                L.Set(slot, null);
                bool placed = !from.isSlot && from.grid != null && (from.grid.TryPlaceAt(occupant, ox, oy, orot) || from.grid.AutoPlace(occupant));
                if (!placed) placed = mode == Mode.Raid ? L.AutoPlace(occupant) : P.stash.AutoPlace(occupant);
                if (!placed) { L.Set(slot, occupant); PutBack(it, from, ox, oy, orot); _ui.Toast("Нет места для " + occupant.Def.name); return; }
            }
            L.Set(slot, it);
            Changed(true);
        }

        void Unequip(EquipSlot s)
        {
            var it = L.Get(s); if (it == null) return;
            L.Set(s, null);
            bool ok = mode == Mode.Raid ? (L.AutoPlace(it) || (container != null && container.grid.AutoPlace(it))) : P.stash.AutoPlace(it);
            if (!ok) { L.Set(s, it); _ui.Toast("Нет свободного места"); return; }
            Changed(true);
        }

        bool CanCombine(ItemInstance a, ItemInstance b)
        {
            var da = a.Def; var db = b.Def; if (da == null || db == null) return false;
            if (a.id == b.id && da.Stackable && b.count < da.maxStack) return true;
            if (da.type == ItemType.Ammo && db.type == ItemType.Magazine && da.caliber == db.caliber) return true;
            if (da.type == ItemType.Magazine && db.type == ItemType.Weapon && db.magId == a.id) return true;
            return false;
        }

        bool TryCombine(ItemInstance a, Loc aLoc, ItemInstance b)
        {
            var da = a.Def; var db = b.Def;
            if (!CanCombine(a, b)) return false;
            if (a.id == b.id && da.Stackable)
            {
                int mv = Mathf.Min(da.maxStack - b.count, a.count);
                b.count += mv; a.count -= mv;
                if (a.count <= 0) RemoveFrom(a, aLoc);
                return true;
            }
            if (da.type == ItemType.Ammo && db.type == ItemType.Magazine)
            {
                if (b.ammo > 0 && b.ammoId != a.id) { _ui.Toast("В магазине патроны другого типа — сначала разрядите"); return true; }
                int n = Mathf.Min(db.capacity - b.ammo, a.count);
                if (n <= 0) { _ui.Toast("Магазин полон"); return true; }
                b.ammo += n; b.ammoId = a.id; a.count -= n;
                if (a.count <= 0) RemoveFrom(a, aLoc);
                SFX.Play2D(SFX.MagIn, 0.4f);
                _ui.Toast("Снаряжено " + n + " патр.");
                return true;
            }
            if (da.type == ItemType.Magazine && db.type == ItemType.Weapon)
            {
                int wa = b.ammo; string wid = b.ammoId;
                b.ammo = a.ammo; b.ammoId = a.ammoId; a.ammo = wa; a.ammoId = wid;
                SFX.Play2D(SFX.MagIn, 0.5f);
                _ui.Toast("Магазин установлен в " + db.shortName);
                return true;
            }
            return false;
        }

        void QuickMove(ItemInstance it, Loc from)
        {
            if (mode == Mode.Trader) { if (from.grid == P.stash) Sell(it); return; }
            InventoryGrid target = null; bool toLoadout = false;
            if (mode == Mode.Raid) { if (container != null && from.grid == container.grid) toLoadout = true; else if (container != null) target = container.grid; }
            else { if (from.grid == P.stash) toLoadout = true; else target = P.stash; }
            if (from.isSlot) { Unequip(from.slot); return; }
            if (toLoadout)
            {
                foreach (EquipSlot s in System.Enum.GetValues(typeof(EquipSlot)))
                    if (L.Get(s) == null && Loadout.Accepts(s, it.Def)) { Equip(it, from, s); return; }
                MoveAuto(it, from, null);
                return;
            }
            if (target == null) { _ui.Toast("Некуда переместить (откройте контейнер)"); return; }
            MoveAuto(it, from, target);
        }

        void MoveAuto(ItemInstance it, Loc from, InventoryGrid target)
        {
            int ox = it.x, oy = it.y; bool orot = it.rot;
            RemoveFrom(it, from);
            bool ok = target == null ? L.AutoPlace(it) : target.AutoPlace(it);
            if (!ok)
            {
                if (it.count > 0) PutBack(it, from, ox, oy, orot);
                _ui.Toast("Недостаточно места");
            }
            Changed(true);
        }

        void DropToGround(ItemInstance it, Loc from)
        {
            if (Player == null) return;
            RemoveFrom(it, from);
            var t = Player.transform;
            WorldItem.Drop(it, t.position + t.forward * 0.7f + Vector3.up * 0.9f, RaidManager.I != null ? RaidManager.I.runtimeRoot : null);
            SFX.Play2D(SFX.ItemDrop, 0.6f);
            if (_selected == it) _selected = null;
            Changed(true);
        }

        void DefaultAction(ItemInstance it, Loc loc)
        {
            var d = it.Def; if (d == null) return;
            if (mode == Mode.Raid && Player != null && !loc.isSlot && UtilityItems.CanUse(it)) { _ui.Toast(UtilityItems.Use(Player, it, loc.grid)); Changed(false); return; }
            if (d.type == ItemType.Medical && mode == Mode.Raid && Player != null && !loc.isSlot) { UseMed(it, loc); return; }
            if ((d.type == ItemType.Magazine || d.type == ItemType.Weapon) && it.ammo > 0) { Unload(it, loc); return; }
            if (mode == Mode.Trader && loc.grid == P.stash) { Sell(it); return; }
        }

        void UseMed(ItemInstance it, Loc loc)
        {
            if (Player.StartMed(it, loc.grid)) _ui.CloseInventory();
            else _ui.Toast("Сейчас это не нужно");
        }

        void Unload(ItemInstance it, Loc loc)
        {
            if (it.ammo <= 0 || ItemDB.Get(it.ammoId) == null) return;
            var rounds = ItemInstance.Create(it.ammoId, it.ammo);
            int n = it.ammo;
            it.ammo = 0;
            bool ok = (!loc.isSlot && loc.grid != null && loc.grid.AutoPlace(rounds)) || (mode == Mode.Raid ? L.AutoPlace(rounds) : P.stash.AutoPlace(rounds));
            if (!ok)
            {
                it.ammo = rounds.count; // could not place the rest: keep it loaded
                if (rounds.count == n) { _ui.Toast("Нет места для патронов"); return; }
            }
            SFX.Play2D(SFX.MagOut, 0.5f);
            _ui.Toast("Разряжено: " + (n - it.ammo) + " патр.");
            Changed(loc.isSlot);
        }

        void Sell(ItemInstance it)
        {
            if (it == null || it.Def == null || !P.stash.items.Contains(it)) return;
            int price = SellPrice(it);
            if (!P.stash.Remove(it)) return;
            P.roubles += price; P.lootSoldTotal += price;
            if (_selected == it) _selected = null;
            SFX.Play2D(SFX.Buy, 0.6f);
            _ui.Toast("Продано: " + it.Def.name + " за " + UIFactory.Money(price));
            SaveSystem.Save();
            Changed(false);
        }

        static int SellPrice(ItemInstance it) => Mathf.Max(1, Mathf.FloorToInt(it.Price * V14Settings.Current.sellFraction));

        void Changed(bool loadout)
        {
            MarkAll();
            if (mode == Mode.Raid && Player != null) Player.RefreshEquipment();
            if (_tipShown) { HideTip(); _hoverT = 0f; }   // re-shown with fresh numbers
            UpdateInfo(); UpdateStatic();
        }

        // ------------------------------------------------------------------ trader offers
        static string[] Offers => V14Settings.Current.offers;

        static int PackSize(ItemDef d) => d.type == ItemType.Ammo ? Mathf.Min(d.maxStack, 30) : 1;
        static int BuyPrice(ItemDef d) => Mathf.RoundToInt(d.price * PackSize(d) * V14Settings.Current.buyMarkup);

        void Buy(string id)
        {
            var d = ItemDB.Get(id); if (d == null) return;
            int cost = BuyPrice(d);
            if (P.roubles < cost) { _ui.Toast("Недостаточно денег"); return; }
            var it = ItemInstance.Create(id, PackSize(d));
            if (!P.stash.TryAutoPlaceAll(it)) { _ui.Toast("В схроне нет места для всей покупки"); return; }
            P.roubles -= cost;
            SFX.Play2D(SFX.Buy, 0.7f);
            _ui.Toast("Куплено: " + d.name);
            SaveSystem.Save();
            Changed(false);
        }

        // ------------------------------------------------------------------ selection + item card
        void Select(ItemInstance it)
        {
            if (_selected == it) return;
            _selected = it; MarkAll(); UpdateInfo();
            if (_slots != null) foreach (var s in _slots) UpdateSlot(s);
        }

        Loc Find(ItemInstance it)
        {
            foreach (EquipSlot s in System.Enum.GetValues(typeof(EquipSlot))) if (L.Get(s) == it) return Loc.Slot(s);
            foreach (var g in _grids) if (g.grid != null && g.grid.items.Contains(it)) return Loc.Grid(g.grid);
            return default;
        }

        static string Cal(string c) => string.IsNullOrEmpty(c) ? "—" : c.Replace("x", "×");

        static string AmmoName(string id)
        {
            var a = ItemDB.Get(id); if (a == null) return "?";
            return a.name.StartsWith("Патрон ") ? a.name.Substring(7) : (string.IsNullOrEmpty(a.shortName) ? a.name : a.shortName);
        }

        /// <summary>Key stats of an item (card cells + tooltip lines). it may be null (trader offer).</summary>
        static List<Stat> Stats(ItemDef d, ItemInstance it)
        {
            var s = new List<Stat>();
            var n = UITheme.Text0;
            switch (d.type)
            {
                case ItemType.Weapon:
                    s.Add(new Stat("Калибр", Cal(d.caliber), n));
                    s.Add(new Stat("Темп стрельбы", Mathf.RoundToInt(d.rpm) + " в/мин", n));
                    s.Add(new Stat("Режим огня", d.fullAuto ? "авто / одиночный" : "одиночный", n));
                    s.Add(new Stat("Эргономика", Mathf.RoundToInt(d.ergo).ToString(), n));
                    if (it != null)
                    {
                        bool loaded = it.ammo + it.chamber > 0;
                        s.Add(new Stat("Магазин", loaded ? (it.ammo + it.chamber) + " · " + AmmoName(it.ammoId) : "пуст", loaded ? UITheme.AmberHi : UITheme.Text2));
                    }
                    break;
                case ItemType.Magazine:
                    s.Add(new Stat("Калибр", Cal(d.caliber), n));
                    s.Add(new Stat("Ёмкость", d.capacity + " патр.", n));
                    if (it != null)
                    {
                        s.Add(new Stat("Заряжено", it.ammo + " / " + d.capacity, it.ammo > 0 ? UITheme.AmberHi : UITheme.Text2));
                        if (it.ammo > 0) s.Add(new Stat("Патрон", AmmoName(it.ammoId), n));
                    }
                    break;
                case ItemType.Ammo:
                    s.Add(new Stat("Калибр", Cal(d.caliber), n));
                    s.Add(new Stat("Урон", Mathf.RoundToInt(d.damage).ToString(), n));
                    s.Add(new Stat("Пробитие", Mathf.RoundToInt(d.penetration).ToString(), n));
                    s.Add(new Stat("Скорость", Mathf.RoundToInt(d.speed) + " м/с", n));
                    s.Add(new Stat("В пачке", (it != null ? it.count : PackSize(d)) + " / " + d.maxStack, n));
                    break;
                case ItemType.Armor:
                case ItemType.Helmet:
                {
                    s.Add(new Stat("Класс защиты", d.armorClass.ToString(), n));
                    float dur = it != null ? Mathf.Max(0f, it.dur < 0f ? d.durability : it.dur) : d.durability;
                    float c = d.durability > 0f ? Mathf.Clamp01(dur / d.durability) : 1f;
                    s.Add(new Stat("Прочность", Mathf.CeilToInt(dur) + " / " + Mathf.CeilToInt(d.durability), c < 0.995f ? UITheme.Condition(c) : n));
                    s.Add(new Stat("Состояние", Mathf.RoundToInt(c * 100f) + "%", UITheme.Condition(c)));
                    break;
                }
                case ItemType.Medical:
                {
                    if (d.med == MedKind.Medkit)
                    {
                        float dur = it != null ? Mathf.Max(0f, it.dur < 0f ? d.hpPool : it.dur) : d.hpPool;
                        float c = d.hpPool > 0f ? Mathf.Clamp01(dur / d.hpPool) : 1f;
                        s.Add(new Stat("Ресурс", Mathf.CeilToInt(dur) + " / " + Mathf.RoundToInt(d.hpPool) + " HP", c < 0.995f ? UITheme.Condition(c) : n));
                        s.Add(new Stat("Кровотечение", d.stopsBleed ? "останавливает" : "нет", d.stopsBleed ? UITheme.Green : UITheme.Text2));
                    }
                    else if (d.med == MedKind.Bandage) s.Add(new Stat("Кровотечение", "останавливает", UITheme.Green));
                    else if (d.med == MedKind.Surgery) s.Add(new Stat("Часть тела", "восстанавливает", UITheme.Green));
                    else s.Add(new Stat("Эффект", "обезболивание", UITheme.Green));
                    if (d.med != MedKind.Medkit && d.uses > 1)
                    {
                        float u = it != null ? Mathf.Max(0f, it.dur < 0f ? d.uses : it.dur) : d.uses;
                        s.Add(new Stat("Применений", Mathf.CeilToInt(u) + " / " + d.uses, n));
                    }
                    s.Add(new Stat("Время", d.useTime.ToString("0.#") + " с", n));
                    break;
                }
                default:
                    s.Add(new Stat("Цена за клетку", UITheme.Rub(Mathf.RoundToInt(d.price / (float)Mathf.Max(1, d.w * d.h))), UITheme.AmberHi));
                    if (d.weight > 0.001f) s.Add(new Stat("Цена за кг", UITheme.Rub(Mathf.RoundToInt(d.price / d.weight)), n));
                    if (d.Stackable && it != null && it.count > 1) s.Add(new Stat("Цена за шт.", UITheme.Rub(d.price), n));
                    break;
            }
            if (d.Stackable && d.type != ItemType.Ammo && it != null) s.Add(new Stat("Количество", it.count + " / " + d.maxStack, n));
            return s;
        }

        static string TypeHint(ItemDef d, Mode m)
        {
            switch (d.type)
            {
                case ItemType.Weapon: return "Перетащите магазин на оружие, чтобы заменить его.";
                case ItemType.Magazine: return "Перетащите патроны на магазин, чтобы снарядить.";
                case ItemType.Ammo: return "Перетащите пачку на магазин, чтобы снарядить его.";
                case ItemType.Armor: case ItemType.Helmet: return "Прочность падает от попаданий; изношенная защита дешевле.";
                case ItemType.Medical: return m == Mode.Raid ? "ПКМ — применить." : "Применяется в рейде (ПКМ).";
                default: return "";
            }
        }

        void ClearInfoDyn()
        {
            foreach (var b in _actionButtons) if (b != null) Object.Destroy(b);
            _actionButtons.Clear();
            foreach (var g in _infoDyn) if (g != null) Object.Destroy(g);
            _infoDyn.Clear();
        }

        void UpdateInfo()
        {
            ClearInfoDyn();
            var it = _selected;
            bool trader = mode == Mode.Trader;
            if (it == null || it.Def == null)
            {
                var hand = UIAssets.Glyph("hand");
                _infoIcon.sprite = hand; _infoIcon.enabled = hand != null; _infoIcon.color = UITheme.A(UITheme.Text3, 0.85f);
                UIFactory.Place(_infoIcon.rectTransform, 66, 66, 72, 72);
                _infoGlow.color = new Color(0f, 0f, 0f, 0f); _infoNotch.color = new Color(0f, 0f, 0f, 0f); _infoEdge.color = UITheme.Line1;
                _infoTitle.fontSize = 26; _infoTitle.text = "Предмет не выбран"; _infoTitle.color = UITheme.Text2;
                _infoPrice.text = ""; _infoPriceSub.text = "";
                _infoBody.text = trader ? "Выберите предмет в схроне, чтобы увидеть цену продажи. Ctrl+клик — быстрая продажа."
                    : "Перетаскивайте предметы мышью (R — повернуть). Ctrl+клик — быстро переместить. ПКМ — использовать / разрядить.";
                _hintsNormal.gameObject.SetActive(!trader); _hintsTrader.gameObject.SetActive(trader);
                return;
            }
            var loc = Find(it);
            if (!loc.Valid) { _selected = null; UpdateInfo(); return; }
            var d = it.Def;
            int rar = Mathf.Clamp(d.rarity, 1, 5); var rc = UITheme.Rarity(rar);
            _hintsNormal.gameObject.SetActive(false); _hintsTrader.gameObject.SetActive(false);

            // preview
            UIFactory.Place(_infoIcon.rectTransform, 14, 14, 176, 176);
            ItemVisual.SetFitIcon(_infoIcon, d, it);
            _infoGlow.color = rar >= 3 ? UITheme.A(rc, 0.24f) : UITheme.A(Color.white, 0.05f);
            _infoNotch.color = rar >= 2 ? rc : new Color(0f, 0f, 0f, 0f);
            _infoEdge.color = rar >= 3 ? UITheme.A(rc, 0.6f) : UITheme.Line1;

            // title, price
            _infoTitle.color = UITheme.Text0; _infoTitle.fontSize = 26; _infoTitle.text = d.name;
            ItemVisual.FitText(_infoTitle, 590, 22);
            _infoPrice.text = UITheme.Rub(it.Price);
            if (trader) _infoPriceSub.text = loc.grid == P.stash ? "скупщик даст " + UITheme.Tint(UITheme.Rub(SellPrice(it)), UITheme.AmberHi) : "продаётся только из схрона";
            else _infoPriceSub.text = "≈ рыночная цена" + (d.Stackable && it.count > 1 ? "  ·  " + UITheme.Rub(d.price) + " / шт." : "");

            // chips: type, rarity, size, weight (+ count, equipped)
            float cx = 0f;
            void AddChip(string text, Color c, string glyph)
            {
                var ch = UIFactory.Chip(_infoChips, text, cx, 0, c, glyph, 22);
                cx += ch.sizeDelta.x + 6f; _infoDyn.Add(ch.gameObject);
            }
            AddChip(UITheme.TypeName(d.type), UITheme.Text1, UITheme.TypeGlyph(d.type));
            AddChip(UITheme.RarityName(rar), rar >= 2 ? rc : UITheme.Text2, "star");
            AddChip(it.W + "×" + it.H, UITheme.Text2, "grid");
            AddChip(Kg(it.Weight), UITheme.Text2, "weight");
            if (d.Stackable && it.count > 1) AddChip("×" + it.count, UITheme.AmberHi, "stack");
            if (loc.isSlot) AddChip("надето", UITheme.Green, "check");

            // description + hint
            string desc = string.IsNullOrEmpty(d.desc) ? "" : d.desc.Trim();
            if (desc.Length > 0 && !desc.EndsWith(".") && !desc.EndsWith("!") && !desc.EndsWith("…")) desc += ".";
            string hint = TypeHint(d, mode);
            _infoBody.text = desc + (desc.Length > 0 && hint.Length > 0 ? " " : "") + UITheme.Tint(hint, UITheme.Text2);

            // stat cells
            var st = Stats(d, it);
            for (int i = 0; i < st.Count && i < 5; i++)
            {
                var cell = UIFactory.Node(_infoStats, "Stat" + i); UIFactory.Place(cell, i * 171f, 0, 163, 46);
                if (i > 0) UIFactory.Box(cell, "sep", -9, 4, 1, 38, UITheme.Line0);
                UIFactory.Caps(cell, st[i].k, 0, 0, 163, 16, 11, UITheme.Text2, TextAnchor.MiddleLeft, 1.3f);
                var v = UIFactory.T(cell, st[i].v, 0, 17, 163, 28, UITheme.BodySemi, 17, st[i].c, TextAnchor.MiddleLeft, "value");
                v.horizontalOverflow = HorizontalWrapMode.Overflow;
                ItemVisual.FitText(v, 160, 14);
                _infoDyn.Add(cell.gameObject);
            }

            // actions (same conditions as before)
            float x = 0f;
            void Act(string label, System.Action a, BtnStyle style, string glyph)
            {
                var b = UIFactory.Button(_actions, label, x, 0, 160, 40, a, style, glyph, 16);
                var fx = b.GetComponent<UIButtonFX>();
                float tw = fx != null && fx.label != null ? UIFactory.TextWidth(fx.label) : label.Length * 10f;
                float bw = Mathf.Max(132f, Mathf.Ceil(tw + (glyph != null ? 25f : 0f) + 40f));
                ((RectTransform)b.transform).sizeDelta = new Vector2(bw, 40f);
                UIFactory.SetButtonText(b, label);
                x += bw + 8f; _actionButtons.Add(b.gameObject);
            }
            if (mode == Mode.Raid && Player != null && !loc.isSlot && UtilityItems.CanUse(it))
                Act(d.type == ItemType.Food ? "УПОТРЕБИТЬ" : "РЕМОНТ БРОНИ (1 ИЗОЛЕНТА)", () => { _ui.Toast(UtilityItems.Use(Player, it, loc.grid)); Changed(false); }, BtnStyle.Primary, "med");
            if (d.type == ItemType.Medical && mode == Mode.Raid && Player != null && !loc.isSlot) Act("ИСПОЛЬЗОВАТЬ", () => UseMed(it, loc), BtnStyle.Primary, "med");
            if ((d.type == ItemType.Magazine || d.type == ItemType.Weapon) && it.ammo > 0) Act("РАЗРЯДИТЬ", () => Unload(it, loc), BtnStyle.Secondary, "bullet");
            if (!trader)
            {
                if (loc.isSlot) Act("СНЯТЬ", () => Unequip(loc.slot), BtnStyle.Secondary, "minus");
                else foreach (EquipSlot s in System.Enum.GetValues(typeof(EquipSlot)))
                        if (Loadout.Accepts(s, d)) { var ss = s; Act("НАДЕТЬ", () => Equip(it, loc, ss), BtnStyle.Secondary, "check"); break; }
                if (mode == Mode.Stash) Act(loc.grid == P.stash ? "В СНАРЯЖЕНИЕ" : "В СХРОН", () => QuickMove(it, loc), BtnStyle.Secondary, loc.grid == P.stash ? "backpack" : "box");
                if (mode == Mode.Raid && container != null && !loc.isSlot) Act(loc.grid == container.grid ? "ЗАБРАТЬ" : "ПОЛОЖИТЬ", () => QuickMove(it, loc), BtnStyle.Secondary, loc.grid == container.grid ? "hand" : "box");
                if (mode == Mode.Raid) Act("ВЫБРОСИТЬ", () => DropToGround(it, loc), BtnStyle.Danger, "ground");
            }
            else if (loc.grid == P.stash) Act("ПРОДАТЬ ЗА " + UITheme.Rub(SellPrice(it)), () => Sell(it), BtnStyle.Primary, "rouble");
        }

        // ------------------------------------------------------------------ static numbers (money, slots, weight, value)
        void UpdateStatic()
        {
            if (mode != Mode.Raid)
            {
                _money.text = UITheme.Rub(P.roubles);
                float cw = UIFactory.SetChip(_levelChip, "ур. " + P.Level);
                _levelChip.anchoredPosition = new Vector2(660f - cw, -21f);
                UIFactory.Place(_money.rectTransform, 0, 0, 660f - cw - 14f, 64);
            }
            foreach (var s in _slots) UpdateSlot(s);
            float w = L.TotalWeight();
            bool over = w > WeightLimit;
            _weightVal.text = UITheme.Tint(w.ToString("0.0"), over ? UITheme.Red : UITheme.Text0) + UITheme.Tint(" / " + WeightLimit.ToString("0") + " кг", UITheme.Text2);
            UIFactory.SetBar(_weightBar, Mathf.Clamp01(w / WeightLimit));
            _weightBar.color = over ? UITheme.Red : w > WeightLimit * 0.8f ? UITheme.Orange : UITheme.Amber;
            if (_overChip.gameObject.activeSelf != over) _overChip.gameObject.SetActive(over);
            _valueVal.text = UITheme.Rub(L.TotalValue());
            var st = P.stash; int used = 0;
            foreach (var it in st.items) used += it.W * it.H;
            int pct = st.w * st.h > 0 ? Mathf.RoundToInt(100f * used / (st.w * st.h)) : 0;
            _stashInfo.text = "занято " + pct + "%  ·  " + st.w + "×" + st.h;
            foreach (var r in _offerRows) r.price.color = P.roubles >= BuyPrice(r.d) ? UITheme.AmberHi : UITheme.A(UITheme.Red, 0.9f);
        }

        void UpdateSlot(SlotView s)
        {
            var it = L.Get(s.slot); var d = it != null ? it.Def : null;
            bool has = d != null, sel = has && it == _selected;
            s.empty.gameObject.SetActive(!has); s.emptyText.gameObject.SetActive(!has);
            s.icon.gameObject.SetActive(has); s.foot.gameObject.SetActive(has);
            s.name.gameObject.SetActive(has); s.info.gameObject.SetActive(has);
            if (s.sel.gameObject.activeSelf != sel) s.sel.gameObject.SetActive(sel);
            s.fill.color = sel ? UITheme.A(UITheme.Bg2, 0.97f) : UITheme.A(UITheme.Bg1, 0.94f);
            var condRoot = s.cond.transform.parent.gameObject;
            if (!has)
            {
                s.chip.gameObject.SetActive(false); condRoot.SetActive(false);
                s.glow.color = new Color(0f, 0f, 0f, 0f);
                return;
            }
            int rar = Mathf.Clamp(d.rarity, 1, 5);
            s.glow.color = rar >= 3 ? UITheme.A(UITheme.Rarity(rar), 0.16f) : UITheme.A(Color.white, 0.03f);
            ItemVisual.SetFitIcon(s.icon, d, it);
            // right: weapon state / badge; left: name (fitted into the rest)
            if (d.type == ItemType.Weapon)
            {
                bool loaded = it.ammo + it.chamber > 0;
                s.info.text = (loaded ? UITheme.Tint("ЗАРЯЖЕН", UITheme.AmberHi) : UITheme.Tint("НЕ ЗАРЯЖЕН", UITheme.Text2)) + (d.fullAuto ? "  ·  " + (it.fullAutoMode ? "АВТО" : "ОДИН.") : "");
            }
            else s.info.text = it.Badge;
            s.info.color = d.type == ItemType.Weapon ? UITheme.Text1 : ItemVisual.BadgeColor(it);
            float iw = UIFactory.TextWidth(s.info);
            s.name.fontSize = 15; s.name.text = d.name;
            ItemVisual.FitText(s.name, s.w - 36f - iw - 12f, 13);
            // chip: calibre (weapons) or protection class (armour)
            string chip = d.type == ItemType.Weapon ? Cal(d.caliber) : (d.type == ItemType.Armor || d.type == ItemType.Helmet) ? "класс " + d.armorClass : null;
            s.chip.gameObject.SetActive(chip != null);
            if (chip != null)
            {
                float cw = UIFactory.SetChip(s.chip, chip, d.type == ItemType.Weapon ? UITheme.Text1 : UITheme.Steel);
                s.chip.anchoredPosition = new Vector2(s.w - 8f - cw, -6f);
            }
            float c = ItemVisual.Condition(it);
            bool showCond = c >= 0f && c < 0.995f;
            condRoot.SetActive(showCond);
            if (showCond) { UIFactory.SetBar(s.cond, c); s.cond.color = UITheme.Condition(c); }
        }

        // ------------------------------------------------------------------ health + raid clock
        void ShowRestedHealth()
        {
            float tot = 0f;
            for (int i = 0; i < Health.Parts; i++)
            {
                float mx = Health.DefaultMax[i]; tot += mx;
                _hpTexts[i].text = Mathf.RoundToInt(mx) + "/" + Mathf.RoundToInt(mx);
                _hpTexts[i].color = UITheme.Text0;
                UIFactory.SetBar(_hpBars[i], 1f); _hpBars[i].color = UITheme.Green;
                _bodyParts[i].color = UITheme.A(UITheme.Green, 0.85f);
                _hpDrops[i].gameObject.SetActive(false);
                _hpNames[i].color = UITheme.Text1;
            }
            foreach (var c in _hpChips) c.gameObject.SetActive(false);
            _hpTotal.text = Mathf.RoundToInt(tot) + "/" + Mathf.RoundToInt(tot);
        }

        void UpdateDynamic()
        {
            if (mode != Mode.Raid) return;
            // raid clock (red in the last 5 minutes)
            int sec = RaidManager.I != null ? Mathf.Max(0, Mathf.CeilToInt(RaidManager.I.timeLeft)) : -1;
            if (sec != _clockShown)
            {
                _clockShown = sec;
                _raidClock.text = sec < 0 ? "--:--" : (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
                _raidClock.color = sec >= 0 && sec < 300 ? UITheme.Red : UITheme.AmberHi;
            }
            if (Player == null || Player.health == null || Player.health.hp == null) return;
            var h = Player.health;
            int sig = 17;
            for (int i = 0; i < Health.Parts; i++) sig = sig * 31 + Mathf.CeilToInt(Mathf.Max(0f, h.hp[i])) * 2 + (h.bleeding[i] ? 1 : 0);
            sig = sig * 31 + Mathf.CeilToInt(h.painkillerTimer);
            if (sig == _hpSig) return;
            _hpSig = sig;
            var dead = UITheme.H(0x5A1C14);
            for (int i = 0; i < Health.Parts; i++)
            {
                float hp = Mathf.Max(0f, h.hp[i]), mx = Mathf.Max(1f, h.max[i]), f = hp / mx;
                var c = hp <= 0f ? dead : f > 0.85f ? UITheme.Green : f > 0.45f ? UITheme.Amber : UITheme.Red;
                _hpTexts[i].text = Mathf.CeilToInt(hp) + "/" + Mathf.RoundToInt(mx);
                _hpTexts[i].color = hp <= 0f ? UITheme.Red : f > 0.85f ? UITheme.Text0 : c;
                UIFactory.SetBar(_hpBars[i], f); _hpBars[i].color = c;
                _bodyParts[i].color = hp <= 0f ? dead : UITheme.A(c, 0.9f);
                if (_hpDrops[i].gameObject.activeSelf != h.bleeding[i]) _hpDrops[i].gameObject.SetActive(h.bleeding[i]);
                _hpNames[i].color = hp <= 0f ? UITheme.Red : UITheme.Text1;
            }
            _hpTotal.text = Mathf.CeilToInt(h.Total) + "/" + Mathf.RoundToInt(h.TotalMax);
            // effect chips (flow layout)
            int n = 0; float x = 14f, y = 296f, maxX = _healthPanel.sizeDelta.x - 14f;
            void Effect(string text, Color col, string glyph)
            {
                if (n >= _hpChips.Count) return;
                var ch = _hpChips[n++]; ch.gameObject.SetActive(true);
                float cw = UIFactory.SetChip(ch, text, col, glyph);
                if (x > 14f && x + cw > maxX) { x = 14f; y += 28f; }
                ch.anchoredPosition = new Vector2(x, -y); x += cw + 6f;
            }
            if (h.AnyBleeding) Effect("кровотечение", UITheme.Red, "drop");
            if (h.LegsBroken) Effect("нога разрушена", UITheme.Red, "bone");
            if (h.ArmsBroken) Effect("рука разрушена", UITheme.Red, "bone");
            if (h.PainSuppressed) Effect("обезбол " + Mathf.CeilToInt(h.painkillerTimer) + " с", UITheme.Green, "pill");
            if (n == 0) Effect("здоров", UITheme.Green, "check");
            for (int i = n; i < _hpChips.Count; i++) if (_hpChips[i].gameObject.activeSelf) _hpChips[i].gameObject.SetActive(false);
        }
    }
}
