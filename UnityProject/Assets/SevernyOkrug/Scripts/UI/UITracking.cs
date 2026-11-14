// Letter spacing (tracking) for legacy uGUI Text: shifts every glyph quad of a line by spacing * index,
// plus a wider gap at spaces (word spacing) so tracked capitals still read as separate words.
// Works per line (a new line starts when the next quad jumps back to the left) and keeps the text alignment.
// Unity 2019.1+ emits no quads for whitespace / rich-text tags; older layouts (quads for spaces) are handled too.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    [DisallowMultipleComponent]
    public class UITracking : BaseMeshEffect
    {
        /// <summary>Extra space between letters, in reference (canvas) units.</summary>
        public float spacing = 1.5f;
        /// <summary>Extra space added at every word gap; negative = automatic (1.5 x spacing).</summary>
        public float wordSpacing = -1f;
        public float WordGap => wordSpacing >= 0f ? wordSpacing : spacing * 1.5f;

        static readonly List<int> _lineStart = new List<int>();
        static readonly List<int> _wsA = new List<int>(), _wsB = new List<int>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(spacing, 0f)) return;
            int n = vh.currentVertCount; if (n < 8) return;
            var text = GetComponent<Text>(); if (text == null) return;
            int quads = n / 4;
            // quad -> number of spaces before it (A: no quads for whitespace, B: quads for spaces)
            CountSpaces(text.text, text.supportRichText);
            List<int> ws = quads == _wsA.Count ? _wsA : quads == _wsB.Count ? _wsB : null;
            float gap = ws != null ? WordGap : 0f;
            var v = new UIVertex(); var v0 = new UIVertex();
            // split into lines
            _lineStart.Clear(); _lineStart.Add(0);
            float prevX = float.MinValue;
            for (int q = 0; q < quads; q++)
            {
                vh.PopulateUIVertex(ref v0, q * 4);
                float x = v0.position.x;
                if (q > 0 && x < prevX - 0.5f) _lineStart.Add(q);
                prevX = x;
            }
            _lineStart.Add(quads);
            var a = text.alignment;
            float align = (a == TextAnchor.UpperCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.LowerCenter) ? 0.5f :
                          (a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight) ? 1f : 0f;
            for (int li = 0; li + 1 < _lineStart.Count; li++)
            {
                int s = _lineStart[li], e = _lineStart[li + 1]; int cnt = e - s; if (cnt <= 0) continue;
                int w0 = ws != null ? ws[s] : 0;
                float total = spacing * (cnt - 1) + (ws != null ? gap * (ws[e - 1] - w0) : 0f), shift = -total * align;
                for (int q = s; q < e; q++)
                {
                    float dx = spacing * (q - s) + (ws != null ? gap * (ws[q] - w0) : 0f) + shift;
                    for (int k = 0; k < 4; k++)
                    {
                        int i = q * 4 + k;
                        vh.PopulateUIVertex(ref v, i); v.position.x += dx; vh.SetUIVertex(v, i);
                    }
                }
            }
        }

        static void CountSpaces(string s, bool rich)
        {
            _wsA.Clear(); _wsB.Clear();
            if (string.IsNullOrEmpty(s)) return;
            int spaces = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (rich && ch == '<') { int j = TagEnd(s, i); if (j > i) { i = j; continue; } }
                if (ch == '\n' || ch == '\r') continue;
                if (ch == ' ' || ch == '\t' || ch == '\u00A0') { _wsB.Add(spaces); spaces++; continue; }
                _wsA.Add(spaces); _wsB.Add(spaces);
            }
        }

        /// <summary>Index of the closing '>' when a rich-text tag starts at i (b, i, size, color, material), else -1.</summary>
        public static int TagEnd(string s, int i)
        {
            int j = s.IndexOf('>', i + 1); if (j < 0) return -1;
            int k = i + 1; if (k < j && s[k] == '/') k++;
            string name = s.Substring(k, j - k).ToLowerInvariant();
            if (name == "b" || name == "i" || name.StartsWith("size") || name.StartsWith("color") || name.StartsWith("material")) return j;
            return -1;
        }

        /// <summary>Extra width added by the effect to a single line of text.</summary>
        public float ExtraWidth(string s, bool rich)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            int vis = 0, gaps = 0, pending = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (rich && ch == '<') { int j = TagEnd(s, i); if (j > i) { i = j; continue; } }
                if (ch == '\n' || ch == '\r') continue;
                if (ch == ' ' || ch == '\t' || ch == '\u00A0') { if (vis > 0) pending++; continue; }
                vis++; gaps += pending; pending = 0;
            }
            return spacing * Mathf.Max(0, vis - 1) + WordGap * gaps;
        }
    }
}
