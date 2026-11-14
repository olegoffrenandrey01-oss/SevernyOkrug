// v11 UI motion: button hover/press/selection states, screen fade-in, pulses, slow background drift.
// Everything runs on unscaled time (works in the pause menu with Time.timeScale = 0).
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Severny
{
    public enum BtnStyle { Secondary, Primary, Danger, Ghost, Tab, Segment, Menu, Card, Row }

    /// <summary>Visual states of a code-built button (replaces Selectable colour tinting).</summary>
    public class UIButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Button button;
        public BtnStyle style;
        public Image bg, edge, accent, glow, icon;
        public Graphic extra;              // Card: image brightened on hover / selection
        public Text label, sub;
        public RectTransform slide;          // Menu: arrow that slides in on hover
        public float slideFrom = -10f, slideTo = 0f;
        public bool selected;
        public Color labelColor = UITheme.Text0;
        float _h, _p, _s = -1f, _lh = -1f, _lp = -1f, _ls = -1f; bool _over, _down; bool _wasInteractable = true;

        public void OnPointerEnter(PointerEventData e) { _over = true; }
        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { _down = true; }
        public void OnPointerUp(PointerEventData e) { _down = false; }

        void OnEnable() { _h = 0f; _p = 0f; _s = selected ? 1f : 0f; _lh = -1f; Apply(); }
        /// <summary>Re-apply colours now (after changing selected / interactable from code).</summary>
        public void Refresh() { _lh = -1f; }
        void OnDisable() { _over = _down = false; }

        public bool Interactable => button == null || button.IsInteractable();

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool inter = Interactable;
            _h = Mathf.MoveTowards(_h, inter && _over ? 1f : 0f, dt * 9f);
            _p = Mathf.MoveTowards(_p, inter && _down && _over ? 1f : 0f, dt * 16f);
            _s = Mathf.MoveTowards(_s, selected ? 1f : 0f, dt * 10f);
            if (_s < 0f) _s = selected ? 1f : 0f;
            if (_h == _lh && _p == _lp && _s == _ls && inter == _wasInteractable) return;   // idle: nothing to update
            _lh = _h; _lp = _p; _ls = _s; _wasInteractable = inter;
            Apply();
        }

        static Color L(Color a, Color b, float t) => Color.Lerp(a, b, t);

        public void Apply()
        {
            bool inter = Interactable; float h = _h, p = _p, s = Mathf.Max(0f, _s);
            var T = UITheme.Text0;
            switch (style)
            {
                case BtnStyle.Primary:
                    if (bg) bg.color = !inter ? UITheme.H(0x2A2B26) : L(L(UITheme.Amber, UITheme.AmberHi, h), UITheme.AmberLo, p * 0.6f);
                    if (edge) edge.color = !inter ? UITheme.Line0 : UITheme.A(UITheme.AmberHi, 0.25f + 0.5f * h);
                    if (glow) glow.color = UITheme.A(UITheme.Amber, inter ? 0.10f + 0.22f * h : 0f);
                    if (label) label.color = inter ? UITheme.Ink : UITheme.Text2;
                    if (icon) icon.color = inter ? UITheme.Ink : UITheme.Text2;
                    if (sub) sub.color = inter ? UITheme.A(UITheme.Ink, 0.7f) : UITheme.Text3;
                    break;
                case BtnStyle.Danger:
                    if (bg) bg.color = !inter ? UITheme.A(UITheme.Bg2, 0.8f) : L(UITheme.A(UITheme.Bg3, 0.92f), UITheme.A(UITheme.RedLo, 0.98f), h);
                    if (edge) edge.color = !inter ? UITheme.Line0 : L(UITheme.A(UITheme.Red, 0.55f), UITheme.Red, h);
                    if (label) label.color = !inter ? UITheme.Text3 : L(UITheme.Mul(UITheme.Red, 1.12f), T, h);
                    if (icon) icon.color = label ? label.color : UITheme.Red;
                    if (accent) accent.color = UITheme.A(UITheme.Red, h);
                    break;
                case BtnStyle.Ghost:
                    if (bg) bg.color = UITheme.A(UITheme.Bg4, 0.6f * h);
                    if (label) label.color = !inter ? UITheme.Text3 : L(UITheme.Text1, T, h);
                    if (icon) icon.color = !inter ? UITheme.Text3 : L(UITheme.Text1, UITheme.AmberHi, h);
                    break;
                case BtnStyle.Tab:
                    if (bg) bg.color = UITheme.A(UITheme.Bg4, 0.35f * h + 0.55f * s);
                    if (accent) { accent.color = L(UITheme.A(UITheme.Line2, h), UITheme.Amber, s); }
                    if (label) label.color = !inter ? UITheme.Text3 : L(L(UITheme.Text2, UITheme.Text1, h), T, s);
                    if (icon) icon.color = !inter ? UITheme.Text3 : L(L(UITheme.Text2, UITheme.Text1, h), UITheme.Amber, s);
                    if (sub) sub.color = L(UITheme.Text3, UITheme.Text2, Mathf.Max(h, s));
                    break;
                case BtnStyle.Segment:
                    if (bg) bg.color = !inter ? UITheme.A(UITheme.Bg2, 0.6f) : L(L(UITheme.A(UITheme.Bg2, 0.92f), UITheme.A(UITheme.Bg4, 0.96f), h), UITheme.A(UITheme.Mul(UITheme.Amber, 0.22f), 1f), s);
                    if (edge) edge.color = !inter ? UITheme.Line0 : L(L(UITheme.Line1, UITheme.Line2, h), UITheme.Amber, s);
                    if (label) label.color = !inter ? UITheme.Text3 : L(L(UITheme.Text1, T, h), UITheme.AmberHi, s);
                    if (icon) icon.color = label ? label.color : T;
                    break;
                case BtnStyle.Menu:
                    if (bg) bg.color = UITheme.A(UITheme.Amber, 0.16f * h + 0.05f * p);
                    if (accent) accent.color = UITheme.A(UITheme.Amber, h);
                    if (label) label.color = !inter ? UITheme.Text3 : L(UITheme.A(labelColor, 0.86f), Color.white, h);
                    if (sub) sub.color = L(UITheme.Text3, UITheme.Amber, h);
                    if (icon) icon.color = UITheme.A(UITheme.Amber, h);
                    if (slide) slide.anchoredPosition = new Vector2(Mathf.Lerp(slideFrom, slideTo, h), slide.anchoredPosition.y);
                    break;
                case BtnStyle.Card:
                    if (edge) edge.color = L(L(UITheme.Line1, UITheme.Line2, h), UITheme.Amber, s);
                    if (accent) accent.color = UITheme.A(UITheme.Amber, s);
                    if (extra) extra.color = L(new Color(0.72f, 0.72f, 0.72f, 1f), Color.white, Mathf.Max(h * 0.8f, s));
                    if (bg) bg.color = L(UITheme.A(UITheme.Bg2, 0.94f), UITheme.A(UITheme.Bg3, 0.97f), Mathf.Max(h, s));
                    break;
                case BtnStyle.Row:
                    if (bg) bg.color = !inter ? UITheme.A(UITheme.Bg2, 0.5f) : L(UITheme.A(UITheme.Bg2, 0.9f), UITheme.A(UITheme.Bg4, 0.95f), h);
                    if (edge) edge.color = !inter ? UITheme.Line0 : L(UITheme.Line0, UITheme.A(UITheme.Amber, 0.7f), h);
                    if (accent) accent.color = UITheme.A(UITheme.Amber, h);
                    if (label) label.color = !inter ? UITheme.Text2 : T;
                    break;
                default: // Secondary
                    if (bg) bg.color = !inter ? UITheme.A(UITheme.Bg2, 0.7f) : L(L(UITheme.A(UITheme.Bg3, 0.94f), UITheme.A(UITheme.Bg4, 0.98f), h), UITheme.A(UITheme.Bg1, 0.98f), p);
                    if (edge) edge.color = !inter ? UITheme.Line0 : L(UITheme.Line1, UITheme.A(UITheme.Amber, 0.75f), h);
                    if (accent) accent.color = UITheme.A(UITheme.Amber, h);
                    if (label) label.color = !inter ? UITheme.Text3 : L(UITheme.A(T, 0.9f), Color.white, h);
                    if (icon) icon.color = !inter ? UITheme.Text3 : L(UITheme.Text1, UITheme.AmberHi, h);
                    break;
            }
        }
    }

    /// <summary>Fades a CanvasGroup in when the object is enabled (optionally sliding up a few units).</summary>
    public class UIFadeIn : MonoBehaviour
    {
        public float duration = 0.18f, slide;
        public RectTransform target;
        CanvasGroup _cg; float _t; Vector2 _base; bool _baseSet;

        void OnEnable()
        {
            if (_cg == null) { _cg = GetComponent<CanvasGroup>(); if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>(); }
            if (target != null && !_baseSet) { _base = target.anchoredPosition; _baseSet = true; }
            _t = 0f; Step(0f);
        }

        void Update() { if (_t < duration) Step(Mathf.Min(Time.unscaledDeltaTime, 0.05f)); }

        void Step(float dt)
        {
            _t += dt; float k = duration <= 0f ? 1f : Mathf.Clamp01(_t / duration); k = 1f - (1f - k) * (1f - k) * (1f - k);
            _cg.alpha = k;
            if (target != null && slide != 0f) target.anchoredPosition = _base + new Vector2(0f, -slide * (1f - k));
        }
    }

    /// <summary>Alpha pulse for a graphic whose colour nobody else animates.</summary>
    public class UIPulse : MonoBehaviour
    {
        public Graphic graphic; public float speed = 1.4f, min = 0.35f, max = 1f;
        void Update()
        {
            if (graphic == null) return;
            var c = graphic.color; c.a = Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed * 6.2832f)); graphic.color = c;
        }
    }

    /// <summary>Very slow zoom / pan of a full-screen background (menu key art).</summary>
    public class UIKenBurns : MonoBehaviour
    {
        public float period = 46f, zoom = 0.06f; public Vector2 pan = new Vector2(-26f, 8f);
        RectTransform _rt; Vector2 _base; float _t0;
        void OnEnable() { if (_rt == null) { _rt = (RectTransform)transform; _base = _rt.anchoredPosition; } _t0 = Time.unscaledTime; }
        void Update()
        {
            float t = (Time.unscaledTime - _t0) / period; float k = 0.5f - 0.5f * Mathf.Cos(t * 6.2832f);
            _rt.localScale = Vector3.one * (1.02f + zoom * k);
            _rt.anchoredPosition = _base + pan * k;
        }
    }

    /// <summary>Crops a RawImage's uvRect so the texture covers its rect without stretching (like CSS object-fit: cover).</summary>
    public class UICover : MonoBehaviour
    {
        public Vector2 focus = new Vector2(0.5f, 0.5f);   // which part of the texture stays visible when cropping (0..1)
        RawImage _ri; Vector2 _last; Texture _lastTex;

        void OnEnable() { _last = Vector2.zero; Fit(); }
        void LateUpdate() { Fit(); }

        public void Fit()
        {
            if (_ri == null) _ri = GetComponent<RawImage>();
            if (_ri == null || _ri.texture == null) return;
            var r = ((RectTransform)transform).rect.size;
            if (r == _last && _ri.texture == _lastTex) return;
            _last = r; _lastTex = _ri.texture;
            if (r.x <= 1f || r.y <= 1f) return;
            float ta = (float)_ri.texture.width / Mathf.Max(1, _ri.texture.height), ra = r.x / r.y;
            if (ra > ta) { float h = ta / ra; _ri.uvRect = new Rect(0f, Mathf.Clamp(focus.y - h * 0.5f, 0f, 1f - h), 1f, h); }
            else { float w = ra / ta; _ri.uvRect = new Rect(Mathf.Clamp(focus.x - w * 0.5f, 0f, 1f - w), 0f, w, 1f); }
        }
    }
    /// <summary>Text field states: hairline edge brightens on hover and turns amber while typing.</summary>
    public class UIInputFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public InputField field; public Image edge, fill, icon;
        bool _over; int _last = -1;
        public void OnPointerEnter(PointerEventData e) { _over = true; }
        public void OnPointerExit(PointerEventData e) { _over = false; }
        void OnDisable() { _over = false; _last = -1; }
        void Update()
        {
            if (field == null) return;
            int st = !field.interactable ? 0 : field.isFocused ? 3 : _over ? 2 : 1;
            if (st == _last) return;
            _last = st;
            if (edge) edge.color = st == 3 ? UITheme.Amber : st == 2 ? UITheme.Line2 : st == 1 ? UITheme.Line1 : UITheme.Line0;
            if (fill) fill.color = st == 0 ? UITheme.A(UITheme.Bg2, 0.6f) : st == 3 ? UITheme.A(UITheme.Bg0, 0.98f) : UITheme.A(UITheme.Bg0, 0.92f);
            if (icon) icon.color = st == 3 ? UITheme.Amber : st == 0 ? UITheme.Text3 : UITheme.Text2;
            if (field.textComponent) field.textComponent.color = st == 0 ? UITheme.Text2 : UITheme.Text0;
        }
    }
}
