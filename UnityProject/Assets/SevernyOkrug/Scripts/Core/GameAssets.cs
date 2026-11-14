// Runtime material/mesh helpers. Uses template materials from Resources/SevernyGameAssets (created by
// menu Severny > 1) so shaders are included in builds; falls back to Shader.Find in the editor.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public static class GameAssets
    {
        static SevernyGameAssets _so;
        static bool _soTried;
        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        static Font _font, _fontBold;

        public static SevernyGameAssets SO
        {
            get
            {
                if (!_soTried) { _soTried = true; _so = Resources.Load<SevernyGameAssets>("SevernyGameAssets"); }
                return _so;
            }
        }

        static Shader FindFirst(params string[] names)
        {
            foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
            return null;
        }

        static Material NewLit()
        {
            if (SO != null && SO.litTemplate != null) return new Material(SO.litTemplate);
            var sh = FindFirst("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard", "Diffuse");
            return new Material(sh);
        }

        /// <summary>New URP Lit material (from the template asset when available, so the shader is in builds).</summary>
        public static Material NewLitMaterial() => NewLit();

        static Material NewUnlit()
        {
            if (SO != null && SO.unlitTemplate != null) return new Material(SO.unlitTemplate);
            var sh = FindFirst("Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");
            return new Material(sh);
        }

        public static Material Lit(Color c, float smooth = 0.15f, float metal = 0f)
        {
            string key = "L" + ColorUtility.ToHtmlStringRGBA(c) + smooth.ToString("F2") + metal.ToString("F2");
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            m = NewLit();
            m.name = "SEV_" + key;
            m.color = c;
            SetF(m, "_Smoothness", smooth); SetF(m, "_Glossiness", smooth); SetF(m, "_Metallic", metal);
            m.enableInstancing = true;
            _cache[key] = m;
            return m;
        }

        public static Material Emissive(Color c, float intensity)
        {
            string key = "E" + ColorUtility.ToHtmlStringRGBA(c) + intensity.ToString("F1");
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            m = NewLit();
            m.name = "SEV_" + key;
            m.color = c * 0.3f;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
            _cache[key] = m;
            return m;
        }

        public static Material Unlit(Color c, bool transparent = false)
        {
            string key = "U" + ColorUtility.ToHtmlStringRGBA(c) + (transparent ? "T" : "O");
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            m = NewUnlit();
            m.name = "SEV_" + key;
            m.color = c;
            if (transparent) MakeTransparent(m);
            _cache[key] = m;
            return m;
        }

        public static void MakeTransparent(Material m)
        {
            SetF(m, "_Surface", 1f); SetF(m, "_Blend", 0f);
            SetF(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetF(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetF(m, "_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        public static Material Skybox()
        {
            if (_cache.TryGetValue("SKY", out var m) && m != null) return m;
            if (SO != null && SO.skyboxTemplate != null) m = new Material(SO.skyboxTemplate);
            else { var sh = Shader.Find("Skybox/Procedural"); if (sh == null) return null; m = new Material(sh); }
            _cache["SKY"] = m;
            return m;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        public static Font Font
        {
            get
            {
                if (_font == null) _font = (SO != null && SO.uiFont != null) ? SO.uiFont : Resources.Load<Font>("SevernyUI/Fonts/LiberationSans-Regular");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Font FontBold
        {
            get
            {
                if (_fontBold == null) _fontBold = (SO != null && SO.uiFontBold != null) ? SO.uiFontBold : Resources.Load<Font>("SevernyUI/Fonts/LiberationSans-Bold");
                if (_fontBold == null) _fontBold = Font;
                return _fontBold;
            }
        }

        public static Texture2D Logo => SO != null && SO.logo != null ? SO.logo : Resources.Load<Texture2D>("SevernyUI/logo");
        public static Texture2D MenuBackground => SO != null && SO.menuBackground != null ? SO.menuBackground : Resources.Load<Texture2D>("SevernyUI/menu_bg");
    }

    /// <summary>Primitive builders for procedural props, weapons, bots and the greybox map.</summary>
    public static class Prim
    {
        public static GameObject Make(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m, bool collider = true, Quaternion? rot = null, string name = null)
        {
            var g = GameObject.CreatePrimitive(t);
            if (name != null) g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = scale;
            if (m != null) g.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (!collider) { var c = g.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }
            return g;
        }

        public static GameObject Box(Transform p, Vector3 pos, Vector3 size, Material m, bool col = true, Quaternion? rot = null, string name = null)
            => Make(PrimitiveType.Cube, p, pos, size, m, col, rot, name);

        /// <summary>Cylinder along local Y with given diameter and height.</summary>
        public static GameObject Cyl(Transform p, Vector3 pos, float diameter, float height, Material m, bool col = true, Quaternion? rot = null, string name = null)
            => Make(PrimitiveType.Cylinder, p, pos, new Vector3(diameter, height * 0.5f, diameter), m, col, rot, name);

        public static GameObject Sphere(Transform p, Vector3 pos, float d, Material m, bool col = true, string name = null)
            => Make(PrimitiveType.Sphere, p, pos, Vector3.one * d, m, col, null, name);

        static Mesh _cone;
        public static Mesh ConeMesh
        {
            get
            {
                if (_cone != null) return _cone;
                const int seg = 12;
                var v = new List<Vector3>(); var t = new List<int>();
                v.Add(new Vector3(0, 1, 0));
                for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2f / seg; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f)); }
                v.Add(Vector3.zero);
                int c = v.Count - 1;
                for (int i = 0; i < seg; i++)
                {
                    int a = 1 + i, b = 1 + (i + 1) % seg;
                    t.Add(0); t.Add(b); t.Add(a);
                    t.Add(c); t.Add(a); t.Add(b);
                }
                _cone = new Mesh { name = "SEV_Cone" };
                _cone.SetVertices(v); _cone.SetTriangles(t, 0);
                _cone.RecalculateNormals(); _cone.RecalculateBounds();
                return _cone;
            }
        }

        public static GameObject Cone(Transform p, Vector3 pos, float diameter, float height, Material m, string name = "Cone")
        {
            var g = new GameObject(name);
            g.transform.SetParent(p, false);
            g.transform.localPosition = pos;
            g.transform.localScale = new Vector3(diameter, height, diameter);
            g.AddComponent<MeshFilter>().sharedMesh = ConeMesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        public static void SetLayerRecursive(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform c in g.transform) SetLayerRecursive(c.gameObject, layer);
        }
    }
}
