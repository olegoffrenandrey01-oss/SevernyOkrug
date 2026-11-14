// v11 inventory grid: header (tick + caps title + size / weight), recessed tray of engraved cells, item tiles
// (3D icons, rarity notch / glow, count, condition), hover + selection states, hit testing and the drop ghost
// (green = fits, red = blocked, amber = combine). Tiles are rebuilt only when their item actually changes.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    public class GridView
    {
        public InventoryGrid grid;
        public LootContainer container;      // hidden (not yet searched) items are drawn face-down
        public RectTransform clip;           // scroll viewport (hit tests outside it are ignored)
        public RectTransform root, items, headerRoot;
        /// <summary>Header title (caps) and the small text on its right (size / weight by default).</summary>
        public Text header, headerInfo;
        public readonly string title;
        public bool dirty = true;
        public System.Func<ItemInstance> selected;
        /// <summary>Right side of the header; default "4×2 · 1,2 кг".</summary>
        public System.Func<InventoryGrid, string> info;

        bool _headerVisible = true; float _minHeaderW;
        readonly RectTransform _headerLine;
        readonly Image _ghost, _ghostEdge;
        readonly Dictionary<ItemInstance, Tile> _tiles = new Dictionary<ItemInstance, Tile>();
        readonly List<ItemInstance> _gone = new List<ItemInstance>();
        readonly System.Action _onChanged;
        ItemInstance _hover;

        class Tile { public RectTransform rt; public int sig; public bool hidden; }

        const float C = UIFactory.Cell;

        public GridView(Transform parent, string title, float x, float y, InventoryGrid g, LootContainer c = null)
        {
            this.title = title;
            // ---- header: amber tick, caps title, right info, hairline (8 units above the tray)
            headerRoot = UIFactory.Node(parent, "GridHeader_" + title);
            UIFactory.Place(headerRoot, x, y - 40, 256, 31);
            UIFactory.Box(headerRoot, "tick", 0, 8, 3, 12, UITheme.Amber);
            header = UIFactory.Caps(headerRoot, title, 12, 0, 244, 28, 15, UITheme.Text1, TextAnchor.MiddleLeft, 2.2f);
            header.name = "Header_" + title; header.horizontalOverflow = HorizontalWrapMode.Overflow;
            headerInfo = UIFactory.Mono(headerRoot, "", 0, 0, 256, 28, 12, UITheme.Text2, TextAnchor.MiddleRight);
            headerInfo.name = "HeaderInfo"; headerInfo.horizontalOverflow = HorizontalWrapMode.Overflow;
            _headerLine = UIFactory.Box(headerRoot, "line", 0, 30, 256, 1, UITheme.Line0).rectTransform;

            // ---- tray + tiled cells
            root = UIFactory.Node(parent, "Grid_" + title);
            UIFactory.Place(root, x, y, C, C);
            var tray = UIFactory.Img(root, "tray", UITheme.A(UITheme.Bg0, 0.94f)); UIFactory.Stretch(tray.rectTransform, -3, -3, -3, -3);
            UIFactory.Border(tray.transform, UITheme.Line1);
            var cells = UIFactory.Pattern(root, "cells", UIAssets.Cell, Color.white);
            cells.raycastTarget = false;
            UIFactory.Gradient(root, "shade", 0, 14, UITheme.A(Color.black, 0.28f));
            items = UIFactory.Stretch(UIFactory.Node(root, "Items"));
            _ghost = UIFactory.Img(root, "Ghost", UITheme.A(UITheme.Green, 0.2f));
            _ghostEdge = UIFactory.Sliced(_ghost.transform, "edge", UIAssets.Frame2, UITheme.Green); UIFactory.Stretch(_ghostEdge.rectTransform);
            _ghost.gameObject.SetActive(false);
            _onChanged = () => dirty = true;
            Bind(g, c);
        }

        /// <summary>Show / hide the header (the stash column draws its own).</summary>
        public bool headerVisible
        {
            get => _headerVisible;
            set { _headerVisible = value; headerRoot.gameObject.SetActive(grid != null && value && root.gameObject.activeSelf); }
        }

        /// <summary>Header width at least this (e.g. a small container in a wide column).</summary>
        public float minHeaderW { get => _minHeaderW; set { _minHeaderW = value; LayoutHeader(); } }

        public void Bind(InventoryGrid g, LootContainer c = null)
        {
            if (grid != null) grid.Changed -= _onChanged;
            if (!ReferenceEquals(g, grid)) ClearTiles();
            grid = g; container = c;
            if (grid != null)
            {
                grid.Changed += _onChanged;
                root.sizeDelta = new Vector2(grid.w * C, grid.h * C);
            }
            root.gameObject.SetActive(grid != null);
            LayoutHeader();
            headerRoot.gameObject.SetActive(grid != null && _headerVisible);
            dirty = true;
        }

        public void Unbind() { if (grid != null) grid.Changed -= _onChanged; grid = null; ClearTiles(); }

        public void SetActive(bool on)
        {
            root.gameObject.SetActive(on && grid != null);
            headerRoot.gameObject.SetActive(on && grid != null && _headerVisible);
        }

        void LayoutHeader()
        {
            float w = Mathf.Max(grid != null ? grid.w * C : C, _minHeaderW);
            headerRoot.sizeDelta = new Vector2(w, 31);
            header.rectTransform.sizeDelta = new Vector2(w - 12, 28);
            headerInfo.rectTransform.sizeDelta = new Vector2(w, 28);
            _headerLine.sizeDelta = new Vector2(w, 1);
        }

        void ClearTiles()
        {
            foreach (var kv in _tiles) if (kv.Value.rt != null) Object.Destroy(kv.Value.rt.gameObject);
            _tiles.Clear(); _hover = null;
        }

        /// <summary>Everything that changes an item tile's look (position, count, rounds, condition, hidden...).</summary>
        static int Sig(ItemInstance it, bool hidden)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (it.id != null ? it.id.GetHashCode() : 0);
                h = h * 31 + it.x; h = h * 31 + it.y; h = h * 31 + (it.rot ? 1 : 0);
                h = h * 31 + it.count; h = h * 31 + it.ammo; h = h * 31 + it.chamber;
                h = h * 31 + Mathf.RoundToInt(it.dur * 10f);
                h = h * 31 + (it.ammoId != null ? it.ammoId.GetHashCode() : 0);
                h = h * 31 + (it.fullAutoMode ? 1 : 0) + (hidden ? 2 : 0);
                return h;
            }
        }

        public void Refresh()
        {
            if (!dirty || grid == null) return;
            dirty = false;
            var sel = selected != null ? selected() : null;
            _gone.Clear();
            foreach (var kv in _tiles) if (!grid.items.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var it in _gone) { var t = _tiles[it]; if (t.rt != null) Object.Destroy(t.rt.gameObject); _tiles.Remove(it); }
            if (_hover != null && !_tiles.ContainsKey(_hover) && !grid.items.Contains(_hover)) _hover = null;

            foreach (var it in grid.items)
            {
                bool hidden = container != null && container.IsHidden(it);
                int sig = Sig(it, hidden);
                if (_tiles.TryGetValue(it, out var t))
                {
                    if (t.sig != sig || t.rt == null)
                    {
                        bool reveal = t.hidden && !hidden;
                        if (t.rt != null) Object.Destroy(t.rt.gameObject);
                        t.rt = ItemVisual.Make(items, it, it.x * C, it.y * C, it.W, it.H, hidden, it == sel);
                        t.sig = sig; t.hidden = hidden;
                        if (reveal) ItemVisual.Reveal(t.rt);
                    }
                    else ItemVisual.SetSelected(t.rt, it == sel && !hidden);
                }
                else
                {
                    t = new Tile { rt = ItemVisual.Make(items, it, it.x * C, it.y * C, it.W, it.H, hidden, it == sel), sig = sig, hidden = hidden };
                    _tiles[it] = t;
                }
                ItemVisual.SetHover(t.rt, ReferenceEquals(it, _hover) && !hidden);
            }
            headerInfo.text = info != null ? info(grid) : DefaultInfo(grid);
            _ghost.transform.SetAsLastSibling();
        }

        static string DefaultInfo(InventoryGrid g)
        {
            float w = g.Weight();
            return g.w + "×" + g.h + (w >= 0.05f ? "  ·  " + w.ToString("0.0") + " кг" : "");
        }

        /// <summary>Hover highlight of one item (null = none).</summary>
        public void SetHover(ItemInstance it)
        {
            if (ReferenceEquals(_hover, it)) return;
            if (_hover != null && _tiles.TryGetValue(_hover, out var a)) ItemVisual.SetHover(a.rt, false);
            _hover = it;
            if (it != null && _tiles.TryGetValue(it, out var b) && !b.hidden) ItemVisual.SetHover(b.rt, true);
        }

        /// <summary>Screen-space rect of an item's tile (tooltips), false when it has none.</summary>
        public RectTransform TileOf(ItemInstance it) => it != null && _tiles.TryGetValue(it, out var t) ? t.rt : null;

        public bool Contains(Vector2 screen) => grid != null && UIFactory.Contains(root, screen) && (clip == null || UIFactory.Contains(clip, screen));

        /// <summary>Cell under the screen point (may be outside the grid -> false).</summary>
        public bool CellAt(Vector2 screen, out int cx, out int cy)
        {
            var p = UIFactory.LocalTopLeft(root, screen);
            cx = Mathf.FloorToInt(p.x / C); cy = Mathf.FloorToInt(p.y / C);
            return grid != null && cx >= 0 && cy >= 0 && cx < grid.w && cy < grid.h;
        }

        public void ShowGhost(int x, int y, int w, int h, bool ok) => ShowGhost(x, y, w, h, ok, false);

        /// <summary>Drop preview: green = fits, red = blocked, amber = combine (stack / load / insert).</summary>
        public void ShowGhost(int x, int y, int w, int h, bool ok, bool combine)
        {
            if (grid == null) return;
            int x0 = Mathf.Max(0, x), y0 = Mathf.Max(0, y), x1 = Mathf.Min(grid.w, x + w), y1 = Mathf.Min(grid.h, y + h);
            if (x1 <= x0 || y1 <= y0) { HideGhost(); return; }
            _ghost.gameObject.SetActive(true);
            UIFactory.Place(_ghost.rectTransform, x0 * C, y0 * C, (x1 - x0) * C, (y1 - y0) * C);
            var c = combine ? UITheme.Amber : ok ? UITheme.Green : UITheme.Red;
            _ghost.color = UITheme.A(c, combine ? 0.24f : 0.2f);
            _ghostEdge.color = UITheme.A(c, 0.95f);
            _ghost.transform.SetAsLastSibling();
        }

        public void HideGhost() { if (_ghost.gameObject.activeSelf) _ghost.gameObject.SetActive(false); }
    }

    /// <summary>Item tile: tinted plate, rendered icon (rotated with the item), rarity notch / glow, short name,
    /// badge (count / rounds / durability), condition bar, weapon state. Children "hov" / "sel" are the hover / selection states.</summary>
    public static class ItemVisual
    {
        /// <summary>Plate colour of an item category (blended with the item's own colour).</summary>
        public static Color TypeColor(ItemDef d)
        {
            if (d == null) return UITheme.Bg3;
            return Color.Lerp(UITheme.TypeTint(d.type), d.color, 0.3f);
        }

        /// <summary>Rendered icon for an item instance (weapons without rounds use the "no magazine" variant).</summary>
        public static Sprite IconSprite(ItemInstance it)
        {
            var d = it != null ? it.Def : null; if (d == null) return null;
            bool empty = d.type == ItemType.Weapon && it.ammo + it.chamber <= 0;
            return UIAssets.ItemIcon(d.id, empty);
        }

        /// <summary>0..1 condition (armour durability, medkit resource, uses), -1 when the item has none.</summary>
        public static float Condition(ItemInstance it)
        {
            var d = it != null ? it.Def : null; if (d == null) return -1f;
            float dur = it.dur;
            switch (d.type)
            {
                case ItemType.Armor:
                case ItemType.Helmet:
                    return d.durability > 0 ? Mathf.Clamp01((dur < 0 ? d.durability : dur) / d.durability) : -1f;
                case ItemType.Medical:
                    if (d.med == MedKind.Medkit) return d.hpPool > 0 ? Mathf.Clamp01((dur < 0 ? d.hpPool : dur) / d.hpPool) : -1f;
                    return d.uses > 1 ? Mathf.Clamp01((dur < 0 ? d.uses : dur) / d.uses) : -1f;
                default: return -1f;
            }
        }

        /// <summary>Colour of the tile badge (rounds amber, empty magazine dim, durability by condition).</summary>
        public static Color BadgeColor(ItemInstance it)
        {
            var d = it.Def; if (d == null) return UITheme.Text1;
            switch (d.type)
            {
                case ItemType.Magazine: return it.ammo > 0 ? UITheme.AmberHi : UITheme.Text2;
                case ItemType.Armor:
                case ItemType.Helmet:
                case ItemType.Medical: { float c = Condition(it); return c < 0 ? UITheme.Text0 : c > 0.6f ? UITheme.Text0 : UITheme.Condition(c); }
                default: return UITheme.Text0;
            }
        }

        public static RectTransform Make(Transform parent, ItemInstance it, float x, float y, int w, int h, bool hidden, bool selected)
        {
            var d = it.Def;
            float pw = w * UIFactory.Cell - 2f, ph = h * UIFactory.Cell - 2f;
            int rar = d != null ? Mathf.Clamp(d.rarity, 1, 5) : 1;
            var rc = UITheme.Rarity(rar);
            var plate = UIFactory.Box(parent, "Item_" + (d != null ? d.id : "?"), x + 1, y + 1, pw, ph, hidden ? UITheme.A(UITheme.Bg1, 0.97f) : UITheme.A(TypeColor(d), 0.93f));
            var t = plate.transform;

            if (hidden)
            {
                var hatch = UIFactory.Pattern(t, "hatch", UIAssets.Hatch, UITheme.A(UITheme.Line1, 0.55f));
                UIFactory.Border(t, UITheme.Line1, "edge");
                float s = Mathf.Clamp(Mathf.Min(pw, ph) * 0.42f, 18f, 40f);
                var q = UIFactory.Icon(t, "search", (pw - s) * 0.5f, (ph - s) * 0.5f, s, UITheme.Text2, "search");
                var pulse = q.gameObject.AddComponent<UIPulse>(); pulse.graphic = q; pulse.speed = 0.9f; pulse.min = 0.25f; pulse.max = 0.85f;
                if (!q.enabled) { var qt = UIFactory.T(t, "?", 0, 0, pw, ph, UITheme.Display, Mathf.RoundToInt(s), UITheme.Text2, TextAnchor.MiddleCenter, "q"); }
                AddStates(t, pw, ph, false);
                return plate.rectTransform;
            }

            // light from above + rarity glow behind the icon
            var sheen = UIFactory.Gradient(t, "sheen", 0, ph * 0.55f, UITheme.A(Color.white, 0.05f));
            UIFactory.Gradient(t, "foot", 1, Mathf.Min(ph * 0.4f, 26f), UITheme.A(Color.black, 0.22f));
            if (rar >= 3)
            {
                var glow = UIFactory.Img(t, "glow", UITheme.A(rc, rar >= 5 ? 0.26f : rar == 4 ? 0.2f : 0.14f));
                UIFactory.SetSoft(glow, "radial", glow.color); UIFactory.Stretch(glow.rectTransform, pw * 0.08f, ph * 0.08f, pw * 0.08f, ph * 0.08f);
            }

            // icon (natural orientation in the atlas; rotated 90 degrees with the item)
            var sp = IconSprite(it);
            bool rotated = d != null && w != d.w && d.w != d.h;
            if (sp != null)
            {
                var icon = UIFactory.Img(t, "icon", Color.white);
                icon.sprite = sp; icon.preserveAspect = true;
                var irt = icon.rectTransform;
                float pad = Mathf.Min(pw, ph) < 70f ? 3f : 5f;
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = Vector2.zero;
                irt.sizeDelta = rotated ? new Vector2(ph - pad * 2f, pw - pad * 2f) : new Vector2(pw - pad * 2f, ph - pad * 2f);
                if (rotated) irt.localEulerAngles = new Vector3(0f, 0f, 90f);
                UIFactory.DropShadow(icon, 0.45f, 2f);
            }
            else
            {
                float s = Mathf.Clamp(Mathf.Min(pw, ph) * 0.46f, 18f, 48f);
                var g = UIFactory.Icon(t, d != null ? UITheme.TypeGlyph(d.type) : "box", (pw - s) * 0.5f, (ph - s) * 0.5f, s, UITheme.A(UITheme.Text1, 0.35f), "glyph");
                if (!g.enabled) UIFactory.T(t, d != null ? d.shortName : "?", 0, 0, pw, ph, UITheme.Display, Mathf.RoundToInt(s * 0.6f), UITheme.A(UITheme.Text0, 0.2f), TextAnchor.MiddleCenter, "glyphText");
            }

            // edge (rarity tinted for rare+) and rarity notch
            var edge = UIFactory.Border(t, rar >= 4 ? UITheme.A(rc, 0.62f) : rar == 3 ? UITheme.A(rc, 0.42f) : UITheme.A(UITheme.Line2, 0.85f), "edge");
            if (rar >= 2)
            {
                var notch = UIFactory.Img(t, "rarity", UITheme.A(rc, 0.95f)); notch.sprite = UIAssets.CornerTL;
                UIFactory.Place(notch.rectTransform, 1, 1, 9, 9);
            }

            // short name (top right)
            if (d != null)
            {
                var n = UIFactory.T(t, d.shortName, 4, 2, pw - 8, 18, UITheme.BodySemi, 13, UITheme.Text0, TextAnchor.UpperRight, "name");
                n.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.DropShadow(n, 0.85f, 1f);
                FitText(n, pw - (rar >= 2 ? 16f : 8f));
            }

            // condition bar (only when used / damaged)
            float cond = Condition(it);
            bool bar = cond >= 0f && cond < 0.995f;
            if (bar)
            {
                var b = UIFactory.Bar(t, 4, ph - 5, pw - 8, 2, UITheme.Condition(cond));
                b.transform.parent.GetComponent<Image>().color = UITheme.A(Color.black, 0.55f);
                UIFactory.SetBar(b, cond);
            }

            // badge (bottom right): count / rounds / durability
            string badge = it.Badge;
            if (!string.IsNullOrEmpty(badge))
            {
                var bt = UIFactory.Mono(t, badge, 3, 0, pw - 8, ph - (bar ? 7 : 3), 13, BadgeColor(it), TextAnchor.LowerRight, true);
                bt.name = "badge"; bt.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.DropShadow(bt, 0.9f, 1f);
            }

            // weapon state (bottom left)
            if (d != null && d.type == ItemType.Weapon)
            {
                bool loaded = it.ammo + it.chamber > 0;
                var a = UIFactory.Caps(t, loaded ? "заряжен" : "пусто", 5, ph - 19, pw - 10, 16, 10, loaded ? UITheme.Text2 : UITheme.Red, TextAnchor.LowerLeft, 1.2f);
                a.name = "state";
                UIFactory.DropShadow(a, 0.85f, 1f);
            }

            AddStates(t, pw, ph, selected);
            return plate.rectTransform;
        }

        static void AddStates(Transform t, float pw, float ph, bool selected)
        {
            var hov = UIFactory.Img(t, "hov", UITheme.A(Color.white, 0.07f)); UIFactory.Stretch(hov.rectTransform);
            UIFactory.Border(hov.transform, UITheme.A(UITheme.Text1, 0.55f));
            hov.gameObject.SetActive(false);
            var sel = UIFactory.Img(t, "sel", UITheme.A(UITheme.Amber, 0.1f)); UIFactory.Stretch(sel.rectTransform);
            var se = UIFactory.Sliced(sel.transform, "edge", UIAssets.Frame2, UITheme.Amber); UIFactory.Stretch(se.rectTransform);
            sel.gameObject.SetActive(selected);
        }

        /// <summary>Shrink (13 -> 11) and then truncate a single-line label to fit maxW.</summary>
        public static void FitText(Text t, float maxW, int smaller = 11)
        {
            if (t == null || maxW <= 4f || UIFactory.TextWidth(t) <= maxW) return;
            if (t.fontSize > smaller) { t.fontSize = smaller; if (UIFactory.TextWidth(t) <= maxW) return; }
            string s = t.text;
            while (s.Length > 1)
            {
                s = s.Substring(0, s.Length - 1).TrimEnd('.', ' ');
                t.text = s + "…";
                if (UIFactory.TextWidth(t) <= maxW) return;
            }
        }

        public static void SetSelected(RectTransform tile, bool on)
        {
            if (tile == null) return;
            var s = tile.Find("sel"); if (s != null && s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
        }

        public static void SetHover(RectTransform tile, bool on)
        {
            if (tile == null) return;
            var s = tile.Find("hov"); if (s != null && s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
        }

        /// <summary>Short fade-in when a searched item is revealed.</summary>
        public static void Reveal(RectTransform tile)
        {
            if (tile == null) return;
            var f = tile.gameObject.AddComponent<UIFadeIn>(); f.duration = 0.3f;
        }

        /// <summary>Icon of an item definition / instance fitted (aspect kept) into a box; glyph fallback. Returns the image.</summary>
        public static Image FitIcon(Transform parent, string name, ItemDef d, ItemInstance it, float x, float y, float w, float h)
        {
            var im = UIFactory.Img(parent, name, Color.white);
            im.preserveAspect = true;
            UIFactory.Place(im.rectTransform, x, y, w, h);
            SetFitIcon(im, d, it);
            return im;
        }

        /// <summary>Update a FitIcon image (sprite or the category glyph when there is no rendered icon).</summary>
        public static void SetFitIcon(Image im, ItemDef d, ItemInstance it)
        {
            if (im == null) return;
            var sp = it != null ? IconSprite(it) : d != null ? UIAssets.ItemIcon(d.id) : null;
            if (sp != null) { im.sprite = sp; im.color = Color.white; im.enabled = true; return; }
            var g = d != null ? UIAssets.Glyph(UITheme.TypeGlyph(d.type)) : null;
            im.sprite = g; im.color = UITheme.A(UITheme.Text1, 0.4f); im.enabled = g != null;
        }
    }
}
