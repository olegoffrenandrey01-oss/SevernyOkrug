// Code-built uGUI helpers (v11 design system). Coordinates are in the 1920x1080 reference space, origin = TOP-LEFT of the parent.
// Palette / fonts: UITheme. Sprites: UIAssets. Button states: UIButtonFX (UIAnim.cs).
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Severny
{
    public static class UIFactory
    {
        // legacy colour names (older call sites) mapped onto the v11 palette
        public static readonly Color BgDark = UITheme.A(UITheme.Bg0, 0.96f);
        public static readonly Color Panel = UITheme.A(UITheme.Bg1, 0.94f);
        public static readonly Color PanelLight = UITheme.A(UITheme.Bg3, 0.95f);
        public static readonly Color Line = UITheme.Line1;
        public static readonly Color TextMain = UITheme.Text0;
        public static readonly Color TextDim = UITheme.Text1;
        public static readonly Color Accent = UITheme.Amber;
        public static readonly Color Olive = UITheme.Olive;
        public static readonly Color Red = UITheme.Red;
        public static readonly Color Green = UITheme.Green;

        /// <summary>Inventory cell size in reference units (the cell sprite is 64 px).</summary>
        public const float Cell = 64f;
        static Sprite _white;

        public static Sprite White
        {
            get
            {
                if (_white == null)
                {
                    var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "ui_white", hideFlags = HideFlags.DontSave };
                    var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                    t.SetPixels32(px); t.Apply(false, true);
                    _white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                }
                return _white;
            }
        }

        /// <summary>One inventory cell (tiled by Image.Type.Tiled).</summary>
        public static Sprite CellSprite => UIAssets.Cell;

        public static Canvas CreateCanvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            // v11: Expand - the whole 1920x1080 layout always fits (16:10, 4:3, 21:9, odd editor Game views); the extra space goes to the edges
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            s.referencePixelsPerUnit = 100f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// <summary>Top-left anchored rect: x,y from the parent's top-left corner (y grows downwards).</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Like Place (x,y,w,h in the 1920x1080 reference layout, y down) but pinned to a screen anchor
        /// (ax,ay: 0..1 from the top-left; 0.5,0.5 = centre) so it stays put on any aspect ratio (16:10, 21:9, editor Game view).</summary>
        public static RectTransform PlaceAt(RectTransform rt, float ax, float ay, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 1f - ay); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x - ax * 1920f, -(y - ay * 1080f)); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>A 1920x1080 node centred in the parent: lay screens out in reference coordinates, the surplus of
        /// wider / taller screens stays around it (backgrounds stretch to the full screen separately).</summary>
        public static RectTransform Center(Transform parent, string name = "Frame", float w = 1920f, float h = 1080f)
        {
            var rt = Node(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Img(Transform parent, string name, Color c, bool raycast = false)
        {
            var rt = Node(parent, name);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = White; im.color = c; im.raycastTarget = raycast;
            if (UITheme.Linear) rt.gameObject.AddComponent<UILinearAlpha>();   // v11.1: translucent colours look as designed in Linear space
            return im;
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color c, bool raycast = false)
        {
            var im = Img(parent, name, c, raycast); Place(im.rectTransform, x, y, w, h); return im;
        }

        /// <summary>Box with a 1 px outline: returns the outer image (edge colour), the fill is the child "fill".</summary>
        public static Image Frame(Transform parent, string name, float x, float y, float w, float h, Color fill, Color edge)
        {
            var o = Box(parent, name, x, y, w, h, edge);
            var i = Img(o.transform, "fill", fill); Stretch(i.rectTransform, 1, 1, 1, 1);
            return o;
        }

        /// <summary>9-sliced sprite image.</summary>
        public static Image Sliced(Transform parent, string name, Sprite s, Color c, bool raycast = false)
        {
            var im = Img(parent, name, c, raycast);
            im.sprite = s; im.type = Image.Type.Sliced; im.fillCenter = true;
            return im;
        }

        /// <summary>1 px outline over the whole parent (transparent centre).</summary>
        public static Image Border(Transform parent, Color c, string name = "border", float inset = 0f)
        {
            var im = Sliced(parent, name, UIAssets.Frame1, c); Stretch(im.rectTransform, inset, inset, inset, inset); return im;
        }

        /// <summary>Tiled pattern (noise, hatch, dots) over the parent.</summary>
        public static Image Pattern(Transform parent, string name, Sprite s, Color c, float scale = 1f)
        {
            var im = Img(parent, name, c);
            im.sprite = s; im.type = Image.Type.Tiled; im.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, scale);
            Stretch(im.rectTransform);
            return im;
        }

        /// <summary>Gradient overlay anchored to one edge: dir 0 = top (opaque at the top), 1 = bottom, 2 = left, 3 = right; size in units.</summary>
        public static Image Gradient(Transform parent, string name, int dir, float size, Color c)
        {
            var im = Img(parent, name, c);
            var rt = im.rectTransform;
            switch (dir)
            {
                case 0: rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1); rt.sizeDelta = new Vector2(0, size); break;
                case 1: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); rt.sizeDelta = new Vector2(0, size); break;
                case 2: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f); rt.sizeDelta = new Vector2(size, 0); break;
                default: rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 0.5f); rt.sizeDelta = new Vector2(size, 0); break;
            }
            rt.anchoredPosition = Vector2.zero;
            SetGradient(im, dir, c);
            return im;
        }

        /// <summary>Re-tints a Gradient image (dir as in Gradient) - use instead of setting .color.</summary>
        public static void SetGradient(Image im, int dir, Color c) => SetSoft(im, dir == 0 ? "fdown" : dir == 1 ? "fup" : dir == 2 ? "fright" : "fleft", c);

        /// <summary>Soft overlay sprite ("fdown", "fup", "fright", "fleft", "radial", "vign", "glow") tinted c (c.a = designed peak
        /// alpha). Linear colour space: the alpha falloff is baked, compensated, into a cached sprite and the Image colour keeps
        /// alpha 1 - so dark gradients / vignettes / shadows stay as black as designed. Keeps the image type (sliced glow).</summary>
        public static Image SetSoft(Image im, string shape, Color c)
        {
            if (im == null) return null;
            var s = UIAssets.Soft(shape, c);
            if (s == null) return im;
            im.sprite = s;
            im.color = UITheme.Linear && c.a > 0.001f ? UITheme.A(c, 1f) : c;
            return im;
        }

        /// <summary>Texture that fills the parent like "object-fit: cover" (uvRect cropped, no stretching).</summary>
        public static RawImage Cover(Transform parent, Texture tex, string name = "cover", float focusX = 0.5f, float focusY = 0.5f)
        {
            var ri = Node(parent, name).gameObject.AddComponent<RawImage>();
            ri.texture = tex; ri.raycastTarget = false; ri.color = Color.white;
            Stretch(ri.rectTransform);
            var cv = ri.gameObject.AddComponent<UICover>(); cv.focus = new Vector2(focusX, focusY); cv.Fit();
            return ri;
        }

        /// <summary>Standard surface: Bg1 fill, 1 px line, faint top highlight. Returns the panel rect.</summary>
        public static RectTransform PanelBox(Transform parent, string name, float x, float y, float w, float h, float alpha = 0.93f, bool raycast = false)
        {
            var bg = Box(parent, name, x, y, w, h, UITheme.A(UITheme.Bg1, alpha), raycast);
            Border(bg.transform, UITheme.Line0);
            var hl = Img(bg.transform, "hl", UITheme.A(Color.white, 0.04f));
            var rt = hl.rectTransform; rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1); rt.sizeDelta = new Vector2(-2, 1); rt.anchoredPosition = new Vector2(0, -1);
            return bg.rectTransform;
        }

        public static Image Divider(Transform parent, float x, float y, float w, Color? c = null) => Box(parent, "div", x, y, w, 1, c ?? UITheme.Line0);

        // ------------------------------------------------------------------ text
        public static Text Label(Transform parent, string text, int size, TextAnchor align, Color c, bool bold = false, string name = "Text")
        {
            var rt = Node(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            if (UITheme.Linear) rt.gameObject.AddComponent<UILinearAlpha>();
            t.font = bold ? UITheme.BodySemi : UITheme.Body;
            t.fontSize = size; t.alignment = align; t.color = c; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }

        public static Text Label(Transform parent, string text, float x, float y, float w, float h, int size, TextAnchor align, Color c, bool bold = false)
        {
            var t = Label(parent, text, size, align, c, bold); Place(t.rectTransform, x, y, w, h); return t;
        }

        /// <summary>Text with an explicit font.</summary>
        public static Text T(Transform parent, string text, float x, float y, float w, float h, Font font, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, string name = "Text")
        {
            var t = Label(parent, text, size, align, c, false, name);
            if (font != null) t.font = font;
            Place(t.rectTransform, x, y, w, h);
            return t;
        }

        /// <summary>Upper-case display text (Oswald) with letter spacing.</summary>
        public static Text Caps(Transform parent, string text, float x, float y, float w, float h, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, float tracking = 1.6f, Font font = null)
        {
            var t = T(parent, text != null ? text.ToUpperInvariant() : "", x, y, w, h, font != null ? font : UITheme.Display, size, c, align, "Caps");
            if (tracking != 0f) Track(t, tracking);
            return t;
        }

        /// <summary>Monospaced figures (IBM Plex Mono): money, timers, counts, codes.</summary>
        public static Text Mono(Transform parent, string text, float x, float y, float w, float h, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
            => T(parent, text, x, y, w, h, bold ? UITheme.MonoBold : UITheme.Mono, size, c, align, "Mono");

        public static UITracking Track(Text t, float spacing)
        {
            var tr = t.GetComponent<UITracking>();
            if (tr == null) tr = t.gameObject.AddComponent<UITracking>();
            tr.spacing = spacing;
            return tr;
        }

        /// <summary>Rendered width of a single-line text (including letter spacing).</summary>
        public static float TextWidth(Text t)
        {
            if (t == null || string.IsNullOrEmpty(t.text)) return 0f;
            float w = t.preferredWidth;
            var tr = t.GetComponent<UITracking>();
            if (tr != null && tr.enabled) w += tr.ExtraWidth(t.text, t.supportRichText);
            return w;
        }


        public static Shadow DropShadow(Graphic g, float a = 0.75f, float d = 1.5f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, UITheme.LinAlpha(Color.black, a)); s.effectDistance = new Vector2(d, -d);   // v11.1: Linear space
            return s;
        }

        // ------------------------------------------------------------------ icons, chips, keycaps
        /// <summary>White UI glyph (ui_glyphs atlas) tinted with c. Hidden when the glyph does not exist.</summary>
        public static Image Icon(Transform parent, string glyph, float x, float y, float size, Color c, string name = null)
        {
            var im = Img(parent, name ?? "Icon_" + glyph, c);
            im.preserveAspect = true;
            SetIcon(im, glyph);
            Place(im.rectTransform, x, y, size, size);
            return im;
        }

        public static void SetIcon(Image im, string glyph)
        {
            if (im == null) return;
            var sp = string.IsNullOrEmpty(glyph) ? null : UIAssets.Glyph(glyph);
            im.sprite = sp; im.enabled = sp != null;
        }

        /// <summary>Keyboard key ("F", "ESC", "TAB", "ЛКМ"). Returns its width.</summary>
        public static float Keycap(Transform parent, string key, float x, float y, float h = 26f, int size = 0)
        {
            var box = Sliced(parent, "Key_" + key, UIAssets.Round, UITheme.A(UITheme.Bg4, 0.96f));
            var edge = Sliced(box.transform, "edge", UIAssets.RoundLine, UITheme.Line2); Stretch(edge.rectTransform);
            var lip = Img(box.transform, "lip", UITheme.A(Color.black, 0.35f));
            var lrt = lip.rectTransform; lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 0); lrt.pivot = new Vector2(0.5f, 0); lrt.sizeDelta = new Vector2(-6, 2); lrt.anchoredPosition = new Vector2(0, 1);
            var t = Mono(box.transform, key, 0, 0, 10, h, size > 0 ? size : Mathf.RoundToInt(h * 0.5f), UITheme.Text0, TextAnchor.MiddleCenter, true);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = Mathf.Max(h, Mathf.Ceil(TextWidth(t) + h * 0.6f));
            Place(box.rectTransform, x, y, w, h);
            Stretch(t.rectTransform, 0, 0, 0, 1);
            return w;
        }

        /// <summary>Keycap followed by a caption. Returns the total width.</summary>
        public static float KeyHint(Transform parent, string key, string label, float x, float y, float h = 26f, int size = 16, Color? c = null)
        {
            float w = Keycap(parent, key, x, y, h);
            var t = T(parent, label, x + w + 8, y, 600, h, UITheme.Body, size, c ?? UITheme.Text1, TextAnchor.MiddleLeft, "Hint");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float tw = TextWidth(t);
            t.rectTransform.sizeDelta = new Vector2(tw + 4, h);
            return w + 8 + tw;
        }

        /// <summary>Small status chip (rounded, tinted). solid = filled with c and dark text.</summary>
        public static RectTransform Chip(Transform parent, string text, float x, float y, Color c, string glyph = null, float h = 22f, bool solid = false)
        {
            var bg = Sliced(parent, "Chip", UIAssets.Round, solid ? c : UITheme.A(c, 0.13f));
            if (!solid) { var e = Sliced(bg.transform, "edge", UIAssets.RoundLine, UITheme.A(c, 0.45f)); Stretch(e.rectTransform); }
            var ic = Icon(bg.transform, glyph, 7, (h - h * 0.58f) * 0.5f, h * 0.58f, solid ? UITheme.Ink : c, "icon");
            var t = Caps(bg.transform, text, 0, 0, 10, h, Mathf.RoundToInt(h * 0.56f), solid ? UITheme.Ink : c, TextAnchor.MiddleLeft, 1.1f);
            t.name = "label"; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(bg.rectTransform, x, y, 10, h);
            LayoutChip(bg.rectTransform);
            return bg.rectTransform;
        }

        /// <summary>Change a chip's text / colour and re-fit its width. Returns the new width.</summary>
        public static float SetChip(RectTransform chip, string text, Color? c = null, string glyph = null)
        {
            if (chip == null) return 0f;
            var t = chip.Find("label")?.GetComponent<Text>();
            var ic = chip.Find("icon")?.GetComponent<Image>();
            if (t != null) t.text = (text ?? "").ToUpperInvariant();
            if (glyph != null && ic != null) SetIcon(ic, glyph);
            if (c != null)
            {
                var bg = chip.GetComponent<Image>(); var e = chip.Find("edge")?.GetComponent<Image>();
                bool solid = e == null;
                if (bg != null) bg.color = solid ? c.Value : UITheme.A(c.Value, 0.13f);
                if (e != null) e.color = UITheme.A(c.Value, 0.45f);
                if (t != null) t.color = solid ? UITheme.Ink : c.Value;
                if (ic != null) ic.color = solid ? UITheme.Ink : c.Value;
            }
            return LayoutChip(chip);
        }

        static float LayoutChip(RectTransform chip)
        {
            float h = chip.sizeDelta.y;
            var t = chip.Find("label")?.GetComponent<Text>();
            var ic = chip.Find("icon")?.GetComponent<Image>();
            float x = h * 0.42f;
            if (ic != null && ic.enabled) x = 7 + h * 0.58f + 5;
            float tw = t != null ? TextWidth(t) : 0f;
            if (t != null) Place(t.rectTransform, x, 0, tw + 2, h);
            float w = Mathf.Ceil(x + tw + h * 0.42f);
            chip.sizeDelta = new Vector2(w, h);
            return w;
        }

        /// <summary>Section header: amber tick + caps title + hairline below; returns the right-aligned text (empty).</summary>
        public static Text Header(Transform parent, string title, float x, float y, float w, string right = null, Color? titleColor = null)
        {
            Box(parent, "tick", x, y + 8, 3, 12, UITheme.Amber);
            var t = Caps(parent, title, x + 12, y, w - 12, 28, 15, titleColor ?? UITheme.Text1, TextAnchor.MiddleLeft, 2.2f);
            t.name = "Header_" + title;
            Box(parent, "line", x, y + 30, w, 1, UITheme.Line0);
            var r = Mono(parent, right ?? "", x, y, w, 28, 13, UITheme.Text2, TextAnchor.MiddleRight);
            r.name = "HeaderRight";
            return r;
        }

        // ------------------------------------------------------------------ buttons
        /// <summary>Legacy signature: secondary / primary button (text size is the old Liberation size; mapped to the display font).</summary>
        public static Button Button(Transform parent, string text, float x, float y, float w, float h, Action onClick, int size = 26, bool primary = false)
            => Button(parent, text, x, y, w, h, onClick, primary ? BtnStyle.Primary : BtnStyle.Secondary, null, Mathf.RoundToInt(size * 0.8f));

        /// <summary>v11 button. The GameObject is "Btn_" + text, its first Text child is the label (SetButtonText).</summary>
        public static Button Button(Transform parent, string text, float x, float y, float w, float h, Action onClick, BtnStyle style, string glyph = null, int size = 0)
        {
            var root = Box(parent, "Btn_" + text, x, y, w, h, new Color(0f, 0f, 0f, 0f), true);   // transparent raycast target
            var fx = root.gameObject.AddComponent<UIButtonFX>();
            fx.style = style;
            if (size <= 0)
                size = style == BtnStyle.Menu ? 30 : style == BtnStyle.Tab ? 16 : style == BtnStyle.Segment ? 15 : Mathf.RoundToInt(Mathf.Clamp(h * 0.34f, 15f, 26f));

            switch (style)
            {
                case BtnStyle.Primary:
                    fx.glow = Sliced(root.transform, "glow", UIAssets.Glow, UITheme.A(UITheme.Amber, 0.1f)); Stretch(fx.glow.rectTransform, -18, -18, -18, -18);
                    fx.bg = Sliced(root.transform, "bg", UIAssets.Chamfer, UITheme.Amber); Stretch(fx.bg.rectTransform);
                    fx.edge = Sliced(root.transform, "edge", UIAssets.ChamferLine, UITheme.AmberHi); Stretch(fx.edge.rectTransform);
                    break;
                case BtnStyle.Danger:
                case BtnStyle.Secondary:
                case BtnStyle.Segment:
                case BtnStyle.Row:
                case BtnStyle.Card:
                    fx.bg = Img(root.transform, "bg", UITheme.Bg3); Stretch(fx.bg.rectTransform);
                    fx.edge = Border(root.transform, UITheme.Line1, "edge");
                    if (style == BtnStyle.Card)
                    {   // selection: 2 px amber frame
                        fx.accent = Sliced(root.transform, "accent", UIAssets.Frame2, new Color(0, 0, 0, 0)); Stretch(fx.accent.rectTransform);
                    }
                    else if (style != BtnStyle.Segment)
                    {   // hover: 2 px amber bar on the left edge
                        fx.accent = Img(root.transform, "accent", new Color(0, 0, 0, 0));
                        var a = fx.accent.rectTransform; a.anchorMin = new Vector2(0, 0); a.anchorMax = new Vector2(0, 1); a.pivot = new Vector2(0, 0.5f);
                        a.sizeDelta = new Vector2(2, -2); a.anchoredPosition = new Vector2(1, 0);
                    }
                    break;
                case BtnStyle.Ghost:
                    fx.bg = Img(root.transform, "bg", new Color(0, 0, 0, 0)); Stretch(fx.bg.rectTransform);
                    break;
                case BtnStyle.Tab:
                    fx.bg = Img(root.transform, "bg", new Color(0, 0, 0, 0)); Stretch(fx.bg.rectTransform);
                    fx.accent = Img(root.transform, "accent", new Color(0, 0, 0, 0));
                    { var a = fx.accent.rectTransform; a.anchorMin = new Vector2(0, 0); a.anchorMax = new Vector2(1, 0); a.pivot = new Vector2(0.5f, 0); a.sizeDelta = new Vector2(0, 2); a.anchoredPosition = Vector2.zero; }
                    break;
                case BtnStyle.Menu:
                    fx.bg = Sliced(root.transform, "bg", UIAssets.FadeRight, new Color(0, 0, 0, 0)); fx.bg.type = Image.Type.Simple; Stretch(fx.bg.rectTransform);
                    fx.accent = Img(root.transform, "accent", new Color(0, 0, 0, 0));
                    { var a = fx.accent.rectTransform; a.anchorMin = new Vector2(0, 0); a.anchorMax = new Vector2(0, 1); a.pivot = new Vector2(0, 0.5f); a.sizeDelta = new Vector2(3, -14); a.anchoredPosition = Vector2.zero; }
                    break;
            }

            if (style == BtnStyle.Menu)
            {
                fx.sub = Mono(root.transform, "", 22, 0, 40, h, 13, UITheme.Text3, TextAnchor.MiddleLeft);
                fx.sub.name = "num";
                var slide = Node(root.transform, "slide");
                slide.anchorMin = slide.anchorMax = new Vector2(1f, 0.5f); slide.pivot = new Vector2(1f, 0.5f); slide.sizeDelta = new Vector2(22, 22);
                fx.icon = Img(slide, "arrow", new Color(0, 0, 0, 0)); Stretch(fx.icon.rectTransform); fx.icon.preserveAspect = true;
                SetIcon(fx.icon, glyph ?? "chev_r");
                fx.slide = slide; fx.slideFrom = -34f; fx.slideTo = -18f;
                slide.anchoredPosition = new Vector2(-34f, 0f);
            }
            else if (!string.IsNullOrEmpty(glyph))
            {
                fx.icon = Img(root.transform, "icon", UITheme.Text1); fx.icon.preserveAspect = true;
                SetIcon(fx.icon, glyph);
            }

            // label (the first Text child of the button)
            var lab = Label(root.transform, text, size, TextAnchor.MiddleCenter, UITheme.Text0, false, "label");
            lab.font = style == BtnStyle.Row ? UITheme.BodyMedium : UITheme.Display;
            lab.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (style != BtnStyle.Row) Track(lab, style == BtnStyle.Menu ? 2.2f : 1.4f);
            if (style == BtnStyle.Menu) { lab.alignment = TextAnchor.MiddleLeft; Stretch(lab.rectTransform, 64, 0, 40, 2); }
            else if (style == BtnStyle.Row) { lab.alignment = TextAnchor.MiddleLeft; Stretch(lab.rectTransform, 16, 0, 12, 0); }
            else Stretch(lab.rectTransform, 10, 0, 10, 1);
            // sub / number text must come after the label (GetComponentInChildren<Text>() = label)
            if (fx.sub != null) fx.sub.transform.SetAsLastSibling();

            fx.label = lab;
            var b = root.gameObject.AddComponent<Button>();
            b.targetGraphic = root;
            b.transition = Selectable.Transition.None;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            fx.button = b;
            LayoutButton(fx);
            fx.Apply();
            b.onClick.AddListener(() => { SFX.Play2D(SFX.UIClick, 0.5f, 0.05f); onClick?.Invoke(); });
            return b;
        }

        /// <summary>Second line under a button label (e.g. "Ctrl+клик", map name); moves the label up.</summary>
        public static Text ButtonSub(Button b, string sub, int size = 13)
        {
            var fx = b.GetComponent<UIButtonFX>();
            var rt = (RectTransform)b.transform; float h = rt.sizeDelta.y;
            var t = Label(b.transform, sub, size, fx != null && fx.label != null ? fx.label.alignment : TextAnchor.MiddleCenter, UITheme.Text2, false, "sub");
            t.font = UITheme.BodyMedium; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (fx != null)
            {
                fx.sub = t;
                if (fx.label != null)
                {
                    var l = fx.label.rectTransform;
                    l.offsetMax = new Vector2(l.offsetMax.x, -h * 0.14f); l.offsetMin = new Vector2(l.offsetMin.x, h * 0.36f);
                    var s = t.rectTransform; s.anchorMin = Vector2.zero; s.anchorMax = Vector2.one; s.pivot = new Vector2(0.5f, 0.5f);
                    s.offsetMin = new Vector2(l.offsetMin.x, h * 0.12f); s.offsetMax = new Vector2(l.offsetMax.x, -h * 0.58f);
                }
                fx.Refresh();
            }
            return t;
        }

        /// <summary>Centre the icon + label group (buttons with a glyph).</summary>
        static void LayoutButton(UIButtonFX fx)
        {
            if (fx == null || fx.icon == null || fx.label == null || fx.style == BtnStyle.Menu) return;
            var rt = (RectTransform)fx.transform; float w = rt.sizeDelta.x, h = rt.sizeDelta.y;
            float isz = Mathf.Round(Mathf.Clamp(h * 0.4f, 14f, 24f));
            bool hasText = !string.IsNullOrEmpty(fx.label.text);
            if (!hasText)
            {
                var ir = fx.icon.rectTransform; ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0.5f, 0.5f); ir.sizeDelta = new Vector2(isz, isz); ir.anchoredPosition = Vector2.zero;
                return;
            }
            if (fx.style == BtnStyle.Row)
            {
                Place(fx.icon.rectTransform, 14, (h - isz) * 0.5f, isz, isz);
                Stretch(fx.label.rectTransform, 14 + isz + 10, 0, 12, 0);
                return;
            }
            float tw = TextWidth(fx.label), gap = 9f, total = isz + gap + tw;
            float x0 = Mathf.Max(10f, (w - total) * 0.5f);
            Place(fx.icon.rectTransform, x0, (h - isz) * 0.5f, isz, isz);
            var l = fx.label.rectTransform; l.anchorMin = new Vector2(0, 0); l.anchorMax = new Vector2(0, 1); l.pivot = new Vector2(0, 0.5f);
            l.sizeDelta = new Vector2(tw + 6, -1); l.anchoredPosition = new Vector2(x0 + isz + gap, 0.5f);
            fx.label.alignment = TextAnchor.MiddleLeft;
        }

        public static void SetButtonText(Button b, string s)
        {
            if (b == null) return;
            var fx = b.GetComponent<UIButtonFX>();
            var t = fx != null && fx.label != null ? fx.label : b.GetComponentInChildren<Text>();
            if (t != null) t.text = s;
            if (fx != null) { LayoutButton(fx); fx.Refresh(); }
        }

        /// <summary>Mark a button selected (tabs, segments, cards).</summary>
        public static void SetSelected(Button b, bool on)
        {
            if (b == null) return;
            var fx = b.GetComponent<UIButtonFX>(); if (fx == null) return;
            if (fx.selected != on) { fx.selected = on; fx.Refresh(); }
        }

        /// <summary>Row of segment buttons (one is selected via SelectSegment).</summary>
        public static Button[] Segmented(Transform parent, float x, float y, float w, float h, string[] labels, string[] glyphs, Action<int> pick, int size = 15)
        {
            int n = labels.Length; float gap = 6f, bw = (w - gap * (n - 1)) / n;
            var arr = new Button[n];
            for (int i = 0; i < n; i++)
            {
                int k = i;
                arr[i] = Button(parent, labels[i], x + i * (bw + gap), y, bw, h, () => pick(k), BtnStyle.Segment, glyphs != null && i < glyphs.Length ? glyphs[i] : null, size);
            }
            return arr;
        }

        public static void SelectSegment(Button[] seg, int index) { if (seg == null) return; for (int i = 0; i < seg.Length; i++) SetSelected(seg[i], i == index); }

        // ------------------------------------------------------------------ bars, scroll, option rows
        /// <summary>Bar (background + fill). Returns the fill image (use SetBar).</summary>
        public static Image Bar(Transform parent, float x, float y, float w, float h, Color fillColor)
        {
            var bg = Box(parent, "Bar", x, y, w, h, UITheme.A(UITheme.Bg0, 0.8f));
            var f = Img(bg.transform, "fill", fillColor);
            var rt = f.rectTransform; rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return f;
        }

        public static void SetBar(Image fill, float t)
        {
            var rt = fill.rectTransform; rt.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
        }

        /// <summary>Vertical scroll view; returns the content node (top-left anchored, set its height).</summary>
        public static RectTransform ScrollView(Transform parent, float x, float y, float w, float h, out ScrollRect sr)
        {
            var root = Box(parent, "Scroll", x, y, w, h, UITheme.A(UITheme.Bg0, 0.3f), true);
            sr = root.gameObject.AddComponent<ScrollRect>();
            var vp = Node(root.transform, "Viewport"); Stretch(vp, 0, 0, 12, 0);
            vp.gameObject.AddComponent<RectMask2D>();
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.01f); vpImg.sprite = White;
            var content = Node(vp, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, h);
            // slim scrollbar: 12 units of hit area, 2 px track, 4 px handle
            var sbBg = Img(root.transform, "Scrollbar", new Color(0, 0, 0, 0), true);
            var srt = sbBg.rectTransform; srt.anchorMin = new Vector2(1, 0); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(1, 0.5f);
            srt.sizeDelta = new Vector2(12, -8); srt.anchoredPosition = Vector2.zero;
            var track = Img(sbBg.transform, "track", UITheme.A(UITheme.Line0, 0.9f));
            var trt = track.rectTransform; trt.anchorMin = new Vector2(0.5f, 0); trt.anchorMax = new Vector2(0.5f, 1); trt.pivot = new Vector2(0.5f, 0.5f); trt.sizeDelta = new Vector2(2, 0); trt.anchoredPosition = Vector2.zero;
            var area = Node(sbBg.transform, "Sliding"); Stretch(area);
            var handle = Img(area, "Handle", UITheme.Line2, true);
            var hrt = handle.rectTransform; hrt.anchorMin = new Vector2(0.33f, 0); hrt.anchorMax = new Vector2(0.67f, 1); hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            var sb = sbBg.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = hrt; sb.targetGraphic = handle; sb.direction = Scrollbar.Direction.BottomToTop;
            var cb = sb.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.5f, 1.4f, 1.1f, 1f); cb.pressedColor = new Color(1.8f, 1.5f, 1f, 1f); cb.selectedColor = Color.white; cb.colorMultiplier = 1.6f; sb.colors = cb;
            var nav = sb.navigation; nav.mode = Navigation.Mode.None; sb.navigation = nav;
            sr.viewport = vp; sr.content = content; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 45f; sr.inertia = false;
            sr.verticalScrollbar = sb; sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        /// <summary>"Label   ‹ value ›" option row. getText returns the current value text; change(+1/-1).</summary>
        public static Text OptionRow(Transform parent, string label, float x, float y, float w, Func<string> getText, Action<int> change)
            => Stepper(parent, label, null, x, y, w, 52, getText, change, null);

        /// <summary>Settings row: label (+ optional hint line), ‹ value › stepper and an optional position bar (frac 0..1).</summary>
        public static Text Stepper(Transform parent, string label, string hint, float x, float y, float w, float h, Func<string> getText, Action<int> change, Func<float> frac)
        {
            var row = Node(parent, "Row_" + label); Place(row, x, y, w, h);
            var bg = Img(row, "bg", UITheme.A(UITheme.Bg2, 0.82f)); Stretch(bg.rectTransform);
            Box(row, "line", 0, h - 1, w, 1, UITheme.Line0);
            if (string.IsNullOrEmpty(hint)) T(row, label, 20, 0, w - 360, h, UITheme.BodyMedium, 18, UITheme.Text0, TextAnchor.MiddleLeft, "label");
            else
            {
                T(row, label, 20, h * 0.5f - 24, w - 360, 26, UITheme.BodyMedium, 18, UITheme.Text0, TextAnchor.MiddleLeft, "label");
                T(row, hint, 20, h * 0.5f + 1, w - 360, 22, UITheme.Body, 14, UITheme.Text2, TextAnchor.MiddleLeft, "hint");
            }
            Text val = null; Image bar = null;
            void Upd() { val.text = getText(); if (bar != null) SetBar(bar, frac()); }
            float bs = 36f, vx = w - 300f;
            var prev = Button(row, "", vx, (h - bs) * 0.5f, bs, bs, () => { change(-1); Upd(); }, BtnStyle.Secondary, "chev_l");
            prev.name = "Btn_Prev";
            val = T(row, getText(), vx + bs, 0, 300f - 2 * bs - 14f, h - (frac != null ? 6 : 0), UITheme.BodySemi, 18, UITheme.AmberHi, TextAnchor.MiddleCenter, "value");
            var next = Button(row, "", w - 14f - bs, (h - bs) * 0.5f, bs, bs, () => { change(+1); Upd(); }, BtnStyle.Secondary, "chev_r");
            next.name = "Btn_Next";
            if (frac != null)
            {
                bar = Bar(row, vx + bs + 30, h * 0.5f + 13, 300f - 2 * bs - 14f - 60, 2, UITheme.Amber);
                bar.transform.parent.GetComponent<Image>().color = UITheme.Line1;
                SetBar(bar, frac());
            }
            return val;
        }

        // ------------------------------------------------------------------ input helpers
        public static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        public static bool Contains(RectTransform rt, Vector2 screen)
        {
            return rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
        }

        /// <summary>Screen point -> top-left based local coordinates of rt (reference units).</summary>
        public static Vector2 LocalTopLeft(RectTransform rt, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out var lp);
            var r = rt.rect;
            return new Vector2(lp.x - r.xMin, r.yMax - lp.y);
        }

        public static string Money(int v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ") + " руб.";
    }
}
