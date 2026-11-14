// In-raid HUD (v11): interaction prompt with a keycap, progress card, notification cards, damage / low-health vignette,
// status chips + body silhouette, stamina bar, weapon card, exits panel (O), extraction countdown, death banner.
// The Hud.* handlers and the per-frame logic are the same as before; only the presentation changed.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    public partial class UIManager
    {
        // prompt
        RectTransform _promptRoot, _promptKey; Text _prompt, _promptVerb, _promptKeyText; string _promptShown;
        // progress
        RectTransform _progressRoot, _progressHintRoot; Text _progressLabel, _progressPct, _progressHint, _progressHintKey; Image _progressBar; string _progressShown;
        // notifications
        readonly List<Text> _notes = new List<Text>();
        readonly List<float> _noteUntil = new List<float>();
        readonly List<float> _noteBorn = new List<float>();
        readonly List<RectTransform> _noteCards = new List<RectTransform>();
        readonly List<CanvasGroup> _noteCg = new List<CanvasGroup>();
        readonly List<Image> _noteBar = new List<Image>(), _noteIcon = new List<Image>();
        readonly List<string> _noteShown = new List<string>();
        // extraction / exits / timer
        RectTransform _extractRoot, _exitsRoot, _exitsList, _timerRoot; Text _extractText, _extractName, _extractTime, _exitsMap, _exitsClock, _exitsCount, _timerSmall;
        Image _extractBar, _timerIcon, _exitsClockIcon;
        readonly List<RectTransform> _exitRows = new List<RectTransform>();
        // status / body / stamina / weapon / death
        RectTransform _statusRoot, _bodyRoot, _staminaRoot, _weaponRoot, _deathRoot; Text _statusText, _weaponText, _weaponCal, _deathText;
        RectTransform _weaponMode; Image _weaponIcon, _staminaBar, _staminaIcon;
        readonly List<RectTransform> _statusChips = new List<RectTransform>();
        readonly Image[] _bodyParts = new Image[Health.Parts];
        int _statusSig = -1; string _weaponShown;
        // screen effects
        Image _damageFlash, _damageTint, _crosshair, _crossShadow;
        float _flash, _exitsUntil;

        static readonly string[] BodyGlyphs = { "body_Head", "body_Thorax", "body_Stomach", "body_LeftArm", "body_RightArm", "body_LeftLeg", "body_RightLeg" };

        void BuildHud()
        {
            var s = NewScreen(UIScreen.Hud, false);

            // ---- damage: red vignette + faint tint (disabled while invisible)
            _damageTint = UIFactory.Img(s, "damageTint", new Color(0.55f, 0f, 0f, 0f)); UIFactory.Stretch(_damageTint.rectTransform);
            _damageFlash = UIFactory.Img(s, "damage", new Color(0.6f, 0.02f, 0f, 0f)); UIFactory.Stretch(_damageFlash.rectTransform, -40, -40, -40, -40);
            _damageFlash.sprite = UIAssets.Vignette;
            _damageTint.enabled = _damageFlash.enabled = false;

            // ---- crosshair: a dot pinned to the exact screen centre (dark halo keeps it visible on bright surfaces)
            _crossShadow = UIFactory.Img(s, "dotHalo", new Color(0f, 0f, 0f, 0.35f)); _crossShadow.sprite = UIAssets.Circle;
            var hrt = _crossShadow.rectTransform; hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f); hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = new Vector2(8, 8);
            _crosshair = UIFactory.Img(s, "dot", new Color(1f, 1f, 1f, 0.6f)); _crosshair.sprite = UIAssets.Circle;
            var crt = _crosshair.rectTransform; crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f); crt.anchoredPosition = Vector2.zero; crt.sizeDelta = new Vector2(4, 4);

            // ---- interaction prompt: [F] ОБЫСКАТЬ  Ящик с инструментами
            _promptRoot = UIFactory.Node(s, "prompt"); UIFactory.PlaceAt(_promptRoot, 0.5f, 0.5f, 760, 598, 400, 40);
            HudCard(_promptRoot, 0.74f);
            float kw = UIFactory.Keycap(_promptRoot, "F", 8, 7, 26, 14);
            _promptKey = (RectTransform)_promptRoot.GetChild(_promptRoot.childCount - 1);
            _promptKeyText = _promptKey.GetComponentInChildren<Text>();
            _promptVerb = UIFactory.Caps(_promptRoot, "", 8 + kw + 10, 0, 200, 40, 16, UITheme.AmberHi, TextAnchor.MiddleLeft, 1.6f);
            _promptVerb.horizontalOverflow = HorizontalWrapMode.Overflow;
            _prompt = UIFactory.T(_promptRoot, "", 120, 0, 300, 40, UITheme.BodySemi, 19, UITheme.Text0, TextAnchor.MiddleLeft, "text");
            _prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
            _promptRoot.gameObject.SetActive(false);

            // ---- progress (medicine, benchmark)
            _progressRoot = UIFactory.Node(s, "progress"); UIFactory.PlaceAt(_progressRoot, 0.5f, 0.5f, 720, 650, 480, 74);
            HudCard(_progressRoot, 0.8f);
            UIFactory.Icon(_progressRoot, "hourglass", 16, 13, 16, UITheme.Amber, "icon");
            _progressLabel = UIFactory.T(_progressRoot, "", 40, 6, 340, 30, UITheme.BodyMedium, 17, UITheme.Text0, TextAnchor.MiddleLeft, "label");
            _progressLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _progressPct = UIFactory.Mono(_progressRoot, "", 380, 6, 84, 30, 16, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            _progressBar = UIFactory.Bar(_progressRoot, 16, 42, 448, 4, UITheme.Amber);
            _progressBar.transform.parent.GetComponent<Image>().color = UITheme.A(UITheme.Line1, 0.9f);
            _progressHintRoot = UIFactory.Node(_progressRoot, "hint"); UIFactory.Place(_progressHintRoot, 16, 51, 448, 18);
            float hk = UIFactory.Keycap(_progressHintRoot, "H", 0, 0, 18, 11);
            _progressHintKey = _progressHintRoot.GetChild(_progressHintRoot.childCount - 1).GetComponentInChildren<Text>();
            _progressHint = UIFactory.T(_progressHintRoot, "", hk + 7, 0, 300, 18, UITheme.Body, 13, UITheme.Text2, TextAnchor.MiddleLeft, "text");
            _progressRoot.gameObject.SetActive(false);

            // ---- notifications: cards stacked under the top edge, newest first
            var notes = UIFactory.Node(s, "notes"); UIFactory.PlaceAt(notes, 0.5f, 0f, 0, 0, 1920, 400);
            for (int i = 0; i < 4; i++)
            {
                var card = UIFactory.Node(notes, "Note" + i);
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 1f); card.pivot = new Vector2(0.5f, 1f); card.sizeDelta = new Vector2(300, 38);
                var cg = card.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
                HudCard(card, 0.76f);
                var bar = UIFactory.Img(card, "bar", UITheme.Amber); var brt = bar.rectTransform;
                brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 0.5f); brt.sizeDelta = new Vector2(2, 0); brt.anchoredPosition = Vector2.zero;
                var ic = UIFactory.Icon(card, "info", 14, 11, 16, UITheme.Amber, "icon");
                var t = UIFactory.T(card, "", 40, 0, 600, 38, UITheme.BodyMedium, 18, UITheme.Text0, TextAnchor.MiddleLeft, "text");
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.DropShadow(t, 0.6f, 1f);
                _notes.Add(t); _noteUntil.Add(0f); _noteBorn.Add(-10f); _noteCards.Add(card); _noteCg.Add(cg); _noteBar.Add(bar); _noteIcon.Add(ic); _noteShown.Add(null);
            }

            // ---- extraction countdown
            _extractRoot = UIFactory.Node(s, "extract"); UIFactory.PlaceAt(_extractRoot, 0.5f, 0f, 740, 262, 440, 96);
            HudCard(_extractRoot, 0.82f, UITheme.A(UITheme.Green, 0.45f));
            UIFactory.Box(_extractRoot, "top", 0, 0, 440, 2, UITheme.Green);
            UIFactory.Icon(_extractRoot, "exit", 18, 16, 16, UITheme.Green, "icon");
            _extractText = UIFactory.Caps(_extractRoot, "Эвакуация", 42, 12, 240, 24, 14, UITheme.Green, TextAnchor.MiddleLeft, 2.4f);
            _extractName = UIFactory.T(_extractRoot, "", 18, 36, 300, 36, UITheme.Display, 26, UITheme.Text0, TextAnchor.MiddleLeft, "name");
            _extractName.horizontalOverflow = HorizontalWrapMode.Overflow;
            _extractTime = UIFactory.Mono(_extractRoot, "", 290, 22, 132, 48, 36, UITheme.Green, TextAnchor.MiddleRight, true);
            _extractBar = UIFactory.Bar(_extractRoot, 18, 80, 404, 4, UITheme.Green);
            _extractBar.transform.parent.GetComponent<Image>().color = UITheme.A(UITheme.GreenLo, 0.9f);
            _extractRoot.gameObject.SetActive(false);

            // ---- death banner
            _deathRoot = UIFactory.Node(s, "death");
            _deathRoot.anchorMin = new Vector2(0f, 0.5f); _deathRoot.anchorMax = new Vector2(1f, 0.5f); _deathRoot.pivot = new Vector2(0.5f, 0.5f);
            _deathRoot.sizeDelta = new Vector2(0f, 170f); _deathRoot.anchoredPosition = new Vector2(0f, 20f);
            var dbg = UIFactory.Img(_deathRoot, "bg", UITheme.A(Color.black, 0.62f)); UIFactory.Stretch(dbg.rectTransform);
            var dl1 = UIFactory.Img(_deathRoot, "lineTop", UITheme.A(UITheme.Red, 0.55f)); var d1 = dl1.rectTransform; d1.anchorMin = new Vector2(0, 1); d1.anchorMax = new Vector2(1, 1); d1.pivot = new Vector2(0.5f, 1); d1.sizeDelta = new Vector2(0, 1); d1.anchoredPosition = Vector2.zero;
            var dl2 = UIFactory.Img(_deathRoot, "lineBottom", UITheme.A(UITheme.Red, 0.55f)); var d2 = dl2.rectTransform; d2.anchorMin = new Vector2(0, 0); d2.anchorMax = new Vector2(1, 0); d2.pivot = new Vector2(0.5f, 0); d2.sizeDelta = new Vector2(0, 1); d2.anchoredPosition = Vector2.zero;
            _deathText = UIFactory.Label(_deathRoot, "", 88, TextAnchor.MiddleCenter, UITheme.Red, false, "text");
            _deathText.font = UITheme.DisplayBold; _deathText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(_deathText.rectTransform, 0, 0, 0, 6); UIFactory.Track(_deathText, 8f);
            _deathRoot.gameObject.SetActive(false);

            // ---- exits panel (O)
            _exitsRoot = UIFactory.Node(s, "exits"); UIFactory.PlaceAt(_exitsRoot, 1f, 0f, 1452, 24, 444, 200);
            HudCard(_exitsRoot, 0.86f);
            UIFactory.Box(_exitsRoot, "top", 0, 0, 444, 2, UITheme.Amber);
            _exitsMap = UIFactory.T(_exitsRoot, "", 20, 14, 280, 32, UITheme.Display, 22, UITheme.Text0, TextAnchor.MiddleLeft, "map");
            _exitsMap.horizontalOverflow = HorizontalWrapMode.Overflow;
            _exitsClockIcon = UIFactory.Icon(_exitsRoot, "clock", 316, 22, 16, UITheme.Text2, "clockIcon");
            _exitsClock = UIFactory.Mono(_exitsRoot, "", 330, 14, 96, 32, 22, UITheme.AmberHi, TextAnchor.MiddleRight, true);
            UIFactory.Box(_exitsRoot, "line", 20, 54, 404, 1, UITheme.Line1);
            UIFactory.Caps(_exitsRoot, "Выходы", 20, 62, 200, 24, 13, UITheme.Text2, TextAnchor.MiddleLeft, 2.2f);
            _exitsCount = UIFactory.Mono(_exitsRoot, "", 224, 62, 200, 24, 13, UITheme.Text2, TextAnchor.MiddleRight);
            _exitsList = UIFactory.Node(_exitsRoot, "list"); UIFactory.Place(_exitsList, 0, 92, 444, 100);
            _exitsRoot.gameObject.SetActive(false);

            // ---- raid timer (last 5 minutes, while the exits panel is hidden)
            _timerRoot = UIFactory.Node(s, "timer"); UIFactory.PlaceAt(_timerRoot, 1f, 0f, 1716, 24, 180, 40);
            HudCard(_timerRoot, 0.72f, UITheme.A(UITheme.Red, 0.45f));
            _timerIcon = UIFactory.Icon(_timerRoot, "clock", 14, 12, 16, UITheme.Red, "icon");
            _timerSmall = UIFactory.Mono(_timerRoot, "", 38, 0, 128, 40, 22, UITheme.Red, TextAnchor.MiddleRight, true);
            _timerRoot.gameObject.SetActive(false);

            // ---- bottom left: body silhouette, status chips, stamina
            _bodyRoot = UIFactory.Node(s, "body"); UIFactory.PlaceAt(_bodyRoot, 0f, 1f, 28, 914, 60, 120);
            var bshade = UIFactory.Img(_bodyRoot, "shade", UITheme.A(Color.black, 0.6f)); UIFactory.SetSoft(bshade, "radial", bshade.color); UIFactory.Stretch(bshade.rectTransform, -40, -30, -40, -30);
            var outline = UIFactory.Img(_bodyRoot, "outline", UITheme.A(Color.black, 0.7f)); UIFactory.Stretch(outline.rectTransform, -1, -1, -1, -1);
            UIFactory.SetIcon(outline, "body_Outline");
            for (int i = 0; i < Health.Parts; i++)
            {
                var im = UIFactory.Img(_bodyRoot, BodyGlyphs[i], UITheme.Green); UIFactory.Stretch(im.rectTransform);
                UIFactory.SetIcon(im, BodyGlyphs[i]); _bodyParts[i] = im;
            }
            _bodyRoot.gameObject.SetActive(false);

            _statusRoot = UIFactory.Node(s, "status"); UIFactory.PlaceAt(_statusRoot, 0f, 1f, 28, 834, 600, 200);
            for (int i = 0; i < 6; i++)
            {
                var chip = UIFactory.Chip(_statusRoot, "", 0, 0, UITheme.Red, "drop", 28f);
                chip.name = "Status" + i;
                var cbg = chip.GetComponent<Image>(); if (cbg != null) cbg.color = UITheme.A(UITheme.Bg0, 0.8f);
                chip.gameObject.SetActive(false);
                _statusChips.Add(chip);
            }
            _statusText = UIFactory.T(_statusRoot, "", 0, 0, 10, 10, UITheme.Body, 14, UITheme.Text1, TextAnchor.MiddleLeft, "text");   // compatibility (empty)
            _statusText.enabled = false;

            _staminaRoot = UIFactory.Node(s, "stamina"); UIFactory.PlaceAt(_staminaRoot, 0f, 1f, 28, 1040, 290, 16);
            _staminaIcon = UIFactory.Icon(_staminaRoot, "runner", 0, 0, 16, UITheme.Text1, "icon");
            UIFactory.DropShadow(_staminaIcon, 0.6f, 1f);
            _staminaBar = UIFactory.Bar(_staminaRoot, 24, 6, 260, 4, UITheme.A(UITheme.Text0, 0.85f));
            _staminaBar.transform.parent.GetComponent<Image>().color = UITheme.A(Color.black, 0.5f);
            _staminaRoot.gameObject.SetActive(false);

            // ---- bottom right: weapon card
            _weaponRoot = UIFactory.Node(s, "weapon"); UIFactory.PlaceAt(_weaponRoot, 1f, 1f, 1392, 966, 500, 86);
            var shade = UIFactory.Img(_weaponRoot, "shade", UITheme.A(Color.black, 0.8f)); UIFactory.SetSoft(shade, "radial", shade.color);   // soft, edge-less backing
            UIFactory.Place(shade.rectTransform, 40, -50, 600, 186);
            _weaponIcon = UIFactory.Img(_weaponRoot, "icon", UITheme.A(Color.white, 0.92f)); _weaponIcon.preserveAspect = true;
            UIFactory.Place(_weaponIcon.rectTransform, 40, 12, 168, 64);
            UIFactory.DropShadow(_weaponIcon, 0.5f, 2f);
            _weaponText = UIFactory.T(_weaponRoot, "", 220, 8, 280, 40, UITheme.Display, 28, UITheme.Text0, TextAnchor.MiddleRight, "name");
            _weaponText.horizontalOverflow = HorizontalWrapMode.Overflow; UIFactory.DropShadow(_weaponText, 0.7f, 1.5f);
            _weaponCal = UIFactory.Mono(_weaponRoot, "", 220, 52, 200, 24, 14, UITheme.Text1, TextAnchor.MiddleRight);
            UIFactory.DropShadow(_weaponCal, 0.7f, 1f);
            _weaponMode = UIFactory.Chip(_weaponRoot, "ОД", 440, 52, UITheme.Amber, null, 24f);
            _weaponRoot.gameObject.SetActive(false);

            Hud.NotifyHandler = (msg, dur) =>
            {
                if (string.IsNullOrEmpty(msg)) return;
                if (Current != UIScreen.Hud) { Toast(msg, dur); return; }
                for (int i = _notes.Count - 1; i > 0; i--) { _notes[i].text = _notes[i - 1].text; _noteUntil[i] = _noteUntil[i - 1]; _noteBorn[i] = _noteBorn[i - 1]; }
                _notes[0].text = msg; _noteUntil[0] = Time.unscaledTime + dur; _noteBorn[0] = Time.unscaledTime;
            };
            Hud.PromptHandler = msg => { if (_prompt != null) SetPrompt(msg); };
            Hud.ProgressHandler = (label, t) =>
            {
                if (_progressRoot == null) return;
                bool on = label != null;
                if (_progressRoot.gameObject.activeSelf != on) _progressRoot.gameObject.SetActive(on);
                if (on) SetProgress(label, t);
            };
            Hud.DamageFlashHandler = () => _flash = 0.55f;
        }

        /// <summary>Dark translucent HUD surface with a hairline edge.</summary>
        static void HudCard(RectTransform rt, float alpha, Color? edge = null)
        {
            var bg = UIFactory.Img(rt, "bg", UITheme.A(UITheme.Bg0, alpha)); UIFactory.Stretch(bg.rectTransform);
            UIFactory.Border(rt, edge ?? UITheme.A(UITheme.Line1, 0.9f));
        }

        // ------------------------------------------------------------------ prompt / progress / notes
        void SetPrompt(string msg)
        {
            msg = msg ?? "";
            if (msg == _promptShown) return;
            _promptShown = msg;
            bool on = msg.Length > 0;
            if (_promptRoot.gameObject.activeSelf != on) _promptRoot.gameObject.SetActive(on);
            if (!on) { _prompt.text = ""; return; }
            // "F  Обыскать: Ящик" -> key "F", verb "Обыскать", object "Ящик"
            string key = null, body = msg;
            int sp = msg.IndexOf("  ");
            if (sp > 0 && sp <= 5 && msg.IndexOf(' ') == sp) { key = msg.Substring(0, sp); body = msg.Substring(sp + 2).TrimStart(); }
            string verb = null, obj = body;
            int c = body.IndexOf(": ");
            if (c > 0 && c <= 22) { verb = body.Substring(0, c); obj = body.Substring(c + 2); }
            else if (body.Length <= 22 && body.IndexOf(' ') < 0) { verb = body; obj = ""; }

            float x = 10f;
            _promptKey.gameObject.SetActive(key != null);
            if (key != null)
            {
                _promptKeyText.text = key;
                float kw = Mathf.Max(26f, Mathf.Ceil(UIFactory.TextWidth(_promptKeyText) + 16f));
                _promptKey.sizeDelta = new Vector2(kw, 26f); _promptKey.anchoredPosition = new Vector2(x, -7f);
                x += kw + 12f;
            }
            _promptVerb.text = verb != null ? verb.ToUpperInvariant() : "";
            float vw = UIFactory.TextWidth(_promptVerb);
            UIFactory.Place(_promptVerb.rectTransform, x, 0, vw + 4, 40);
            if (vw > 0f) x += vw + (obj.Length > 0 ? 10f : 0f);
            _prompt.text = obj;
            float ow = UIFactory.TextWidth(_prompt);
            UIFactory.Place(_prompt.rectTransform, x, 0, ow + 4, 40);
            x += ow + 14f;
            float w = Mathf.Max(120f, Mathf.Ceil(x));
            UIFactory.PlaceAt(_promptRoot, 0.5f, 0.5f, 960f - w * 0.5f, 598, w, 40);
        }

        void SetProgress(string label, float t)
        {
            if (!ReferenceEquals(label, _progressShown) && label != _progressShown)
            {
                _progressShown = label;
                // "Применение: Аптечка  (H — отмена)" -> label + key hint
                string main = label, hintKey = null, hint = null;
                int i = label.LastIndexOf("  (");
                if (i > 0 && label.EndsWith(")"))
                {
                    string inner = label.Substring(i + 3, label.Length - i - 4);
                    int d = inner.IndexOf(" — ");
                    if (d > 0 && d <= 5) { hintKey = inner.Substring(0, d); hint = inner.Substring(d + 3); main = label.Substring(0, i); }
                }
                _progressLabel.text = main;
                bool hasHint = hintKey != null;
                _progressHintRoot.gameObject.SetActive(hasHint);
                if (hasHint)
                {
                    _progressHintKey.text = hintKey;
                    var key = (RectTransform)_progressHintKey.transform.parent;
                    float kw = Mathf.Max(18f, Mathf.Ceil(UIFactory.TextWidth(_progressHintKey) + 11f));
                    key.sizeDelta = new Vector2(kw, 18f);
                    _progressHint.text = hint; UIFactory.Place(_progressHint.rectTransform, kw + 7f, 0, 300, 18);
                }
                float w = Mathf.Clamp(UIFactory.TextWidth(_progressLabel) + 140f, 420f, 760f);
                float h = hasHint ? 78f : 58f;
                UIFactory.PlaceAt(_progressRoot, 0.5f, 0.5f, 960f - w * 0.5f, 650, w, h);
                UIFactory.Place(_progressPct.rectTransform, w - 100f, 6, 84, 30);
                var barBg = (RectTransform)_progressBar.transform.parent; UIFactory.Place(barBg, 16, 42, w - 32f, 4);
                UIFactory.Place(_progressHintRoot, 16, 54, w - 32f, 18);
            }
            UIFactory.SetBar(_progressBar, t);
            _progressPct.text = Mathf.RoundToInt(Mathf.Clamp01(t) * 100f) + "%";
        }

        /// <summary>Accent colour + glyph of a notification, guessed from its wording.</summary>
        static void NoteStyle(string m, out Color c, out string glyph)
        {
            c = UITheme.Amber; glyph = "info";
            if (m.Contains("остановлено") || m.Contains("Восстановлено") || m.Contains("восстановлена")) { c = UITheme.Green; glyph = "med"; }
            else if (m.Contains("нейтрализован") || m.Contains("погиб")) { c = UITheme.Red; glyph = "skull"; }
            else if (m.Contains("ровотечение")) { c = UITheme.Red; glyph = "drop"; }
            else if (m.Contains("подключился") || m.Contains("эвакуировался")) { c = UITheme.Green; glyph = "user"; }
            else if (m.Contains("отключился") || m.Contains("пропал") || m.Contains("покинул")) { c = UITheme.Text1; glyph = "user"; }
            else if (m.Contains("Нет ") || m.Contains("нет места") || m.Contains("не найдены") || m.Contains("не загрузился") || m.Contains("Мало места")) { c = UITheme.Orange; glyph = "warn"; }
            else if (m.Contains("Подобрано") || m.Contains("Найдено")) { c = UITheme.Green; glyph = "box"; }
            else if (m.Contains("Магазин") || m.Contains("Режим") || m.Contains("Щелчок") || m.Contains("пусто") || m.Contains("Полный") || m.Contains("половин")) { c = UITheme.Text1; glyph = "mag"; }
            else if (m.Contains("Обезболивающее")) { c = UITheme.Amber; glyph = "pill"; }
            else if (m.Contains("Фонарь")) { c = UITheme.Text1; glyph = "bolt"; }
            else if (m.Contains("метель") || m.Contains("Зима")) { c = UITheme.Steel; glyph = "snow"; }
        }

        void UpdateNotes(float now)
        {
            float y = 72f;
            for (int i = 0; i < _notes.Count; i++)
            {
                float a = Mathf.Clamp01((_noteUntil[i] - now) / 0.5f);
                if (a <= 0f || string.IsNullOrEmpty(_notes[i].text)) { if (_noteCg[i].alpha != 0f) _noteCg[i].alpha = 0f; continue; }
                var card = _noteCards[i];
                if (!ReferenceEquals(_noteShown[i], _notes[i].text))
                {
                    _noteShown[i] = _notes[i].text;
                    var t = _notes[i];
                    NoteStyle(t.text, out var c, out var g);
                    _noteBar[i].color = c; _noteIcon[i].color = c; UIFactory.SetIcon(_noteIcon[i], g);
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    float tw = UIFactory.TextWidth(t), h = 38f;
                    if (tw > 820f || t.text.IndexOf('\n') >= 0)
                    {
                        tw = Mathf.Min(tw, 820f);
                        t.horizontalOverflow = HorizontalWrapMode.Wrap;
                        t.rectTransform.sizeDelta = new Vector2(tw + 2f, 38f);
                        h = Mathf.Max(38f, Mathf.Ceil(t.preferredHeight + 14f));
                    }
                    float w = Mathf.Clamp(tw + 58f, 240f, 900f);
                    card.sizeDelta = new Vector2(w, h);
                    UIFactory.Place(t.rectTransform, 40, 0, w - 52f, h);
                    UIFactory.Place(_noteIcon[i].rectTransform, 14, (h - 16f) * 0.5f, 16, 16);
                }
                float k = Mathf.Clamp01((now - _noteBorn[i]) / 0.18f); k = 1f - (1f - k) * (1f - k);
                card.anchoredPosition = new Vector2(0f, -(y - 10f * (1f - k)));
                _noteCg[i].alpha = a * k;
                y += card.sizeDelta.y + 6f;
            }
        }

        // ------------------------------------------------------------------ per frame
        void UpdateHud()
        {
            float now = Time.unscaledTime;
            UpdateNotes(now);
            var rm = RaidManager.I; var p = PlayerController.Local;

            // damage flash + low-health pulse
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 1.2f);
            float low = 0f;
            if (p != null && p.health != null && p.health.hp != null && !p.health.IsDead)
            {
                float tot = p.health.Total / Mathf.Max(1f, p.health.TotalMax);
                if (tot < 0.35f) low = (0.35f - tot) / 0.35f * (0.75f + 0.25f * Mathf.Sin(now * 3.2f));
            }
            float va = Mathf.Clamp01(_flash * 1.25f + low * 0.32f), ta = _flash * 0.22f;
            bool vOn = va > 0.004f, tOn = ta > 0.004f;
            if (_damageFlash.enabled != vOn) _damageFlash.enabled = vOn;
            if (_damageTint.enabled != tOn) _damageTint.enabled = tOn;
            if (vOn) _damageFlash.color = new Color(0.62f, 0.03f, 0f, va);
            if (tOn) _damageTint.color = new Color(0.55f, 0f, 0f, ta);

            if (rm == null) return;
            // extraction countdown
            bool extracting = rm.Running && !string.IsNullOrEmpty(rm.extractName) && rm.extractLeft > 0f;
            if (_extractRoot.gameObject.activeSelf != extracting) _extractRoot.gameObject.SetActive(extracting);
            if (extracting)
            {
                float dur = 8f;
                foreach (var e in rm.exits) if (e != null && e.exitName == rm.extractName) { dur = Mathf.Max(0.1f, e.duration); break; }
                if (_extractName.text != rm.extractName) _extractName.text = rm.extractName;
                _extractTime.text = rm.extractLeft.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                UIFactory.SetBar(_extractBar, 1f - Mathf.Clamp01(rm.extractLeft / dur));
            }
            // exits list (O)
            if (GameInput.GDown(GKey.Exits)) _exitsUntil = now + 7f;
            bool showExits = now < _exitsUntil;
            if (_exitsRoot.gameObject.activeSelf != showExits) _exitsRoot.gameObject.SetActive(showExits);
            int mm = Mathf.FloorToInt(rm.timeLeft / 60f), ss = Mathf.FloorToInt(rm.timeLeft % 60f);
            string clock = mm.ToString("00") + ":" + ss.ToString("00");
            if (showExits) UpdateExits(rm, p, clock);
            bool timer = rm.Running && rm.timeLeft < 300f && !showExits;
            if (_timerRoot.gameObject.activeSelf != timer) _timerRoot.gameObject.SetActive(timer);
            _timerSmall.text = timer ? clock : "";
            if (timer) _timerIcon.color = UITheme.A(UITheme.Red, 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(now * 2.2f)));

            if (p == null || !p.isActiveAndEnabled)
            {
                _staminaRoot.gameObject.SetActive(false); ClearStatus(); _weaponText.text = ""; _weaponShown = null;
                if (_weaponRoot.gameObject.activeSelf) _weaponRoot.gameObject.SetActive(false);
                if (_bodyRoot.gameObject.activeSelf) _bodyRoot.gameObject.SetActive(false);
                _crosshair.enabled = _crossShadow.enabled = false;
                return;
            }
            bool dead = p.health != null && p.health.IsDead;
            _deathText.text = dead ? "ВЫ ПОГИБЛИ" : "";
            if (_deathRoot.gameObject.activeSelf != dead) _deathRoot.gameObject.SetActive(dead);
            float st = p.stamina / Mathf.Max(1f, p.staminaMax);
            bool stOn = st < 0.995f && !dead;
            if (_staminaRoot.gameObject.activeSelf != stOn) _staminaRoot.gameObject.SetActive(stOn);
            if (stOn)
            {
                UIFactory.SetBar(_staminaBar, st);
                _staminaBar.color = st < 0.25f ? UITheme.Amber : UITheme.A(UITheme.Text0, 0.85f);
                _staminaIcon.color = st < 0.25f ? UITheme.Amber : UITheme.Text1;
            }
            var h = p.health;
            if (h != null && !dead && h.hp != null) UpdateStatus(p, h);
            else { ClearStatus(); if (_bodyRoot.gameObject.activeSelf) _bodyRoot.gameObject.SetActive(false); }
            UpdateWeapon(p, dead);
            bool cross = !dead && (p.weapon == null || !p.weapon.IsAiming);
            _crosshair.enabled = cross; _crossShadow.enabled = cross;
        }

        void UpdateExits(RaidManager rm, PlayerController p, string clock)
        {
            string map = rm.map != null ? rm.map.title : "";
            if (_exitsMap.text != map) _exitsMap.text = map;
            if (_exitsClock.text != clock)
            {
                _exitsClock.text = clock;
                if (_exitsClockIcon != null) UIFactory.Place(_exitsClockIcon.rectTransform, 426f - UIFactory.TextWidth(_exitsClock) - 24f, 22, 16, 16);
            }
            _exitsClock.color = rm.timeLeft < 300f ? UITheme.Red : UITheme.AmberHi;
            if (_exitsClockIcon != null) _exitsClockIcon.color = rm.timeLeft < 300f ? UITheme.Red : UITheme.Text2;
            int k = 0;
            foreach (var e in rm.exits)
            {
                if (e == null) continue;
                if (k >= _exitRows.Count) _exitRows.Add(MakeExitRow(k));
                var row = _exitRows[k];
                if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
                float d = p != null ? Vector3.Distance(p.transform.position, e.transform.position) : 0f;
                var name = row.Find("name").GetComponent<Text>(); if (name.text != e.exitName) name.text = e.exitName;
                row.Find("dist").GetComponent<Text>().text = "~" + (Mathf.RoundToInt(d / 10f) * 10) + " м";
                k++;
            }
            for (int i = k; i < _exitRows.Count; i++) if (_exitRows[i].gameObject.activeSelf) _exitRows[i].gameObject.SetActive(false);
            _exitsCount.text = k > 0 ? "АКТИВНЫ: " + k : "НЕТ ДАННЫХ";
            float hgt = 92f + k * 36f + 12f;
            if (Mathf.Abs(_exitsRoot.sizeDelta.y - hgt) > 0.5f) _exitsRoot.sizeDelta = new Vector2(444f, hgt);
        }

        RectTransform MakeExitRow(int i)
        {
            var row = UIFactory.Node(_exitsList, "Exit" + i); UIFactory.Place(row, 12, i * 36f, 420, 32);
            var bg = UIFactory.Img(row, "bg", UITheme.A(UITheme.Bg3, 0.7f)); UIFactory.Stretch(bg.rectTransform);
            UIFactory.Box(row, "mark", 0, 0, 2, 32, UITheme.Green);
            UIFactory.Mono(row, "EXFIL" + (i + 1).ToString("00"), 12, 0, 80, 32, 13, UITheme.Amber, TextAnchor.MiddleLeft, true).name = "code";
            var n = UIFactory.T(row, "", 92, 0, 230, 32, UITheme.BodyMedium, 17, UITheme.Text0, TextAnchor.MiddleLeft, "name");
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Mono(row, "", 300, 0, 108, 32, 14, UITheme.Text1, TextAnchor.MiddleRight).name = "dist";
            return row;
        }

        void ClearStatus()
        {
            if (_statusSig == 0) return;
            _statusSig = 0; _statusText.text = "";
            foreach (var c in _statusChips) if (c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        void UpdateStatus(PlayerController p, Health h)
        {
            // body silhouette: only while something is wrong
            bool hurt = h.AnyBleeding;
            for (int i = 0; i < Health.Parts && i < h.hp.Length; i++) if (h.hp[i] < h.max[i] - 0.5f) { hurt = true; break; }
            if (_bodyRoot.gameObject.activeSelf != hurt) _bodyRoot.gameObject.SetActive(hurt);
            if (hurt)
                for (int i = 0; i < Health.Parts && i < h.hp.Length; i++)
                {
                    float f = h.hp[i] / Mathf.Max(1f, h.max[i]);
                    Color c = f <= 0f ? UITheme.H(0x5A1C14, 0.95f) : f > 0.85f ? UITheme.A(UITheme.Green, 0.85f) : f > 0.45f ? UITheme.A(UITheme.Amber, 0.9f) : UITheme.A(UITheme.Red, 0.95f);
                    if (h.bleeding[i] && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f) c = UITheme.Mul(c, 1.25f);
                    _bodyParts[i].color = c;
                }
            // status chips (rebuilt only when something changes)
            float tot = h.Total / Mathf.Max(1f, h.TotalMax);
            int pk = h.PainSuppressed ? Mathf.CeilToInt(h.painkillerTimer) : 0;
            float wgt = p.CarriedWeight;
            int sig = 1 + (h.AnyBleeding ? 2 : 0) + (h.LegsBroken ? 4 : 0) + (h.ArmsBroken ? 8 : 0) + (h.PainSuppressed ? 16 : 0) + (wgt > 30f ? 32 : 0)
                      + (tot < 0.5f ? 64 : 0) + pk * 128 + (wgt > 30f ? Mathf.RoundToInt(wgt) : 0) * 65536 + (tot < 0.5f ? Mathf.RoundToInt(tot * 100f) : 0) * 1000;
            if (sig == _statusSig) return;
            _statusSig = sig;
            int n = 0;
            if (h.AnyBleeding) SetStatus(n++, "Кровотечение · H — бинт", UITheme.Red, "drop");
            if (h.LegsBroken) SetStatus(n++, h.PainSuppressed ? "Ноги: обезболено" : "Нога повреждена · бег невозможен", h.PainSuppressed ? UITheme.Amber : UITheme.Red, "bone");
            if (h.ArmsBroken && !h.PainSuppressed) SetStatus(n++, "Рука повреждена · тремор", UITheme.Red, "hand");
            if (h.PainSuppressed) SetStatus(n++, "Обезболивающее · " + pk + " c", UITheme.Amber, "pill");
            if (wgt > 30f) SetStatus(n++, "Перегруз · " + wgt.ToString("0") + " кг", UITheme.Amber, "weight");
            if (tot < 0.5f) SetStatus(n++, "Здоровье · " + Mathf.RoundToInt(tot * 100f) + "%", UITheme.Red, "heart");
            for (int i = n; i < _statusChips.Count; i++) if (_statusChips[i].gameObject.activeSelf) _statusChips[i].gameObject.SetActive(false);
            // stack upwards from the bottom, right of the silhouette
            float x = 0f;
            for (int i = 0; i < n; i++)
            {
                var c = _statusChips[i];
                c.anchoredPosition = new Vector2(x, -(200f - 28f - (n - 1 - i) * 34f));
            }
            _statusRoot.anchoredPosition = new Vector2((hurt ? 104f : 28f), _statusRoot.anchoredPosition.y);
        }

        void SetStatus(int i, string text, Color c, string glyph)
        {
            if (i >= _statusChips.Count) return;
            var chip = _statusChips[i];
            if (!chip.gameObject.activeSelf) chip.gameObject.SetActive(true);
            UIFactory.SetChip(chip, text, c, glyph);
            var bg = chip.GetComponent<Image>(); if (bg != null) bg.color = UITheme.A(UITheme.Bg0, 0.8f);
            var lab = chip.Find("label")?.GetComponent<Text>(); if (lab != null) lab.color = UITheme.Mul(c, 1.08f);
        }

        void UpdateWeapon(PlayerController p, bool dead)
        {
            var w = p.weapon;
            string line = w != null && !dead ? w.StatusLine() : "";
            if (line == _weaponShown) return;
            _weaponShown = line;
            bool on = !string.IsNullOrEmpty(line);
            if (_weaponRoot.gameObject.activeSelf != on) _weaponRoot.gameObject.SetActive(on);
            if (!on) { _weaponText.text = ""; return; }
            string name = line, mode = null;
            int i = line.LastIndexOf("  ");
            if (i > 0) { name = line.Substring(0, i); mode = line.Substring(i + 2).Trim(); }
            _weaponText.text = name;
            var d = w.Def;
            _weaponCal.text = d != null && !string.IsNullOrEmpty(d.caliber) ? d.caliber.Replace('x', '×') : "";
            bool hasMode = !string.IsNullOrEmpty(mode);
            _weaponMode.gameObject.SetActive(hasMode);
            float mw = hasMode ? UIFactory.SetChip(_weaponMode, mode, mode == "АВТ" ? UITheme.Amber : UITheme.Text1) : 0f;
            _weaponMode.anchoredPosition = new Vector2(500f - mw, -52f);
            UIFactory.Place(_weaponCal.rectTransform, 220f, 52f, 280f - mw - (hasMode ? 10f : 0f), 24f);
            var icon = d != null ? UIAssets.ItemIcon(d.id) : null;
            _weaponIcon.sprite = icon; _weaponIcon.enabled = icon != null;
            float nw = UIFactory.TextWidth(_weaponText);
            float right = 500f - Mathf.Max(nw, 120f) - 22f;
            UIFactory.Place(_weaponIcon.rectTransform, Mathf.Max(0f, right - 168f), 12, 168, 64);
        }
    }
}
