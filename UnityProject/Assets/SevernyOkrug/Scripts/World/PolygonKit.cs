// Runtime loader for the Blender-built map «ТЦ «Волна»» (map id "polygon"; v2 = big city map with the mall interior).
// Data: Resources/PolygonKit/{pk_geo.bytes, pk_map.json, Textures/*, PK_Sky.hdr}, written by Blender script pk_export.py.
// pk_geo.bytes holds meshes already in Unity space (static chunks per layer, collider chunks, prop prototypes);
// pk_map.json holds materials, prop instances (+ box/capsule colliders), spawn/exit anchors, bounds and lighting.
// If the kit is missing or broken, GameBootstrap falls back to the procedural GreyboxMap.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    [Serializable] public class PKMaterial { public string name, baseMap, normalMap, maskMap, mode; public float[] tint, emit; public float smooth, metal, nscale, clip; public int cull; }
    [Serializable] public class PKProto { public string name; public int mesh, tris; public float[] center, size; public float trunk; public int[] lods; public float[] lodd; }
    [Serializable] public class PKInstance { public int p, flags, col; public float[] pos, rot, scl; public float cull; }
    /// <summary>kind: spawn / exit / loot / item / bot / zone; v13: shot (screenshot point: pos = ground under the camera,
    /// yaw / pitch in degrees, fov = vertical field of view, 0 = the player's setting).</summary>
    [Serializable] public class PKAnchor { public string name, title, kind; public float[] pos, size; public float yaw, pitch, fov; }
    [Serializable] public class PKEnv
    {
        public string sky; public float skyExposure, skyRotation, sunIntensity, shadowStrength, fogDensity, reflIntensity;
        public float[] sunDir, sunColor, ambSky, ambEquator, ambGround, fogColor;
    }
    [Serializable] public class PKMap
    {
        public int version; public string source, title; public float[] boundsMin, boundsMax;
        public PKMaterial[] materials; public int[] statics; public PKProto[] protos; public PKInstance[] instances; public PKAnchor[] anchors; public PKEnv env;
    }

    public static class PolygonKit
    {
        public const string Res = "PolygonKit/";
        public const int F_NOSHADOW = 1, F_FAR = 2, F_COL = 4, F_NOWALK = 8, F_PROTO = 16, F_INTERIOR = 32, F_DETAIL = 64, F_DECAL = 128;
        /// <summary>Interior chunks (shops, halls, warehouses) are hidden beyond this distance — they are enclosed by the shell anyway.</summary>
        public const float InteriorCull = 230f;
        /// <summary>v13.1: small detail chunks (F_DETAIL: grass, small street bits) and decal-size chunks (F_DECAL: ground cracks / stains,
        /// window sills, AC units, pipes, roof antennas) are hidden beyond these distances (LODGroup cull, cross-fade).</summary>
        public const float DetailCull = 60f, DecalCull = 130f;
        /// <summary>v13.1: tan(36 deg) - LOD / cull distances of detail chunks and LOD prototypes are exact at the game camera's 72 deg vertical FOV
        /// (narrower FOV = scope zoom keeps the detail further, as it should).</summary>
        public const float LodTan = 0.7265425f;
        static PKMap _map;
        static bool _tried;
        static Material _sky, _skyInst;
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        static readonly List<Mesh> _meshes = new List<Mesh>();

        /// <summary>Root of all non-walkable colliders of the last built kit (NavMesh markup "Not Walkable").</summary>
        public static Transform NoWalkRoot { get; private set; }

        public static PKMap Map
        {
            get
            {
                if (_tried) return _map;
                _tried = true;
                var t = Resources.Load<TextAsset>(Res + "pk_map");
                if (t == null) return null;
                try { _map = JsonUtility.FromJson<PKMap>(t.text); }
                catch (Exception e) { Debug.LogError("[Severny] PolygonKit: pk_map.json не читается: " + e.Message); _map = null; }
                if (_map != null && (_map.materials == null || _map.statics == null || _map.protos == null || _map.instances == null)) { Debug.LogError("[Severny] PolygonKit: pk_map.json неполный"); _map = null; }
                return _map;
            }
        }

        public static bool Available => Map != null && Map.version >= 1 && Resources.Load<TextAsset>(Res + "pk_geo") != null;

        // ------------------------------------------------------------------ geometry
        public class GeoMesh { public string name; public int flags; public Mesh mesh; public int[] mats; }

        static int I32(byte[] d, ref int o) { int v = BitConverter.ToInt32(d, o); o += 4; return v; }
        static int Pad4(int n) => (n + 3) & ~3;

        public static List<GeoMesh> ParseGeo(byte[] d)
        {
            if (d == null || d.Length < 12 || d[0] != (byte)'P' || d[1] != (byte)'K' || d[2] != (byte)'G' || d[3] != (byte)'1')
                throw new Exception("pk_geo.bytes: неверный формат");
            int o = 4;
            int ver = I32(d, ref o), n = I32(d, ref o);
            if (ver != 1) throw new Exception("pk_geo.bytes: версия " + ver);
            var list = new List<GeoMesh>(n);
            for (int i = 0; i < n; i++)
            {
                int ln = I32(d, ref o);
                string name = Encoding.UTF8.GetString(d, o, ln); o += Pad4(ln);
                int flags = I32(d, ref o), v = I32(d, ref o), ns = I32(d, ref o);
                var fp = new float[v * 3]; Buffer.BlockCopy(d, o, fp, 0, v * 12); o += v * 12;
                var pos = new Vector3[v];
                for (int k = 0; k < v; k++) pos[k] = new Vector3(fp[3 * k], fp[3 * k + 1], fp[3 * k + 2]);
                bool col = (flags & F_COL) != 0;
                Vector3[] nrm = null; Vector2[] uv = null;
                if (!col)
                {
                    var sn = new short[v * 3]; Buffer.BlockCopy(d, o, sn, 0, v * 6); o += Pad4(v * 6);
                    nrm = new Vector3[v];
                    const float s = 1f / 32767f;
                    for (int k = 0; k < v; k++) nrm[k] = new Vector3(sn[3 * k] * s, sn[3 * k + 1] * s, sn[3 * k + 2] * s);
                    var fu = new float[v * 2]; Buffer.BlockCopy(d, o, fu, 0, v * 8); o += v * 8;
                    uv = new Vector2[v];
                    for (int k = 0; k < v; k++) uv[k] = new Vector2(fu[2 * k], fu[2 * k + 1]);
                }
                var m = new Mesh { name = name };
                if (v > 65535) m.indexFormat = IndexFormat.UInt32;
                m.vertices = pos;
                if (!col) { m.normals = nrm; m.uv = uv; }
                m.subMeshCount = Mathf.Max(1, ns);
                var mats = new int[ns];
                for (int s2 = 0; s2 < ns; s2++)
                {
                    mats[s2] = I32(d, ref o);
                    int ic = I32(d, ref o);
                    var idx = new int[ic];
                    if (v <= 65535)
                    {
                        var u = new ushort[ic]; Buffer.BlockCopy(d, o, u, 0, ic * 2); o += Pad4(ic * 2);
                        for (int k = 0; k < ic; k++) idx[k] = u[k];
                    }
                    else { Buffer.BlockCopy(d, o, idx, 0, ic * 4); o += ic * 4; }
                    m.SetTriangles(idx, s2, false);
                }
                m.RecalculateBounds();
                if (!col) m.RecalculateTangents();
                list.Add(new GeoMesh { name = name, flags = flags, mesh = m, mats = mats });
            }
            if (o + 5 > d.Length || d[o] != (byte)'P' || d[o + 4] != (byte)'D') throw new Exception("pk_geo.bytes: файл обрезан или повреждён");
            return list;
        }

        // ------------------------------------------------------------------ materials
        /// <summary>Normal maps and mask maps (metal/AO/smoothness) hold data, not colour.</summary>
        public static bool IsLinearTex(string n) => n.EndsWith("_N") || n.EndsWith("_MS") || n.EndsWith("_MSX");
        public static bool TexHasAlpha(string n) => n.EndsWith("_CA") || n.EndsWith("_MS") || n.EndsWith("_MSX");

        static readonly HashSet<string> _texImp = new HashSet<string>(), _texBaked = new HashSet<string>(), _texDisk = new HashSet<string>();

        /// <summary>Kit texture: imported texture (Resources/PolygonKit/Textures) → texture converted by the editor
        /// (Resources/PolygonKit/TexturesBaked, when Unity did not import the file as a texture) → in the editor: decoded from the file.</summary>
        static Texture2D Tex(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            var t = Resources.Load<Texture2D>(Res + "Textures/" + n);
            if (t != null) { _texImp.Add(n); return t; }
            t = Resources.Load<Texture2D>(Res + "TexturesBaked/" + n);
            if (t != null) { _texBaked.Add(n); return t; }
#if UNITY_EDITOR
            t = EditorDiskTex(n);
            if (t != null) { _texDisk.Add(n); return t; }
#endif
            return null;
        }

#if UNITY_EDITOR
        static Dictionary<string, string> _diskIndex;
        static readonly Dictionary<string, Texture2D> _diskCache = new Dictionary<string, Texture2D>();

        // Last resort for Play mode in the editor: read the JPG/PNG from Assets/…/Resources/PolygonKit/Textures directly.
        static Texture2D EditorDiskTex(string n)
        {
            if (_diskCache.TryGetValue(n, out var c) && c != null) return c;
            if (_diskIndex == null)
            {
                _diskIndex = new Dictionary<string, string>();
                try
                {
                    foreach (var dir in System.IO.Directory.GetDirectories(Application.dataPath, "PolygonKit", System.IO.SearchOption.AllDirectories))
                    {
                        string td = System.IO.Path.Combine(dir, "Textures");
                        if (!dir.Replace('\\', '/').EndsWith("/Resources/PolygonKit") || !System.IO.Directory.Exists(td)) continue;
                        foreach (var f in System.IO.Directory.GetFiles(td))
                        {
                            string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png") _diskIndex[System.IO.Path.GetFileNameWithoutExtension(f)] = f;
                        }
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Severny] PolygonKit: поиск текстур на диске: " + e.Message); }
            }
            if (!_diskIndex.TryGetValue(n, out var path)) return null;
            try
            {
                bool linear = IsLinearTex(n);
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
                Texture2D t = null;
                if (ImageConversion.LoadImage(tmp, System.IO.File.ReadAllBytes(path), false))
                {
                    t = new Texture2D(tmp.width, tmp.height, TextureFormat.RGBA32, true, linear)
                    { name = n, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                    t.SetPixels32(tmp.GetPixels32());
                    t.Apply(true, true);
                }
                if (Application.isPlaying) UnityEngine.Object.Destroy(tmp); else UnityEngine.Object.DestroyImmediate(tmp);
                if (t != null) _diskCache[n] = t;
                return t;
            }
            catch (Exception e) { Debug.LogWarning("[Severny] PolygonKit: " + n + ": " + e.Message); return null; }
        }
#endif
        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        /// <summary>Configure a URP Lit material from the kit description (used at runtime and by the editor bake).</summary>
        public static void Configure(Material m, PKMaterial d, Func<string, Texture2D> tex)
        {
            m.name = d.name;
            var t = d.tint != null && d.tint.Length >= 3 ? new Color(d.tint[0], d.tint[1], d.tint[2], d.tint.Length > 3 ? d.tint[3] : 1f) : Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", t);
            if (m.HasProperty("_Color")) m.SetColor("_Color", t);
            var bm = tex(d.baseMap);
            if (bm != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", bm); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", bm); }
            var nm = tex(d.normalMap);
            if (nm != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nm); SetF(m, "_BumpScale", d.nscale); m.EnableKeyword("_NORMALMAP"); }
            else m.DisableKeyword("_NORMALMAP");
            var ms = tex(d.maskMap);
            if (ms != null && m.HasProperty("_MetallicGlossMap"))
            {
                // R = metallic, G = AO, A = smoothness (scaled by _Smoothness)
                m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                SetF(m, "_SmoothnessTextureChannel", 0f);
                if (m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", ms); SetF(m, "_OcclusionStrength", 0.85f); m.EnableKeyword("_OCCLUSIONMAP"); }
            }
            else { m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_OCCLUSIONMAP"); }
            SetF(m, "_Smoothness", Mathf.Clamp01(d.smooth)); SetF(m, "_Glossiness", Mathf.Clamp01(d.smooth));
            SetF(m, "_Metallic", Mathf.Clamp01(d.metal));
            SetF(m, "_Cull", d.cull == 0 ? 0f : 2f);
            m.doubleSidedGI = d.cull == 0;
            SetF(m, "_ReceiveShadows", 1f); m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            if (d.mode == "cutout")
            {
                SetF(m, "_Surface", 0f); SetF(m, "_AlphaClip", 1f); SetF(m, "_Cutoff", d.clip > 0f ? d.clip : 0.5f);
                m.EnableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                SetF(m, "_SrcBlend", (float)BlendMode.One); SetF(m, "_DstBlend", (float)BlendMode.Zero); SetF(m, "_ZWrite", 1f);
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else if (d.mode == "transparent")
            {
                SetF(m, "_Surface", 1f); SetF(m, "_Blend", 0f); SetF(m, "_AlphaClip", 0f);
                SetF(m, "_SrcBlend", (float)BlendMode.SrcAlpha); SetF(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetF(m, "_SrcBlendAlpha", (float)BlendMode.One); SetF(m, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                SetF(m, "_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("ShadowCaster", false);
                // decal layering: grime < puddles < paint/graffiti < stencils
                int q = d.name.Contains("Grime") ? 0 : d.name.Contains("Puddle") ? 1 : d.name.Contains("Stencil") ? 3 : 2;
                m.renderQueue = (int)RenderQueue.Transparent - 50 + q;
            }
            else
            {
                SetF(m, "_Surface", 0f); SetF(m, "_AlphaClip", 0f);
                m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }
            if (d.emit != null && d.emit.Length >= 3 && (d.emit[0] + d.emit[1] + d.emit[2]) > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(d.emit[0], d.emit[1], d.emit[2]).gamma);
            }
            m.enableInstancing = true;
        }

        // material diagnostics of the last Build (baked assets that had to be fixed at runtime, textures that could not be loaded)
        static int _matBaked, _matHealed, _matRuntime, _matStale;
        static readonly List<string> _missingTex = new List<string>();

        static bool HasTex(Material m, string p) => m.HasProperty(p) && m.GetTexture(p) != null;

        /// <summary>True when a baked material asset matches its description: working shader and every referenced map assigned.
        /// (A package import can bake materials before Unity has imported their textures — such assets are repaired at runtime.)</summary>
        public static bool MaterialOk(Material m, PKMaterial d)
        {
            if (m == null || m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader") return false;
            if (!string.IsNullOrEmpty(d.baseMap) && !HasTex(m, "_BaseMap") && !HasTex(m, "_MainTex")) return false;
            if (!string.IsNullOrEmpty(d.normalMap) && m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") == null) return false;
            if (!string.IsNullOrEmpty(d.maskMap) && m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") == null) return false;
            return !Stale(m, d);
        }

        /// <summary>v13: the baked asset was made from an older pk_map.json (albedo calibration changed tint / smoothness).</summary>
        public static bool Stale(Material m, PKMaterial d)
        {
            if (m == null) return false;
            if (d.tint != null && d.tint.Length >= 3 && m.HasProperty("_BaseColor"))
            {
                var c = m.GetColor("_BaseColor");
                if (Mathf.Abs(c.r - d.tint[0]) > 0.01f || Mathf.Abs(c.g - d.tint[1]) > 0.01f || Mathf.Abs(c.b - d.tint[2]) > 0.01f) return true;
            }
            if (m.HasProperty("_Smoothness") && Mathf.Abs(m.GetFloat("_Smoothness") - Mathf.Clamp01(d.smooth)) > 0.01f) return true;
            return false;
        }

        static Texture2D TexChecked(string n)
        {
            var t = Tex(n);
            if (t == null && !string.IsNullOrEmpty(n) && !_missingTex.Contains(n)) _missingTex.Add(n);
            return t;
        }

        /// <summary>Kit material by description (baked asset or configured at runtime; cached by name) — also used by the Severny city dress.</summary>
        public static Material MaterialFor(PKMaterial d) => GetMaterial(d);

        static Material GetMaterial(PKMaterial d)
        {
            if (_mats.TryGetValue(d.name, out var m) && m != null) return m;
            var baked = Resources.Load<Material>(Res + "Materials/" + d.name);      // baked in the editor (keeps shader variants in builds)
            if (baked != null && MaterialOk(baked, d)) { m = baked; _matBaked++; }
            else
            {
                // self-heal: no baked asset, or it was baked without textures -> configure a runtime material from pk_map.json + Resources textures
                bool shaderOk = baked != null && baked.shader != null && baked.shader.isSupported && baked.shader.name != "Hidden/InternalErrorShader";
                bool stale = shaderOk && Stale(baked, d);
                m = shaderOk ? new Material(baked) : GameAssets.NewLitMaterial();
                Configure(m, d, TexChecked);
                if (stale) _matStale++;
                if (baked != null) _matHealed++; else _matRuntime++;
            }
            _mats[d.name] = m;
            return m;
        }

        static void LogMaterials(int total)
        {
            string s = "[Severny] PolygonKit materials: " + total + " (baked " + _matBaked + ", repaired at runtime " + _matHealed + ", created at runtime " + _matRuntime + ")";
            if (_matHealed + _matRuntime > 0)
                s += "; textures: imported " + _texImp.Count + ", converted (TexturesBaked) " + _texBaked.Count + ", read from files " + _texDisk.Count + ", missing " + _missingTex.Count;
            if (_texDisk.Count > 0 && _missingTex.Count == 0)
                Hud.Notify("Текстуры карты взяты прямо из файлов — в редакторе выполните меню Severny > Polygon: Reimport Kit Textures + Materials", 8f);
            if (_missingTex.Count > 0)
            {
                Debug.LogError(s + ". НЕ НАЙДЕНЫ ТЕКСТУРЫ (" + _missingTex.Count + "): " + string.Join(", ", _missingTex.GetRange(0, Mathf.Min(8, _missingTex.Count))) +
                               " — проверьте папку Assets/SevernyOkrug/Resources/PolygonKit/Textures и выполните меню Severny > Polygon: Reimport Kit Textures + Materials");
                Hud.Notify("Текстуры карты не найдены — см. Console", 8f);
            }
            else if (_matStale > 0 && _matStale >= _matHealed)
                Debug.LogWarning(s + ". " + _matStale + " материалов в Resources/PolygonKit/Materials устарели (цвет / гладкость из pk_map.json v13) — исправлено на лету; навсегда: меню Severny > Polygon: Reimport Kit Textures + Materials");
            else if (_matHealed > 0)
                Debug.LogWarning(s + ". Материалы в Resources/PolygonKit/Materials были без текстур — исправлено на лету; навсегда: меню Severny > Polygon: Reimport Kit Textures + Materials");
            else Debug.Log(s);
        }

        // ------------------------------------------------------------------ build
        static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Transform Child(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }

        public static Transform Build(Transform parent, out Bounds bounds)
        {
            var map = Map;
            if (map == null) throw new Exception("PolygonKit: нет pk_map.json");
            var ta = Resources.Load<TextAsset>(Res + "pk_geo");
            if (ta == null) throw new Exception("PolygonKit: нет pk_geo.bytes");
            float t0 = Time.realtimeSinceStartup;
            Unload();
            var geo = ParseGeo(ta.bytes);
            Resources.UnloadAsset(ta);
            foreach (var g in geo) _meshes.Add(g.mesh);
            var mats = new Material[map.materials.Length];
            _matBaked = _matHealed = _matRuntime = _matStale = 0; _missingTex.Clear(); _texImp.Clear(); _texBaked.Clear(); _texDisk.Clear();
            for (int i = 0; i < mats.Length; i++) mats[i] = GetMaterial(map.materials[i]);
            if (_matBaked + _matHealed + _matRuntime > 0) LogMaterials(mats.Length);
            Material[] MatsOf(GeoMesh g)
            {
                var r = new Material[g.mats.Length];
                for (int i = 0; i < r.Length; i++) r[i] = mats[Mathf.Clamp(g.mats[i], 0, mats.Length - 1)];
                return r;
            }

            var root = Child(parent, "PolygonKit_Volna");
            var stat = Child(root, "Static");
            var walk = Child(root, "Colliders_Walk");
            var nowalk = Child(root, "NoWalk");
            var props = Child(root, "Props");
            NoWalkRoot = nowalk;
            int renderers = 0, colliders = 0;
            foreach (int gi in map.statics)
            {
                if (gi < 0 || gi >= geo.Count) continue;
                var g = geo[gi];
                var go = new GameObject(g.name);
                if ((g.flags & F_COL) != 0)
                {
                    go.transform.SetParent((g.flags & F_NOWALK) != 0 ? nowalk : walk, false);
                    go.AddComponent<MeshCollider>().sharedMesh = g.mesh;
                    colliders++;
                    continue;
                }
                go.transform.SetParent(stat, false);
                go.AddComponent<MeshFilter>().sharedMesh = g.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = MatsOf(g);
                mr.shadowCastingMode = (g.flags & F_NOSHADOW) != 0 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;   // v12: street probes blend
                if ((g.flags & (F_INTERIOR | F_DETAIL | F_DECAL)) != 0)
                {
                    float cullDist = (g.flags & F_INTERIOR) != 0 ? InteriorCull : (g.flags & F_DETAIL) != 0 ? DetailCull : DecalCull;
                    var lg = go.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.01f, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                    float tn = (g.flags & F_INTERIOR) != 0 ? Mathf.Tan(30f * Mathf.Deg2Rad) : LodTan;
                    float hgt = Mathf.Clamp(lg.size / (2f * cullDist * tn), 0.002f, (g.flags & F_INTERIOR) != 0 ? 0.5f : 0.95f);
                    lg.SetLODs(new[] { new LOD(hgt, new Renderer[] { mr }) });
                    lg.fadeMode = LODFadeMode.CrossFade; lg.animateCrossFading = true;   // v12
                }
                renderers++;
            }
            // prototypes -> shared meshes/materials; instances -> GameObjects (+ colliders)
            var pMesh = new Mesh[map.protos.Length]; var pMats = new Material[map.protos.Length][];
            for (int i = 0; i < map.protos.Length; i++)
            {
                int gi = map.protos[i].mesh;
                if (gi < 0 || gi >= geo.Count) continue;
                pMesh[i] = geo[gi].mesh; pMats[i] = MatsOf(geo[gi]);
            }
            // v13.1: LOD chains (trees / shrubs): proto.lods = geo indices of LOD1..n, proto.lodd = switch distances (m) LOD0->1 ... last->culled
            var pLod = new Mesh[map.protos.Length][]; var pLodMats = new Material[map.protos.Length][][];
            int lodProtos = 0, lodInst = 0;
            for (int i = 0; i < map.protos.Length; i++)
            {
                var pr = map.protos[i];
                if (pMesh[i] == null || pr.lods == null || pr.lods.Length == 0 || pr.lodd == null || pr.lodd.Length < pr.lods.Length + 1) continue;
                var ms = new Mesh[pr.lods.Length]; var mm = new Material[pr.lods.Length][]; bool ok = true;
                for (int k = 0; k < pr.lods.Length; k++)
                {
                    int li = pr.lods[k];
                    if (li < 0 || li >= geo.Count || geo[li].mesh == null) { ok = false; break; }
                    ms[k] = geo[li].mesh; mm[k] = MatsOf(geo[li]);
                }
                if (!ok) { Debug.LogWarning("[Severny] PolygonKit: LOD-цепочка " + pr.name + " битая — только LOD0"); continue; }
                pLod[i] = ms; pLodMats[i] = mm; lodProtos++;
            }
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);
            foreach (var it in map.instances)
            {
                if (it.p < 0 || it.p >= map.protos.Length || pMesh[it.p] == null) continue;
                var pr = map.protos[it.p];
                var go = new GameObject(pr.name.Replace("P_", ""));
                bool hasCol = it.col != 0;
                go.transform.SetParent(hasCol || (it.flags & F_NOWALK) != 0 ? nowalk : props, false);
                go.transform.localPosition = V3(it.pos);
                if (it.rot != null && it.rot.Length == 4) go.transform.localRotation = new Quaternion(it.rot[0], it.rot[1], it.rot[2], it.rot[3]);
                if (it.scl != null && it.scl.Length == 3) go.transform.localScale = V3(it.scl);
                go.AddComponent<MeshFilter>().sharedMesh = pMesh[it.p];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = pMats[it.p];
                mr.shadowCastingMode = (it.flags & F_NOSHADOW) != 0 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                mr.lightProbeUsage = LightProbeUsage.Off;
                renderers++;
                var c = V3(pr.center); var s = V3(pr.size);
                if (it.col == 1)
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = c; bc.size = new Vector3(Mathf.Max(s.x, 0.1f), Mathf.Max(s.y, 0.1f), Mathf.Max(s.z, 0.1f));
                    colliders++;
                }
                else if (it.col == 2)
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.direction = 1;
                    float h = pr.trunk > 0f ? Mathf.Min(s.y, 4.5f) : s.y;      // trees: trunk only (canopy is see-through)
                    cc.radius = pr.trunk > 0f ? Mathf.Max(0.16f, pr.trunk * 1.1f) : 0.16f;
                    cc.height = Mathf.Max(h, cc.radius * 2f);
                    cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
                    colliders++;
                }
                if (pLod[it.p] != null)
                {
                    // v13.1: LOD0 = this renderer, LOD1..n = child renderers; the last LOD of a 3+ chain (billboard) casts no shadow
                    var lods = pLod[it.p]; var rs = new Renderer[lods.Length + 1]; rs[0] = mr;
                    for (int k = 0; k < lods.Length; k++)
                    {
                        var ch = new GameObject("LOD" + (k + 1));
                        ch.transform.SetParent(go.transform, false);
                        ch.AddComponent<MeshFilter>().sharedMesh = lods[k];
                        var r = ch.AddComponent<MeshRenderer>();
                        r.sharedMaterials = pLodMats[it.p][k];
                        r.shadowCastingMode = (k == lods.Length - 1 && lods.Length >= 2) ? ShadowCastingMode.Off : mr.shadowCastingMode;
                        r.lightProbeUsage = LightProbeUsage.Off;
                        rs[k + 1] = r; renderers++;
                    }
                    var lg = go.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.01f, rs) });
                    lg.RecalculateBounds();
                    var ls = go.transform.lossyScale;
                    float wsz = lg.size * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                    var L = new LOD[rs.Length]; float prev = 1f;
                    for (int k = 0; k < rs.Length; k++)
                    {
                        float h = Mathf.Clamp(wsz / (2f * Mathf.Max(1f, pr.lodd[k]) * LodTan), 0.0004f, 0.98f);
                        if (h >= prev) h = prev * 0.85f;
                        prev = h; L[k] = new LOD(h, new Renderer[] { rs[k] });
                    }
                    lg.SetLODs(L);
                    lg.fadeMode = LODFadeMode.CrossFade; lg.animateCrossFading = true;
                    lodInst++;
                }
                else if (it.cull > 0f)
                {
                    // hide small debris beyond ~cull*90 m (cans 36 m, weeds 72 m)
                    var lg = go.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.01f, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                    float dist = it.cull * 90f, sc = go.transform.lossyScale.x;
                    sc = Mathf.Max(Mathf.Abs(sc), Mathf.Abs(go.transform.lossyScale.y), Mathf.Abs(go.transform.lossyScale.z));
                    float hgt = Mathf.Clamp(lg.size * sc / (2f * dist * tanHalf), 0.0015f, 0.25f);
                    lg.SetLODs(new[] { new LOD(hgt, new Renderer[] { mr }) });
                    lg.fadeMode = LODFadeMode.CrossFade; lg.animateCrossFading = true;   // v12
                }
            }
            // anchors (spawns / exits / loot / items / bots / interior zones)
            MapAnchors.Clear();
            if (map.anchors != null)
                foreach (var a in map.anchors)
                {
                    var pt = new MapAnchors.Point { name = a.name, table = a.title, pos = V3(a.pos), yaw = a.yaw };
                    switch (a.kind)
                    {
                        case "spawn": MapAnchors.Spawns.Add(new MapAnchors.Spawn { name = a.name, pos = V3(a.pos), yaw = a.yaw }); break;
                        case "exit": MapAnchors.Exits.Add(new MapAnchors.Exit { name = a.name, title = string.IsNullOrEmpty(a.title) ? a.name : a.title, pos = V3(a.pos) }); break;
                        case "loot": MapAnchors.Loot.Add(pt); break;
                        case "item": MapAnchors.Items.Add(pt); break;
                        case "bot": MapAnchors.Bots.Add(pt); break;
                        case "zone":
                            if (a.size != null && a.size.Length >= 3)
                                MapAnchors.Zones.Add(new MapAnchors.Zone { name = a.name, center = V3(a.pos), size = V3(a.size), yaw = a.yaw });
                            break;
                        case "shot":
                            MapAnchors.Shots.Add(new MapAnchors.Shot { name = a.name, title = string.IsNullOrEmpty(a.title) ? a.name : a.title, pos = V3(a.pos), yaw = a.yaw, pitch = a.pitch, fov = a.fov });
                            break;
                    }
                }
            bounds = new Bounds();
            bounds.SetMinMax(V3(map.boundsMin), V3(map.boundsMax));
            Debug.Log("[Severny] PolygonKit built: " + geo.Count + " meshes, " + renderers + " renderers, " + colliders + " colliders, " +
                      map.instances.Length + " props (" + lodInst + " with LOD chains of " + lodProtos + " protos), anchors spawn/exit/loot/item/bot/zone/shot " + MapAnchors.Spawns.Count + "/" + MapAnchors.Exits.Count + "/" +
                      MapAnchors.Loot.Count + "/" + MapAnchors.Items.Count + "/" + MapAnchors.Bots.Count + "/" + MapAnchors.Zones.Count + "/" + MapAnchors.Shots.Count + ", " +
                      Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f) + " ms");
            return root;
        }

        /// <summary>Free the meshes of the last built kit (call after its GameObjects are destroyed).</summary>
        public static void Unload()
        {
            foreach (var m in _meshes) if (m != null) UnityEngine.Object.Destroy(m);
            _meshes.Clear();
            NoWalkRoot = null;
        }

        // ------------------------------------------------------------------ lighting (HDRI sky, fog, ambient) — call after EnvironmentSetup.Apply
        static Color Lin(float[] a, float k = 1f) => a != null && a.Length >= 3 ? new Color(a[0] * k, a[1] * k, a[2] * k).gamma : Color.gray;

        public static Material SkyMaterial()
        {
            if (_sky != null) return _sky;
            var baked = Resources.Load<Material>(Res + "Materials/PK_SkyMat");
            if (baked != null) return _sky = baked;
            var cube = Resources.Load<Cubemap>(Res + (Map != null && Map.env != null && !string.IsNullOrEmpty(Map.env.sky) ? Map.env.sky : "PK_Sky"));
            var sh = Shader.Find("Skybox/Cubemap");
            if (cube == null || sh == null) return null;
            _sky = new Material(sh) { name = "PK_SkyMat" };
            _sky.SetTexture("_Tex", cube);
            return _sky;
        }

        public static void ApplyEnvironment(EnvironmentSetup.Preset p)
        {
            var e = Map != null ? Map.env : null;
            if (e == null) return;
            float k = p == EnvironmentSetup.Preset.Evening ? 0.55f : p == EnvironmentSetup.Preset.Morning ? 0.8f : 1f;
            Color warm = p == EnvironmentSetup.Preset.Evening ? new Color(1f, 0.9f, 0.8f) : p == EnvironmentSetup.Preset.Morning ? new Color(1f, 0.96f, 0.92f) : Color.white;
            var sky = SkyMaterial();
            if (sky != null)
            {
                if (_skyInst != null) UnityEngine.Object.Destroy(_skyInst);
                sky = _skyInst = new Material(sky);   // per-raid copy (exposure/tint per time of day)
                SetF(sky, "_Exposure", e.skyExposure * k);
                SetF(sky, "_Rotation", e.skyRotation);
                if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", new Color(0.5f * warm.r, 0.5f * warm.g, 0.5f * warm.b, 0.5f));
                RenderSettings.skybox = sky;
                var cube = sky.HasProperty("_Tex") ? sky.GetTexture("_Tex") as Cubemap : null;
                if (cube != null)
                {
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = cube;
                    RenderSettings.reflectionIntensity = e.reflIntensity * Mathf.Lerp(1f, k, 0.5f);
                }
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Lin(e.ambSky, k) * warm;
            RenderSettings.ambientEquatorColor = Lin(e.ambEquator, k) * warm;
            RenderSettings.ambientGroundColor = Lin(e.ambGround, k) * warm;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = e.fogDensity * (p == EnvironmentSetup.Preset.Morning ? 1.6f : p == EnvironmentSetup.Preset.Evening ? 1.2f : 1f);
            RenderSettings.fogColor = Lin(e.fogColor, Mathf.Lerp(1f, k, 0.7f)) * warm;
            var sun = EnvironmentSetup.Sun;
            if (sun != null)
            {
                if (p == EnvironmentSetup.Preset.Day)
                {
                    var dir = V3(e.sunDir);
                    if (dir.sqrMagnitude > 0.01f) sun.transform.rotation = Quaternion.LookRotation(dir.normalized);
                    if (e.sunColor != null && e.sunColor.Length >= 3) sun.color = new Color(e.sunColor[0], e.sunColor[1], e.sunColor[2]);
                    sun.intensity = e.sunIntensity;
                }
                sun.shadowStrength = e.shadowStrength;
            }
            DynamicGI.UpdateEnvironment();
        }
    }

    /// <summary>Hand-placed points of the current map (filled by PolygonKit; empty for procedural maps).</summary>
    public static class MapAnchors
    {
        public struct Spawn { public string name; public Vector3 pos; public float yaw; }
        public struct Exit { public string name, title; public Vector3 pos; }
        /// <summary>Loot container / loose item / bot post: table = loot table name (Crate, Bag, …); yaw = facing.</summary>
        public struct Point { public string name, table; public Vector3 pos; public float yaw; }
        /// <summary>Oriented box of an interior (size = local x, height, z; yaw in degrees).</summary>
        public struct Zone
        {
            public string name; public Vector3 center, size; public float yaw;
            public bool Contains(Vector3 p)
            {
                var l = Quaternion.Euler(0f, -yaw, 0f) * (p - center);
                return Mathf.Abs(l.x) <= size.x * 0.5f && Mathf.Abs(l.y) <= size.y * 0.5f && Mathf.Abs(l.z) <= size.z * 0.5f;
            }
        }
        public static readonly List<Spawn> Spawns = new List<Spawn>();
        public static readonly List<Exit> Exits = new List<Exit>();
        public static readonly List<Point> Loot = new List<Point>();
        public static readonly List<Point> Items = new List<Point>();
        public static readonly List<Point> Bots = new List<Point>();
        public static readonly List<Zone> Zones = new List<Zone>();
        /// <summary>v13: screenshot points (ShotTool) — pos = ground under the camera, yaw / pitch in degrees, fov 0 = player's.</summary>
        public struct Shot { public string name, title; public Vector3 pos; public float yaw, pitch, fov; }
        public static readonly List<Shot> Shots = new List<Shot>();
        public static bool Has => Spawns.Count > 0 && Exits.Count >= 2;
        public static bool HasLoot => Loot.Count >= 20;
        public static bool InsideZone(Vector3 p) { foreach (var z in Zones) if (z.Contains(p)) return true; return false; }
        public static void Clear() { Spawns.Clear(); Exits.Clear(); Loot.Clear(); Items.Clear(); Bots.Clear(); Zones.Clear(); Shots.Clear(); }
    }
}
