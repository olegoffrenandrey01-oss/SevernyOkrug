// v11 UI art: procedural 9-slice / tiling sprites (crisp 1 px lines at any UI scale, generated once at runtime)
// plus the pre-rendered atlases in Resources/SevernyUI (item icons, UI glyphs, body silhouette, map images).
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public static class UIAssets
    {
        // ------------------------------------------------------------------ procedural helpers
        delegate float AlphaFn(float x, float y);   // pixel centre -> alpha (white sprites, tinted by Image.color)

        static Texture2D NewTex(int w, int h, string name, bool repeat)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave };
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        static Sprite Make(string name, int w, int h, float border, AlphaFn f, bool repeat = false)
        {
            var t = NewTex(w, h, "ui_" + name, repeat);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(f(x + 0.5f, y + 0.5f) * 255f), 0, 255));
            t.SetPixels32(px); t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static Sprite MakeRgba(string name, int w, int h, System.Func<int, int, Color32> f, bool repeat, float border = 0f)
        {
            var t = NewTex(w, h, "ui_" + name, repeat);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels32(px); t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static float Sat(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Smooth(float a, float b, float v) { float t = Sat((v - a) / (b - a)); return t * t * (3f - 2f * t); }
        /// <summary>Signed distance to a rounded box centred at the origin (half size hx, hy, corner radius r).</summary>
        static float SdBox(float px, float py, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px) - (hx - r), qy = Mathf.Abs(py) - (hy - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
        /// <summary>Box with the top-left and bottom-right corners cut at 45 degrees (y up).</summary>
        static float SdChamfer(float px, float py, float hx, float hy, float c)
        {
            float d = SdBox(px, py, hx, hy, 0f);
            if ((px < 0f && py > 0f) || (px > 0f && py < 0f)) d = Mathf.Max(d, (Mathf.Abs(px) + Mathf.Abs(py) - (hx + hy - c)) * 0.7071f);
            return d;
        }
        static float Fill(float sd) => Sat(0.5f - sd);
        static float Ring(float sd, float t) => Sat(0.5f - sd) - Sat(0.5f - (sd + t));

        static readonly Dictionary<string, Sprite> _proc = new Dictionary<string, Sprite>();
        static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (_proc.TryGetValue(key, out var s) && s != null) return s;
            s = make(); _proc[key] = s; return s;
        }

        // ------------------------------------------------------------------ procedural sprites
        /// <summary>1 px outline, transparent centre (9-slice).</summary>
        public static Sprite Frame1 => Get("frame1", () => Make("frame1", 8, 8, 3, (x, y) => x < 1 || y < 1 || x > 7 || y > 7 ? 1f : 0f));
        /// <summary>2 px outline (selection).</summary>
        public static Sprite Frame2 => Get("frame2", () => Make("frame2", 8, 8, 3, (x, y) => x < 2 || y < 2 || x > 6 || y > 6 ? 1f : 0f));
        /// <summary>Rounded box r=3 (chips, keycaps, badges).</summary>
        public static Sprite Round => Get("round", () => Make("round", 16, 16, 6, (x, y) => Fill(SdBox(x - 8f, y - 8f, 8f, 8f, 3f))));
        public static Sprite RoundLine => Get("roundl", () => Make("roundl", 16, 16, 6, (x, y) => Ring(SdBox(x - 8f, y - 8f, 8f, 8f, 3f), 1f)));
        /// <summary>Fully rounded pill (radius 12).</summary>
        public static Sprite Pill => Get("pill", () => Make("pill", 26, 26, 12, (x, y) => Fill(SdBox(x - 13f, y - 13f, 13f, 13f, 12.5f))));
        /// <summary>Chamfered box (cut top-left / bottom-right corners) - primary actions, cards.</summary>
        public static Sprite Chamfer => Get("chamf", () => Make("chamf", 32, 32, 11, (x, y) => Fill(SdChamfer(x - 16f, y - 16f, 16f, 16f, 9f))));
        public static Sprite ChamferLine => Get("chamfl", () => Make("chamfl", 32, 32, 11, (x, y) => Ring(SdChamfer(x - 16f, y - 16f, 16f, 16f, 9f), 1f)));
        /// <summary>Gradients (alpha): FadeDown = opaque top -> clear bottom, FadeUp = clear top -> opaque bottom, FadeRight = opaque left -> clear right.</summary>
        public static Sprite FadeDown => Soft("fdown");
        public static Sprite FadeUp => Soft("fup");
        public static Sprite FadeRight => Soft("fright");
        public static Sprite FadeLeft => Soft("fleft");
        /// <summary>Soft radial blob (centre opaque).</summary>
        public static Sprite Radial => Soft("radial");
        /// <summary>Screen vignette (clear centre, opaque corners).</summary>
        public static Sprite Vignette => Soft("vign");
        /// <summary>Film grain tile (grey noise, use with low alpha, Image.Type.Tiled). v11.1: no longer used by the screens.</summary>
        public static Sprite Noise => Get("noise", () =>
        {
            var r = new System.Random(1337);
            return MakeRgba("noise", 128, 128, (x, y) => { byte g = (byte)r.Next(0, 256); return new Color32(g, g, g, 255); }, true);
        });
        /// <summary>Diagonal hazard hatch tile (Tiled).</summary>
        public static Sprite Hatch => Get("hatch", () => Make("hatch", 16, 16, 0, (x, y) => { float m = (x + y) % 8f; return Sat(1.6f - Mathf.Abs(m - 1.6f)); }, true));
        /// <summary>Fine dot raster tile (Tiled) - subtle panel texture.</summary>
        public static Sprite Dots => Get("dots", () => Make("dots", 6, 6, 0, (x, y) => (x > 2f && x < 4f && y > 2f && y < 4f) ? 1f : 0f, true));
        /// <summary>Inventory cell (64 px, engraved 1+1 px lines, slightly lit centre). Opaque colours baked in - use white.</summary>
        public static Sprite Cell => Get("cell", () => MakeRgba("cell", 64, 64, (x, y) =>
        {
            if (y == 63 || x == 0) return new Color32(52, 57, 50, 255);          // top / left: light edge
            if (y == 0 || x == 63) return new Color32(9, 10, 9, 255);            // bottom / right: shadow
            float u = (x - 31.5f) / 31.5f, v = (y - 31.5f) / 31.5f; float d = Mathf.Sqrt(u * u + v * v);
            int g = Mathf.RoundToInt(Mathf.Lerp(25f, 19f, Sat(d * 0.8f)));
            bool tick = (x <= 3 && y >= 60) || (x <= 1 && y >= 58);               // tiny corner tick
            if (tick) g += 9;
            return new Color32((byte)g, (byte)(g + 2), (byte)g, 244);
        }, true));
        /// <summary>Corner brackets (selection / focus), 9-slice.</summary>
        public static Sprite Brackets => Get("brk", () => Make("brk", 24, 24, 9, (x, y) =>
        {
            bool cx = x < 2f || x > 22f, cy = y < 2f || y > 22f;
            bool nearX = x < 8f || x > 16f, nearY = y < 8f || y > 16f;
            return ((cx && nearY) || (cy && nearX)) ? 1f : 0f;
        }));
        /// <summary>Filled right triangle in the top-left corner (rarity notch on item tiles).</summary>
        public static Sprite CornerTL => Get("cornertl", () => Make("cornertl", 16, 16, 0, (x, y) => Sat(16.5f - (x + (16f - y)))));
        public static Sprite Circle => Get("circle", () => Make("circle", 64, 64, 0, (x, y) => Fill(Mathf.Sqrt((x - 32f) * (x - 32f) + (y - 32f) * (y - 32f)) - 31f)));
        public static Sprite RingSprite => Get("ring", () => Make("ring", 64, 64, 0, (x, y) => Ring(Mathf.Sqrt((x - 32f) * (x - 32f) + (y - 32f) * (y - 32f)) - 31f, 3f)));
        /// <summary>Soft glow / drop shadow around a box (9-slice, inner area opaque).</summary>
        public static Sprite Glow => Soft("glow");

        // ------------------------------------------------------------------ soft overlays (+ Linear colour space variants)
        static bool SoftShape(string shape, out int w, out int h, out float border, out AlphaFn f)
        {
            w = h = 4; border = 0f; f = null;
            switch (shape)
            {
                case "fdown": w = 4; h = 128; f = (x, y) => Smooth(0f, 1f, y / 128f); return true;
                case "fup": w = 4; h = 128; f = (x, y) => Smooth(0f, 1f, 1f - y / 128f); return true;
                case "fright": w = 128; h = 4; f = (x, y) => Smooth(0f, 1f, 1f - x / 128f); return true;
                case "fleft": w = 128; h = 4; f = (x, y) => Smooth(0f, 1f, x / 128f); return true;
                case "radial":
                    w = h = 128;
                    f = (x, y) => { float d = Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f)) / 64f; float a = 1f - Smooth(0f, 1f, d); return a * a; };
                    return true;
                case "vign":
                    w = h = 256;
                    f = (x, y) =>
                    {
                        float u = (x / 256f - 0.5f) * 2f, v = (y / 256f - 0.5f) * 2f; float d = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                        return Mathf.Pow(Smooth(0.38f, 1.02f, d), 1.35f);
                    };
                    return true;
                case "glow":
                    w = h = 64; border = 30f;
                    f = (x, y) =>
                    {
                        float dx = Mathf.Max(0f, Mathf.Abs(x - 32f) - 2f), dy = Mathf.Max(0f, Mathf.Abs(y - 32f) - 2f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / 30f; float a = 1f - Smooth(0f, 1f, d); return a * a;
                    };
                    return true;
            }
            return false;
        }

        /// <summary>Plain soft sprite: "fdown", "fup", "fright", "fleft", "radial", "vign", "glow" (white, alpha = shape).</summary>
        public static Sprite Soft(string shape)
        {
            if (!SoftShape(shape, out int w, out int h, out float border, out AlphaFn f)) return null;
            return Get(shape, () => Make(shape, w, h, border, f));
        }

        /// <summary>Soft sprite for the tint c, whose alpha is the designed peak alpha of the overlay. Linear colour space: a cached
        /// variant whose per-pixel alpha already is the compensated c.a x shape (draw it with the Image colour at alpha 1), so the
        /// gradient keeps its designed falloff; Gamma: the plain sprite (draw with c). Use through UIFactory.SetSoft.</summary>
        public static Sprite Soft(string shape, Color c)
        {
            if (!UITheme.Linear || c.a <= 0.001f) return Soft(shape);
            if (!SoftShape(shape, out int w, out int h, out float border, out AlphaFn f)) return null;
            int qa = Mathf.Clamp(Mathf.RoundToInt(c.a * 100f), 1, 100), ql = Mathf.RoundToInt(UITheme.Luma(c) * 50f);
            float pa = qa / 100f, pl = ql / 50f;
            var tone = new Color(pl, pl, pl, 1f);
            string key = shape + "@" + qa + "_" + ql;
            return Get(key, () => Make(key, w, h, border, (x, y) => UITheme.LinAlpha(tone, pa * f(x, y))));
        }

        // ------------------------------------------------------------------ atlases (Resources/SevernyUI)
        class Atlas
        {
            public Texture2D tex; public readonly Dictionary<string, Rect> rects = new Dictionary<string, Rect>();
            public readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        }
        static Atlas _icons, _glyphs;

        static Atlas Load(string name)
        {
            var a = new Atlas();
            a.tex = Resources.Load<Texture2D>("SevernyUI/" + name);
            var idx = Resources.Load<TextAsset>("SevernyUI/" + name);
            if (a.tex == null || idx == null) { if (a.tex == null) Debug.LogWarning("[Severny] UI atlas missing: Resources/SevernyUI/" + name); return a; }
            float srcW = a.tex.width, srcH = a.tex.height;
            foreach (var raw in idx.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var p = line.Split(' ');
                if (line[0] == '#')
                {   // "# ... atlas W H ..." : the size the index was written for (the importer may downscale the texture)
                    int ai = System.Array.IndexOf(p, "atlas");
                    if (ai >= 0 && ai + 2 < p.Length && float.TryParse(p[ai + 1], out var w0) && float.TryParse(p[ai + 2], out var h0)) { srcW = w0; srcH = h0; }
                    continue;
                }
                if (p.Length < 5) continue;
                float x = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), y = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
                float w = float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), h = float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture);
                float kx = a.tex.width / srcW, ky = a.tex.height / srcH;
                a.rects[p[0]] = new Rect(x * kx, (srcH - y - h) * ky, w * kx, h * ky);   // index is top-left based, Unity rects bottom-left
            }
            return a;
        }

        static Sprite From(Atlas a, string id)
        {
            if (a == null || a.tex == null || string.IsNullOrEmpty(id)) return null;
            if (a.sprites.TryGetValue(id, out var s)) return s;
            if (!a.rects.TryGetValue(id, out var r)) { a.sprites[id] = null; return null; }
            s = Sprite.Create(a.tex, r, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = id; a.sprites[id] = s; return s;
        }

        /// <summary>White UI glyph (tint with Image.color): "close", "check", "chev_r", "lock", "rifle", "med", ... (see ui_glyphs.txt).</summary>
        public static Sprite Glyph(string name) { if (_glyphs == null) _glyphs = Load("ui_glyphs"); return From(_glyphs, name); }

        static readonly Dictionary<string, Sprite> _v14Icons = new Dictionary<string, Sprite>();
        static Sprite V14Icon(string id, bool emptyVariant)
        {
            if (V14WeaponArt.ModelTag(id) == null) return null;
            string key = id + (emptyVariant ? "@empty" : "");
            if (_v14Icons.TryGetValue(key, out var sprite)) return sprite;
            var texture = Resources.Load<Texture2D>("SevernyUI/V14/" + key);
            if (texture != null)
            {
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = key;
            }
            _v14Icons[key] = sprite;
            return sprite;
        }

        /// <summary>Pre-rendered 3D item icon (128 px per inventory cell). emptyVariant: "av74@empty" (no magazine) when available.</summary>
        public static Sprite ItemIcon(string id, bool emptyVariant = false)
        {
            var custom = V14Icon(id, emptyVariant) ?? (emptyVariant ? V14Icon(id, false) : null);
            if (custom != null) return custom;
            if (_icons == null) _icons = Load("item_icons");
            if (emptyVariant) { var e = From(_icons, id + "@empty"); if (e != null) return e; }
            return From(_icons, id);
        }

        /// <summary>Location key art (Resources/SevernyUI/map_&lt;id&gt;), null when there is none.</summary>
        public static Texture2D MapImage(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            if (_maps.TryGetValue(mapId, out var t)) return t;
            t = Resources.Load<Texture2D>("SevernyUI/map_" + mapId); _maps[mapId] = t; return t;
        }
        static readonly Dictionary<string, Texture2D> _maps = new Dictionary<string, Texture2D>();
    }
}
