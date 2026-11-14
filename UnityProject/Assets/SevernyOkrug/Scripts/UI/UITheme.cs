// v11 design system: palette, typography, rarity and item-category colours shared by every screen.
// Graphite / olive surfaces, muted amber as the primary accent, orange/red for danger, desaturated green for "ok".
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    public static class UITheme
    {
        public static Color H(int rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        public static string Tint(string s, Color c) => "<color=#" + Hex(c) + ">" + s + "</color>";

        // ---- surfaces (darkest -> lightest)
        public static readonly Color Bg0 = H(0x0A0B0A), Bg1 = H(0x101210), Bg2 = H(0x161916), Bg3 = H(0x1D211D), Bg4 = H(0x262B25);
        // ---- lines
        public static readonly Color Line0 = H(0x262B25), Line1 = H(0x363C34), Line2 = H(0x535B4F);
        // ---- text
        public static readonly Color Text0 = H(0xE6E2D6), Text1 = H(0xA9AA9E), Text2 = H(0x6E7268), Text3 = H(0x4A4E46);
        // ---- accents
        public static readonly Color Amber = H(0xD29B3C), AmberHi = H(0xEEB75A), AmberLo = H(0x7E5C24);
        public static readonly Color Orange = H(0xC8642F), Red = H(0xD9553A), RedLo = H(0x4A1A13);
        public static readonly Color Green = H(0x86A85E), GreenLo = H(0x2C3A20), Olive = H(0x6B7A45), OliveLo = H(0x283020), Steel = H(0x5A86A8);
        public static readonly Color Ink = H(0x0E0F0D);   // text on amber

        // ---- rarity (ItemDef.rarity 1..5)
        static readonly Color[] _rarity = { H(0x7C7F77), H(0x7C7F77), H(0x7E9A57), H(0x5A86A8), H(0xC9A24B), H(0xD0683C) };
        static readonly string[] _rarityName = { "Обычный", "Обычный", "Необычный", "Редкий", "Очень редкий", "Исключительный" };
        public static Color Rarity(int r) => _rarity[Mathf.Clamp(r, 0, 5)];
        public static string RarityName(int r) => _rarityName[Mathf.Clamp(r, 0, 5)];

        // ---- item categories: tile tint + caption + glyph
        public static Color TypeTint(ItemType t)
        {
            switch (t)
            {
                case ItemType.Weapon: return H(0x1F2420);
                case ItemType.Magazine: return H(0x22261E);
                case ItemType.Ammo: return H(0x2B2817);
                case ItemType.Medical: return H(0x2E1A17);
                case ItemType.Armor: case ItemType.Helmet: return H(0x1B2326);
                case ItemType.Valuable: return H(0x2E2914);
                case ItemType.Food: return H(0x1A2528);
                default: return H(0x24221E);
            }
        }
        public static string TypeName(ItemType t)
        {
            switch (t)
            {
                case ItemType.Weapon: return "Оружие";
                case ItemType.Magazine: return "Магазин";
                case ItemType.Ammo: return "Боеприпасы";
                case ItemType.Medical: return "Медицина";
                case ItemType.Armor: return "Бронежилет";
                case ItemType.Helmet: return "Шлем";
                case ItemType.Valuable: return "Ценность";
                case ItemType.Food: return "Провизия";
                default: return "Бартер";
            }
        }
        public static string TypeGlyph(ItemType t)
        {
            switch (t)
            {
                case ItemType.Weapon: return "rifle";
                case ItemType.Magazine: return "mag";
                case ItemType.Ammo: return "ammo3";
                case ItemType.Medical: return "med";
                case ItemType.Armor: return "vest";
                case ItemType.Helmet: return "helmet";
                case ItemType.Valuable: return "gem";
                case ItemType.Food: return "can";
                default: return "wrench";
            }
        }

        /// <summary>0..1 condition -> green / amber / red.</summary>
        public static Color Condition(float t) => t > 0.6f ? Green : t > 0.3f ? Amber : Red;

        // ---- fonts (Resources/SevernyUI/Fonts, OFL); fall back to the game's fonts if the files are missing
        static Font _disp, _dispB, _body, _bodyM, _bodyS, _mono, _monoB;
        static Font Load(ref Font cache, string file, Font fallback)
        {
            if (cache == null) cache = Resources.Load<Font>("SevernyUI/Fonts/" + file);
            return cache != null ? cache : fallback;
        }
        /// <summary>Oswald SemiBold: titles, buttons, caps labels.</summary>
        public static Font Display => Load(ref _disp, "Oswald-SemiBold", GameAssets.FontBold);
        public static Font DisplayBold => Load(ref _dispB, "Oswald-Bold", Display);
        /// <summary>Fira Sans Condensed: body text.</summary>
        public static Font Body => Load(ref _body, "FiraSansCondensed-Regular", GameAssets.Font);
        public static Font BodyMedium => Load(ref _bodyM, "FiraSansCondensed-Medium", Body);
        public static Font BodySemi => Load(ref _bodyS, "FiraSansCondensed-SemiBold", GameAssets.FontBold);
        /// <summary>IBM Plex Mono: numbers, money, timers, codes.</summary>
        public static Font Mono => Load(ref _mono, "IBMPlexMono-Medium", GameAssets.Font);
        public static Font MonoBold => Load(ref _monoB, "IBMPlexMono-SemiBold", Mono);

        /// <summary>"412 350 ₽" (compact money for numeric displays; sentences keep UIFactory.Money "… руб.").</summary>
        public static string Rub(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ") + " ₽";
        public static string Num(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

        // ---- v11.1: Linear colour space (the URP project) - uGUI blends translucent graphics in linear light, which makes dark
        // overlays (scrims, gradients, vignettes, shadows, HUD cards) far weaker and faint light tints / grain far stronger than
        // designed: the palette and alphas are tuned for gamma-space blending. Every code-built Graphic gets UILinearAlpha
        // (re-maps its vertex alpha with LinAlpha); soft gradient / vignette / glow sprites are baked per alpha (UIFactory.SetSoft).
        static int _linear = -1;
        public static bool Linear
        {
            get
            {
                if (_linear < 0)
                {
                    try { _linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0; }
                    catch (UnityException) { return false; }
                }
                return _linear == 1;
            }
        }

        public static float Luma(Color c) => Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
        /// <summary>sRGB -> linear (exact piecewise curve, managed code).</summary>
        static float ToLin(float s) => s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f);

        /// <summary>Alpha that makes the linear-light blend of colour c look like the designed (gamma-space) blend with alpha a.
        /// Dark colours (luma &lt; 0.18: surfaces, scrims, shadows) are matched over mid-tone content, light ones (lines, text,
        /// accents, highlights) over the dark UI surfaces they sit on. Gamma colour space: returns a unchanged.</summary>
        public static float LinAlpha(Color c, float a)
        {
            if (a <= 0.001f || a >= 0.999f || !Linear) return a;
            float cs = Luma(c), b = cs < 0.18f ? 0.45f : 0.07f;
            float C = ToLin(cs), B = ToLin(b);
            if (Mathf.Abs(C - B) < 1e-4f) return a;
            float r = ToLin(b + a * (cs - b));
            return Mathf.Clamp01((r - B) / (C - B));
        }
    }
}
