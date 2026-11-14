// Winter variant of a map (raid setup → «Сезон: Зима»):
//  * ground and roads get snow materials (fresh snow / packed grey snow), ground decals, grass and weeds are hidden;
//  * tree crowns and bushes are frosted (their leaf textures are re-tinted to hoarfrost white);
//  * roofs, ledges, cars, fences, benches… get snow caps — real geometry ~5 cm thick with soft edges
//    (the FBX city is baked once by menu Severny > 10 Winter: Bake Snow, runtime maps are built during loading).
// Everything is restored when the raid ends (important when the map scene stays open in the editor).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Severny
{
    public static class Winter
    {
        public const string BakedName = "SEV_WinterSnow";
        public enum Kind : byte { Keep, Ground, Road, Other, Foliage, Hide }
        public enum Hint : byte { None, Skip, Ground, Road, Foliage, Grass, Trunk, Far }

        public static bool Active { get; private set; }
        public static string LastInfo { get; private set; } = "";

        static readonly List<KeyValuePair<Renderer, Material[]>> _swapped = new List<KeyValuePair<Renderer, Material[]>>();
        static readonly List<Renderer> _disabled = new List<Renderer>();
        static readonly Dictionary<Material, Material> _variants = new Dictionary<Material, Material>();
        static readonly Dictionary<Texture, Texture2D> _frostTex = new Dictionary<Texture, Texture2D>();
        static readonly Dictionary<string, List<Mesh>> _capCache = new Dictionary<string, List<Mesh>>();
        static GameObject _baked, _runtimeCaps;
        static Material _fresh, _road, _cap, _invisible;

        // ---------------------------------------------------------------- classification (shared with the editor bake)
        static bool Has(string s, params string[] keys)
        {
            foreach (var k in keys) if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool IsCutout(Material m) => m != null && (m.IsKeywordEnabled("_ALPHATEST_ON") || (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f));

        /// <summary>Renderer-level hint from the object / group names (FBX city: SM_Ground, SM_Road_*, *_Int, SM_Trees_*_Foliage…).</summary>
        public static Hint HintOf(Transform t, Transform root)
        {
            for (var p = t; p != null && p != root; p = p.parent)
            {
                string n = p.name;
                if (n == BakedName || n.StartsWith(BakedName)) return Hint.Skip;
                if (n.EndsWith("_Int") || n.Contains("_Int_") || n.Contains("_Int.") || Has(n, "interior")) return Hint.Skip;
                if (Has(n, "PK_glass")) return Hint.Skip;
                if (Has(n, "Foliage")) return Hint.Foliage;
                if (Has(n, "Trunk")) return Hint.Trunk;
                if (n.StartsWith("DETAIL_Vegetation") || Has(n, "Weeds", "LeafLitter")) return Hint.Grass;
                if (n.StartsWith("SM_Ground") || n == "ground" || Has(n, "Terrain", "Beach")) return Hint.Ground;
                if (n.StartsWith("SM_Road") || n == "roads" || Has(n, "Freeway", "Road_PCH", "Roads_")) return Hint.Road;
                if (n == "BACKDROP" || Has(n, "PK_far")) return Hint.Far;
            }
            return Hint.None;
        }

        public static Kind SlotKind(Material m, Hint h)
        {
            if (m == null || h == Hint.Skip) return Kind.Keep;
            string n = m.name;
            bool cut = IsCutout(m), transp = m.renderQueue >= 2900;
            if (h == Hint.Foliage) return Kind.Foliage;
            if (h == Hint.Grass) return Kind.Hide;
            if ((cut || transp) && Has(n, "roadpaint", "road_paint", "marking", "puddle", "stencil", "decal", "manhole", "litter", "oilstain", "skid", "crack")) return Kind.Hide;
            if (transp) return Kind.Keep;
            if (Has(n, "glass", "glazing", "window", "screen", "lens", "light", "siren", "neon", "sign", "mirror")) return Kind.Keep;
            if (cut && Has(n, "weed", "nettle", "grass", "fern", "flower", "reed", "clover", "dandelion")) return Kind.Hide;
            if (cut && Has(n, "leaf", "leaves", "foliage", "shrub", "bush", "hedge", "ivy", "palm", "needle", "pine", "fir", "spruce", "tree", "branch", "twig")) return Kind.Foliage;
            if (h == Hint.Trunk) return Kind.Keep;
            if (h == Hint.Ground) return Kind.Ground;
            if (h == Hint.Road) return Has(n, "curb", "kerb", "border", "bordur") ? Kind.Other : Has(n, "grass", "soil", "dirt", "lawn", "ground", "terrain") ? Kind.Ground : Kind.Road;
            if (Has(n, "roof", "wall", "facade", "brick", "plaster", "panel", "tile", "concrete", "metal", "wood", "paint")) return cut ? Kind.Keep : Kind.Other;
            if (Has(n, "asphalt", "tarmac", "road", "paving", "pavement", "slabs", "plaza", "sidewalk")) return Kind.Road;
            if (Has(n, "grass", "soil", "gravel", "leaflitter", "ground", "terrain", "dirt", "mud", "sand", "beach", "lawn", "earth")) return Kind.Ground;
            if (cut) return Kind.Keep;          // chain-link, razor wire, labels: thin and see-through, no caps
            return Kind.Other;
        }

        // ---------------------------------------------------------------- apply / restore
        /// <summary>Loading step of a winter raid. runtimeMap: meshes are readable (PolygonKit / greybox) → caps are built now
        /// (cached for PolygonKit); for the FBX city the baked caps (SEV_WinterSnow under MAP_ROOT) are switched on.</summary>
        public static IEnumerator Apply(Transform root, bool runtimeMap, Action<float> progress)
        {
            Restore();
            Active = true;
            SFX.SnowFootsteps = true;
            if (root == null) yield break;
            float t0 = Time.realtimeSinceStartup;
            int swapped = 0, hidden = 0, frosted = 0;
            var rends = root.GetComponentsInChildren<MeshRenderer>(false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled) continue;
                var h = HintOf(r.transform, root);
                if (h == Hint.Skip) continue;
                var mats = r.sharedMaterials;
                Material[] nm = null; int hide = 0;
                for (int s = 0; s < mats.Length; s++)
                {
                    var k = SlotKind(mats[s], h);
                    Material rep = null;
                    switch (k)
                    {
                        case Kind.Ground: rep = FreshSnow; break;
                        case Kind.Road: rep = RoadSnow; break;
                        case Kind.Foliage: rep = FrostOf(mats[s]); frosted++; break;
                        case Kind.Hide: rep = Invisible; hide++; break;
                    }
                    if (rep == null || rep == mats[s]) continue;
                    if (nm == null) nm = (Material[])mats.Clone();
                    nm[s] = rep;
                }
                if (nm == null) continue;
                if (hide == mats.Length) { r.enabled = false; _disabled.Add(r); hidden++; continue; }
                _swapped.Add(new KeyValuePair<Renderer, Material[]>(r, mats));
                r.sharedMaterials = nm;
                swapped++;
                if (sw.ElapsedMilliseconds > 15) { progress?.Invoke(0.25f * i / rends.Length); yield return null; sw.Restart(); }
            }
            // snow caps
            string caps;
            var baked = root.Find(BakedName);
            if (baked != null && baked.childCount > 0)
            {
                _baked = baked.gameObject;
                foreach (var r in baked.GetComponentsInChildren<MeshRenderer>(true)) SetupCapRenderer(r);
                _baked.SetActive(true);
                caps = "запечённый снег (" + baked.childCount + " ячеек)";
            }
            else if (runtimeMap)
            {
                string key = MapCatalog.Polygon.id == (GameBootstrap.I != null && GameBootstrap.I.CurrentMap != null ? GameBootstrap.I.CurrentMap.id : "") && root.name.StartsWith("PolygonKit") ? "polygon" : null;
                List<Mesh> meshes = null;
                if (key != null && _capCache.TryGetValue(key, out var cached) && cached.TrueForAll(x => x != null)) meshes = cached;
                if (meshes == null)
                {
                    var res = new SnowCapBuilder.Result();
                    yield return SnowCapBuilder.Build(root, true, res, t => progress?.Invoke(0.25f + 0.75f * t));
                    meshes = res.meshes;
                    if (key != null) { foreach (var m in meshes) m.hideFlags = HideFlags.DontUnloadUnusedAsset; _capCache[key] = meshes; }
                }
                _runtimeCaps = new GameObject(BakedName + "_Runtime");            // caps are in world space
                _runtimeCaps.transform.SetParent(root, true);
                foreach (var m in meshes)
                {
                    var g = new GameObject(m.name);
                    g.transform.SetParent(_runtimeCaps.transform, false);
                    g.AddComponent<MeshFilter>().sharedMesh = m;
                    SetupCapRenderer(g.AddComponent<MeshRenderer>());
                }
                caps = "снег построен (" + meshes.Count + " ячеек)";
            }
            else
            {
                caps = "снег на крышах не запечён";
                Hud.Notify("Зима: снег на крышах и машинах этой карты не запечён.\nВ Unity: Severny > 10 Winter: Bake Snow, или пересоберите игру через Severny > 6 — снег запечётся сам.", 9f);
            }
            progress?.Invoke(1f);
            LastInfo = "Winter: swapped " + swapped + ", hidden " + hidden + ", frosted slots " + frosted + ", " + caps + ", " +
                       Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f) + " ms";
            Debug.Log("[Severny] " + LastInfo);
        }

        /// <summary>Extra content loaded on top of a winter map (Severny city dress, v09): puddles, litter, stencils and weeds are hidden,
        /// shrubs frosted, soil / leaf litter covered with snow. Only material swaps (the merged meshes are not readable → no caps);
        /// undone by Restore() like the main pass.</summary>
        public static void ApplyExtra(Transform root)
        {
            if (!Active || root == null) return;
            int swapped = 0, hidden = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (r == null || !r.enabled) continue;
                var h = HintOf(r.transform, root);
                if (h == Hint.Skip) continue;
                var mats = r.sharedMaterials;
                Material[] nm = null; int hide = 0;
                for (int s = 0; s < mats.Length; s++)
                {
                    Material rep = null;
                    switch (SlotKind(mats[s], h))
                    {
                        case Kind.Ground: rep = FreshSnow; break;
                        case Kind.Road: rep = RoadSnow; break;
                        case Kind.Foliage: rep = FrostOf(mats[s]); break;
                        case Kind.Hide: rep = Invisible; hide++; break;
                    }
                    if (rep == null || rep == mats[s]) continue;
                    if (nm == null) nm = (Material[])mats.Clone();
                    nm[s] = rep;
                }
                if (nm == null) continue;
                if (hide == mats.Length) { r.enabled = false; _disabled.Add(r); hidden++; continue; }
                _swapped.Add(new KeyValuePair<Renderer, Material[]>(r, mats));
                r.sharedMaterials = nm;
                swapped++;
            }
            LastInfo += "; dress: swapped " + swapped + ", hidden " + hidden;
            Debug.Log("[Severny] Winter (city dress): swapped " + swapped + ", hidden " + hidden);
        }

        static void SetupCapRenderer(MeshRenderer r)
        {
            r.sharedMaterial = CapSnow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;   // v12
        }

        public static void Restore()
        {
            for (int i = 0; i < _swapped.Count; i++) if (_swapped[i].Key != null) _swapped[i].Key.sharedMaterials = _swapped[i].Value;
            _swapped.Clear();
            foreach (var r in _disabled) if (r != null) r.enabled = true;
            _disabled.Clear();
            if (_baked != null) _baked.SetActive(false);
            _baked = null;
            if (_runtimeCaps != null) Object.Destroy(_runtimeCaps);
            _runtimeCaps = null;
            foreach (var v in _variants.Values) if (v != null) Object.Destroy(v);
            _variants.Clear();
            foreach (var t in _frostTex.Values) if (t != null) Object.Destroy(t);
            _frostTex.Clear();
            Active = false;
        }

        // ---------------------------------------------------------------- materials
        public static Material FreshSnow => _fresh != null ? _fresh : (_fresh = SnowMat("SEV_Snow_Fresh", SnowTex(false, false), SnowTex(false, true), new Color(0.94f, 0.96f, 1f), 0.3f, 0.6f));
        public static Material RoadSnow => _road != null ? _road : (_road = SnowMat("SEV_Snow_Road", SnowTex(true, false), SnowTex(true, true), new Color(0.9f, 0.91f, 0.93f), 0.5f, 0.8f));
        public static Material CapSnow => _cap != null ? _cap : (_cap = SnowMat("SEV_Snow_Cap", SnowTex(false, false), SnowTex(false, true), new Color(0.97f, 0.98f, 1f), 0.28f, 0.5f));

        static Material Invisible
        {
            get
            {
                if (_invisible != null) return _invisible;
                var m = GameAssets.NewLitMaterial();
                m.name = "SEV_Winter_Hidden";
                m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
                if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 1f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                return _invisible = m;
            }
        }

        static Material SnowMat(string name, Texture2D albedo, Texture2D normal, Color c, float smooth, float bump)
        {
            var m = GameAssets.NewLitMaterial();
            m.name = name;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (normal != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Hoarfrost copy of a leaf material: the leaf texture is re-tinted on the GPU→CPU (alpha kept).</summary>
        static Material FrostOf(Material m)
        {
            if (m == null) return null;
            if (_variants.TryGetValue(m, out var v) && v != null) return v;
            v = new Material(m) { name = m.name + " (frost)" };
            Texture src = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
            Texture2D ft = null;
            if (src != null && !_frostTex.TryGetValue(src, out ft)) { ft = FrostTexture(src); _frostTex[src] = ft; }
            if (ft != null)
            {
                if (v.HasProperty("_BaseMap")) v.SetTexture("_BaseMap", ft);
                if (v.HasProperty("_MainTex")) v.SetTexture("_MainTex", ft);
                if (v.HasProperty("_BaseColor")) { var c = v.GetColor("_BaseColor"); v.SetColor("_BaseColor", new Color(0.97f, 0.98f, 1f, c.a)); }
            }
            else if (v.HasProperty("_BaseColor")) { var c = v.GetColor("_BaseColor"); v.SetColor("_BaseColor", new Color(0.75f, 0.8f, 0.82f, c.a)); }
            if (v.HasProperty("_Smoothness")) v.SetFloat("_Smoothness", 0.12f);
            if (v.HasProperty("_EmissionColor")) v.SetColor("_EmissionColor", Color.black);
            _variants[m] = v;
            return v;
        }

        static Texture2D FrostTexture(Texture src)
        {
            try
            {
                int w = Mathf.Clamp(src.width, 16, 512), h = Mathf.Clamp(src.height, 16, 512);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = src.name + "_frost", wrapMode = TextureWrapMode.Repeat, anisoLevel = 2 };
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var p = px[i];
                    float l = (0.3f * p.r + 0.59f * p.g + 0.11f * p.b) / 255f;
                    float f = Mathf.Lerp(l, 0.93f, 0.66f);                   // mostly white, leaf structure from the luminance
                    float k = 0.16f;                                        // a hint of the original hue
                    px[i] = new Color32((byte)Mathf.Clamp(255f * (f * 0.97f + (p.r / 255f - l) * k), 0, 255),
                                        (byte)Mathf.Clamp(255f * (f * 0.985f + (p.g / 255f - l) * k), 0, 255),
                                        (byte)Mathf.Clamp(255f * (f + (p.b / 255f - l) * k), 0, 255), p.a);
                }
                t.SetPixels32(px);
                t.Apply(true, true);
                return t;
            }
            catch (Exception e) { Debug.LogWarning("[Severny] Winter: frost texture failed for " + src.name + ": " + e.Message); return null; }
        }

        // ---------------------------------------------------------------- procedural snow textures (tileable)
        static Texture2D _tFresh, _tFreshN, _tRoad, _tRoadN;

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(s * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        static float VNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int X0 = ((x0 % period) + period) % period, Y0 = ((y0 % period) + period) % period, X1 = (X0 + 1) % period, Y1 = (Y0 + 1) % period;
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int period, int oct, int seed)
        {
            float s = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < oct; o++) { s += VNoise(u, v, period << o, seed + o * 17) * amp; norm += amp; amp *= 0.5f; }
            return s / norm;
        }

        /// <summary>Public for the 2D noise used by the cap builder (world-space thickness variation).</summary>
        public static float Noise2(float x, float z) => VNoise(x / 4096f, z / 4096f, 4096, 7);

        static Texture2D SnowTex(bool road, bool normal)
        {
            if (!road && !normal && _tFresh != null) return _tFresh;
            if (!road && normal && _tFreshN != null) return _tFreshN;
            if (road && !normal && _tRoad != null) return _tRoad;
            if (road && normal && _tRoadN != null) return _tRoadN;
            const int N = 256;
            var hgt = new float[N * N];
            var col = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float grain = Hash(x, y, road ? 5 : 3);
                    float h, r, g, b;
                    if (!road)
                    {
                        float dunes = Fbm(u, v, 3, 3, 11), fine = Fbm(u, v, 16, 3, 23);
                        h = dunes * 0.55f + fine * 0.35f + grain * 0.1f;
                        float lum = 0.9f + 0.07f * (fine - 0.5f) + 0.05f * (dunes - 0.5f) + (grain > 0.985f ? 0.06f : 0f);
                        float blue = (1f - dunes) * 0.06f;
                        r = lum - blue * 1.2f; g = lum - blue * 0.6f; b = lum + 0.01f;
                    }
                    else
                    {
                        float patches = Fbm(u, v, 4, 4, 31), streak = Fbm(u * 0.25f + v, v, 6, 3, 41), fine = Fbm(u, v, 24, 2, 53);
                        float dirt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.75f, patches * 0.7f + streak * 0.3f));
                        h = patches * 0.4f + fine * 0.45f + grain * 0.15f;
                        float lum = Mathf.Lerp(0.84f, 0.52f, dirt) + 0.05f * (fine - 0.5f);
                        r = lum * Mathf.Lerp(1f, 0.98f, dirt); g = lum * Mathf.Lerp(1f, 0.96f, dirt); b = lum * Mathf.Lerp(1.02f, 0.93f, dirt);
                    }
                    hgt[y * N + x] = h;
                    col[y * N + x] = new Color32((byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f), (byte)(Mathf.Clamp01(b) * 255f), 255);
                }
            Texture2D t;
            if (!normal)
            {
                t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = road ? "SEV_SnowRoad_C" : "SEV_Snow_C" };
                t.SetPixels32(col);
            }
            else
            {
                t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = road ? "SEV_SnowRoad_N" : "SEV_Snow_N" };
                var px = new Color32[N * N];
                float str = road ? 3f : 4f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = hgt[y * N + (x + 1) % N] - hgt[y * N + (x + N - 1) % N];
                        float dy = hgt[((y + 1) % N) * N + x] - hgt[((y + N - 1) % N) * N + x];
                        var n = new Vector3(-dx * str, -dy * str, 1f).normalized;
                        px[y * N + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
                    }
                t.SetPixels32(px);
            }
            t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
            t.Apply(true, true);
            if (!road && !normal) _tFresh = t; else if (!road) _tFreshN = t; else if (!normal) _tRoad = t; else _tRoadN = t;
            return t;
        }
    }

    // ==================================================================== snow cap geometry
    /// <summary>Builds snow caps on upward-facing surfaces of a map: world-space triangles with normal.y &gt; 0.6 that are not
    /// covered from above (own mesh = car roofs / shelters, other colliders = balconies, roofs), subdivided, welded per 48 m cell,
    /// offset ~5 cm with noise, thinner at the borders plus a small skirt so the snow has visible thickness.</summary>
    public static class SnowCapBuilder
    {
        public class Result
        {
            public readonly List<Mesh> meshes = new List<Mesh>();
            public readonly List<Mesh> unreadable = new List<Mesh>();
            public int renderers, candidates, covered, tris;
        }

        const float CellSize = 48f, MinNy = 0.6f, MinArea = 0.0025f, MaxEdge = 3.5f, Thick = 0.05f;

        class MeshCache { public Vector3[] v; public int[][] sub; }

        class Cell
        {
            public readonly List<Vector3> p = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            public readonly Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>();
            public readonly HashSet<(int, int, int)> seen = new HashSet<(int, int, int)>();
        }

        static bool CanRead(Mesh m)
        {
            if (m.isReadable) return true;
#if UNITY_EDITOR
            if (!Application.isPlaying) { try { return m.vertexCount == 0 || m.vertices.Length > 0; } catch { return false; } }
#endif
            return false;
        }

        public static IEnumerator Build(Transform root, bool runtimeMap, Result res, Action<float> progress, float sliceMs = 14f)
        {
            var list = new List<MeshRenderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (r == null || !r.enabled || r.isPartOfStaticBatch) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (r.GetComponent<LODGroup>() != null) continue;                     // small debris / interiors of runtime maps
                var b = r.bounds;
                if (Mathf.Max(b.size.x, b.size.y, b.size.z) < 0.35f) continue;
                if (runtimeMap && MapAnchors.Zones.Count > 0 && MapAnchors.InsideZone(b.center) && b.size.magnitude < 30f) continue;
                list.Add(r);
            }
            var cells = new Dictionary<long, Cell>();
            var cache = new Dictionary<Mesh, MeshCache>();
            bool back = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wv = new List<Vector3>(4096);
            for (int ri = 0; ri < list.Count; ri++)
            {
                var r = list[ri];
                if (r == null) continue;
                try { Process(r, root, runtimeMap, res, cells, cache, wv); }
                catch (Exception e) { Debug.LogWarning("[Severny] Snow caps: " + r.name + ": " + e.Message); }
                if (sw.ElapsedMilliseconds > sliceMs)
                {
                    progress?.Invoke(0.9f * ri / Mathf.Max(1, list.Count));
                    Physics.queriesHitBackfaces = back;
                    yield return null;
                    back = Physics.queriesHitBackfaces;
                    Physics.queriesHitBackfaces = true;
                    sw.Restart();
                }
            }
            Physics.queriesHitBackfaces = back;
            int ci = 0;
            foreach (var kv in cells)
            {
                var m = Finish(kv.Value, "SNOW_" + (int)(kv.Key >> 32) + "_" + (int)(kv.Key & 0xFFFFFFFF));
                if (m != null) { res.meshes.Add(m); res.tris += (int)(m.GetIndexCount(0) / 3); }
                if (++ci % 8 == 0 && sw.ElapsedMilliseconds > sliceMs) { progress?.Invoke(0.9f + 0.1f * ci / cells.Count); yield return null; sw.Restart(); }
            }
            progress?.Invoke(1f);
        }

        static void Process(MeshRenderer r, Transform root, bool runtimeMap, Result res, Dictionary<long, Cell> cells, Dictionary<Mesh, MeshCache> cache, List<Vector3> wv)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            var hint = Winter.HintOf(r.transform, root);
            if (hint == Winter.Hint.Skip || hint == Winter.Hint.Foliage || hint == Winter.Hint.Grass || hint == Winter.Hint.Trunk || hint == Winter.Hint.Far) return;
            var mats = r.sharedMaterials;
            int subs = Mathf.Min(mats.Length, mesh.subMeshCount);
            var kinds = new Winter.Kind[subs];
            bool any = false;
            for (int s = 0; s < subs; s++)
            {
                kinds[s] = Winter.SlotKind(mats[s], hint);
                if (kinds[s] == Winter.Kind.Foliage) return;                          // trees: crowns are frosted instead
                if (kinds[s] == Winter.Kind.Other) any = true;
            }
            if (!any) return;
            if (!CanRead(mesh)) { res.unreadable.Add(mesh); return; }
            if (!cache.TryGetValue(mesh, out var mc))
            {
                mc = new MeshCache { v = mesh.vertices, sub = new int[mesh.subMeshCount][] };
                for (int s = 0; s < mesh.subMeshCount; s++) mc.sub[s] = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s) : new int[0];
                cache[mesh] = mc;
            }
            res.renderers++;
            var M = r.transform.localToWorldMatrix;
            bool flip = M.determinant < 0f;
            wv.Clear();
            for (int i = 0; i < mc.v.Length; i++) wv.Add(M.MultiplyPoint3x4(mc.v[i]));
            // self-occlusion grid (xz) over all triangles of this renderer
            var grid = new SelfGrid(r.bounds, wv, mc.sub, subs);
            for (int s = 0; s < subs; s++)
            {
                if (kinds[s] != Winter.Kind.Other) continue;
                var tri = mc.sub[s];
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    int ia = tri[i], ib = flip ? tri[i + 2] : tri[i + 1], ic = flip ? tri[i + 1] : tri[i + 2];
                    Vector3 a = wv[ia], b = wv[ib], c = wv[ic];
                    var cr = Vector3.Cross(b - a, c - a);
                    float len = cr.magnitude;
                    if (len < MinArea * 2f) continue;
                    var n = cr / len;
                    if (n.y < MinNy) continue;
                    AddTri(a, b, c, n, 0, s * 1000003 + i, grid, res, cells, runtimeMap);
                }
            }
        }

        static void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int depth, int selfId, SelfGrid grid, Result res, Dictionary<long, Cell> cells, bool runtimeMap)
        {
            float e = Mathf.Max((a - b).sqrMagnitude, (b - c).sqrMagnitude, (c - a).sqrMagnitude);
            if (e > MaxEdge * MaxEdge && depth < 4)
            {
                Vector3 ab = (a + b) * 0.5f, bc = (b + c) * 0.5f, ca = (c + a) * 0.5f;
                AddTri(a, ab, ca, n, depth + 1, selfId, grid, res, cells, runtimeMap);
                AddTri(ab, b, bc, n, depth + 1, selfId, grid, res, cells, runtimeMap);
                AddTri(ca, bc, c, n, depth + 1, selfId, grid, res, cells, runtimeMap);
                AddTri(ab, bc, ca, n, depth + 1, selfId, grid, res, cells, runtimeMap);
                return;
            }
            res.candidates++;
            Vector3 cen = (a + b + c) / 3f;
            Vector3 o = cen + n * 0.06f;
            if (grid.Covered(o, selfId) || Physics.Raycast(o, Vector3.up, 80f, ~0, QueryTriggerInteraction.Ignore) ||
                (runtimeMap && MapAnchors.Zones.Count > 0 && MapAnchors.InsideZone(cen)))
            { res.covered++; return; }
            long key = ((long)Mathf.FloorToInt(cen.x / CellSize) << 32) | (uint)Mathf.FloorToInt(cen.z / CellSize);
            if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new Cell();
            int i0 = Weld(cell, a, n), i1 = Weld(cell, b, n), i2 = Weld(cell, c, n);
            if (i0 == i1 || i1 == i2 || i0 == i2) return;
            int lo = Mathf.Min(i0, Mathf.Min(i1, i2)), hi = Mathf.Max(i0, Mathf.Max(i1, i2)), mid = i0 + i1 + i2 - lo - hi;
            if (!cell.seen.Add((lo, mid, hi))) return;                                  // duplicated coplanar faces
            cell.t.Add(i0); cell.t.Add(i1); cell.t.Add(i2);
        }

        static int Weld(Cell c, Vector3 p, Vector3 n)
        {
            var k = new Vector3Int(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));
            if (c.weld.TryGetValue(k, out int i)) { c.n[i] += n; return i; }
            i = c.p.Count; c.p.Add(p); c.n.Add(n); c.weld[k] = i;
            return i;
        }

        static Mesh Finish(Cell c, string name)
        {
            if (c.t.Count < 3) return null;
            int vc = c.p.Count;
            // border vertices (edges used by one triangle) get thinner snow + a skirt down to the surface
            var edges = new Dictionary<long, int>();
            for (int i = 0; i < c.t.Count; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    int x = c.t[i + k], y = c.t[i + (k + 1) % 3];
                    long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                    edges.TryGetValue(key, out int cnt); edges[key] = cnt + 1;
                }
            var border = new bool[vc];
            foreach (var kv in edges) if (kv.Value == 1) { border[(int)(kv.Key >> 32)] = true; border[(int)(kv.Key & 0xFFFFFFFF)] = true; }
            var pos = new List<Vector3>(vc * 2); var nrm = new List<Vector3>(vc * 2); var uv = new List<Vector2>(vc * 2);
            var tris = new List<int>(c.t.Count * 2);
            var top = new Vector3[vc];
            for (int i = 0; i < vc; i++)
            {
                var n = c.n[i].normalized;
                float th = Thick * (0.7f + 0.6f * Winter.Noise2(c.p[i].x * 0.7f, c.p[i].z * 0.7f)) * (border[i] ? 0.5f : 1f);
                top[i] = c.p[i] + n * th;
                pos.Add(top[i]);
                nrm.Add(Vector3.Slerp(n, Vector3.up, 0.35f));
                uv.Add(new Vector2(c.p[i].x * 0.25f, c.p[i].z * 0.25f));
            }
            tris.AddRange(c.t);
            for (int i = 0; i < c.t.Count; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    int x = c.t[i + k], y = c.t[i + (k + 1) % 3], z = c.t[i + (k + 2) % 3];
                    long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                    if (edges[key] != 1) continue;
                    Vector3 mid = (c.p[x] + c.p[y]) * 0.5f, outv = mid - c.p[z];
                    outv -= Vector3.Project(outv, c.n[x].normalized);
                    if (outv.sqrMagnitude < 1e-8f) continue;
                    outv.Normalize();
                    Vector3 bx = c.p[x] - Vector3.up * 0.015f, by = c.p[y] - Vector3.up * 0.015f;
                    var sn = (outv + Vector3.up * 0.4f).normalized;
                    int b0 = pos.Count;
                    pos.Add(top[x]); pos.Add(top[y]); pos.Add(by); pos.Add(bx);
                    for (int q = 0; q < 4; q++) nrm.Add(sn);
                    float u0 = (c.p[x].x + c.p[x].z) * 0.25f, u1 = (c.p[y].x + c.p[y].z) * 0.25f;
                    uv.Add(new Vector2(u0, 0.02f)); uv.Add(new Vector2(u1, 0.02f)); uv.Add(new Vector2(u1, 0f)); uv.Add(new Vector2(u0, 0f));
                    // orient so the front face looks outwards (front normal = cross(p1-p0, p2-p0))
                    bool ok = Vector3.Dot(Vector3.Cross(pos[b0 + 1] - pos[b0], pos[b0 + 2] - pos[b0]), outv) > 0f;
                    if (ok) { tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2); tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 3); }
                    else { tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 1); tris.Add(b0); tris.Add(b0 + 3); tris.Add(b0 + 2); }
                }
            var m = new Mesh { name = name };
            if (pos.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(pos); m.SetNormals(nrm); m.SetUVs(0, uv);
            m.SetTriangles(tris, 0, true);
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Vertical-ray occlusion against the renderer's own triangles (car roof over the seats, bus stop roof…).</summary>
        class SelfGrid
        {
            readonly float _x0, _z0, _cs; readonly int _nx, _nz;
            readonly int[] _start, _items; readonly List<Vector3> _v; readonly int[] _tv;
            public SelfGrid(Bounds b, List<Vector3> v, int[][] sub, int subs)
            {
                _v = v;
                float ext = Mathf.Max(b.size.x, b.size.z);
                _cs = Mathf.Max(0.75f, ext / 160f);
                _x0 = b.min.x - 0.01f; _z0 = b.min.z - 0.01f;
                _nx = Mathf.Max(1, Mathf.CeilToInt((b.size.x + 0.02f) / _cs)); _nz = Mathf.Max(1, Mathf.CeilToInt((b.size.z + 0.02f) / _cs));
                int tc = 0; for (int s = 0; s < sub.Length; s++) tc += sub[s].Length / 3;
                _tv = new int[tc * 4];        // a, b, c, selfId
                var cnt = new int[_nx * _nz + 1];
                int t = 0;
                for (int s = 0; s < sub.Length; s++)
                    for (int i = 0; i + 2 < sub[s].Length; i += 3, t++)
                    {
                        _tv[t * 4] = sub[s][i]; _tv[t * 4 + 1] = sub[s][i + 1]; _tv[t * 4 + 2] = sub[s][i + 2]; _tv[t * 4 + 3] = s * 1000003 + i;
                        Range(t, out int x0, out int x1, out int z0, out int z1);
                        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) cnt[z * _nx + x]++;
                    }
                _start = new int[_nx * _nz + 1];
                for (int i = 0; i < _nx * _nz; i++) _start[i + 1] = _start[i] + cnt[i];
                _items = new int[_start[_nx * _nz]];
                var fill = new int[_nx * _nz];
                for (t = 0; t < tc; t++)
                {
                    Range(t, out int x0, out int x1, out int z0, out int z1);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { int c = z * _nx + x; _items[_start[c] + fill[c]++] = t; }
                }
            }

            void Range(int t, out int x0, out int x1, out int z0, out int z1)
            {
                Vector3 a = _v[_tv[t * 4]], b = _v[_tv[t * 4 + 1]], c = _v[_tv[t * 4 + 2]];
                x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - _x0) / _cs), 0, _nx - 1);
                x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) - _x0) / _cs), 0, _nx - 1);
                z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z, b.z, c.z) - _z0) / _cs), 0, _nz - 1);
                z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.z, b.z, c.z) - _z0) / _cs), 0, _nz - 1);
            }

            public bool Covered(Vector3 p, int selfId)
            {
                int x = Mathf.FloorToInt((p.x - _x0) / _cs), z = Mathf.FloorToInt((p.z - _z0) / _cs);
                if (x < 0 || z < 0 || x >= _nx || z >= _nz) return false;
                int c = z * _nx + x;
                for (int k = _start[c]; k < _start[c + 1]; k++)
                {
                    int t = _items[k];
                    if (_tv[t * 4 + 3] == selfId) continue;
                    Vector3 a = _v[_tv[t * 4]], b = _v[_tv[t * 4 + 1]], cc = _v[_tv[t * 4 + 2]];
                    if (Mathf.Max(a.y, b.y, cc.y) < p.y + 0.05f) continue;
                    // vertical line through (p.x, p.z): barycentric in xz
                    float d = (b.z - cc.z) * (a.x - cc.x) + (cc.x - b.x) * (a.z - cc.z);
                    if (Mathf.Abs(d) < 1e-9f) continue;
                    float l1 = ((b.z - cc.z) * (p.x - cc.x) + (cc.x - b.x) * (p.z - cc.z)) / d;
                    float l2 = ((cc.z - a.z) * (p.x - cc.x) + (a.x - cc.x) * (p.z - cc.z)) / d;
                    float l3 = 1f - l1 - l2;
                    if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                    float y = l1 * a.y + l2 * b.y + l3 * cc.y;
                    if (y > p.y + 0.05f) return true;
                }
                return false;
            }
        }
    }
}
