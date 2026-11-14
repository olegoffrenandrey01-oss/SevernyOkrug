// v12 «Графика и карта»: lights of the Severny city (placement is LAN-deterministic from RaidOptions.Seed, nothing is synced).
//  * lamp heads (materials lamp_warm*): no glow by day, dim in the evening / at night; parked cars' head / tail lights off;
//  * evening / night: about half of the 633 street lamps work — sodium (orange) or mercury (cold white), some flicker;
//    every working lamp gets a glow (core + halo, wider in fog / rain) — one ParticleSystem, no per-lamp objects;
//  * a pool of real spot lights follows the camera to the nearest working lamps (6–24 by «Качество графики»),
//    the nearest 2–4 of them cast soft shadows; reflection probes are rendered with the lamps around them (FocusAt);
//  * lit windows: 8% (evening) / 13% (night) of 1600 window slots on the facades glow warm / cold / dim behind curtains.
// Data: Resources/SevernyGfx/severny_lights.json (lamp heads and window rectangles, SevernyDress frame).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public class CityLights : MonoBehaviour
    {
        public static CityLights I { get; private set; }
        public static string LastInfo { get; internal set; } = "нет";

        [Serializable] class Data { public int version; public int lampCount; public int windowCount; public float[] lamps; public float[] windows; }

        struct Lamp { public Vector3 pos; public Color col; public float inten; public bool flicker, mercury; public int seed; }

        class Slot { public Light light; public int lamp = -1, want = -1; public float k; public bool shadow; }

        const float PickRange = 95f, ShadowRange = 40f, Fade = 0.35f;
        Lamp[] _lamps = new Lamp[0];
        Slot[] _slots = new Slot[0];
        int[] _order = new int[0], _mark = new int[0];
        float[] _dist = new float[0];
        int _stamp;
        ParticleSystem _glow;
        ParticleSystem.Particle[] _parts;
        int _partCount;
        readonly List<int> _flickLamp = new List<int>();     // lamp index of every flickering glow pair
        readonly List<int> _flickPart = new List<int>();     // index of its core particle (halo = +1)
        readonly List<Color32> _flickCol = new List<Color32>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        Vector3? _focus;
        float _nextPick, _nextFlick;
        int _windows, _swapped;

        public static CityLights Create(Transform parent, Transform cityRoot)
        {
            var go = new GameObject("SEV_CityLights");
            go.transform.SetParent(parent, false);
            var cl = go.AddComponent<CityLights>();
            cl.Init(cityRoot);
            return cl;
        }

        void Init(Transform cityRoot)
        {
            I = this;
            bool night = RaidOptions.Night, evening = RaidOptions.TimeOfDay == RaidOptions.TodEvening, dark = night || evening;
            Matrix4x4 frame;
            if (SevernyDress.Root != null) frame = SevernyDress.Root.localToWorldMatrix;
            else { SevernyDress.Align(cityRoot, out var p, out var yaw); frame = Matrix4x4.TRS(p, Quaternion.Euler(0f, yaw, 0f), Vector3.one); }

            // lamp heads / car lights (the FBX city and the dress)
            var variants = new Dictionary<Material, Material>();
            float lampMul = dark ? 0.22f : 0f;
            SwapLamps(cityRoot, lampMul, variants);
            if (SevernyDress.Root != null && SevernyDress.Root != cityRoot) SwapLamps(SevernyDress.Root, lampMul, variants);
            if (!dark) { LastInfo = "день: фонари не горят (" + _swapped + " объектов)"; return; }

            var ta = Resources.Load<TextAsset>(GfxRes.Root + "severny_lights");
            if (ta == null) { LastInfo = "нет Resources/SevernyGfx/severny_lights"; return; }
            Data data = null;
            try { data = JsonUtility.FromJson<Data>(ta.text); }
            catch (Exception e) { Debug.LogWarning("[Severny] severny_lights.json: " + e.Message); }
            Resources.UnloadAsset(ta);
            if (data == null || data.lamps == null) { LastInfo = "ошибка данных огней"; return; }

            // working lamps: 3 random numbers per lamp, always consumed (same set on every LAN client and every quality)
            var rng = new System.Random(RaidOptions.Seed ^ 0x51A3C5);
            double pOn = night ? 0.58 : 0.5;
            var list = new List<Lamp>();
            int total = data.lamps.Length / 3, flick = 0;
            for (int i = 0; i < total; i++)
            {
                double a = rng.NextDouble(), b = rng.NextDouble(), c = rng.NextDouble();
                if (a >= pOn) continue;
                var pos = frame.MultiplyPoint3x4(new Vector3(data.lamps[i * 3], data.lamps[i * 3 + 1], data.lamps[i * 3 + 2]));
                bool merc = b < 0.28, fl = c < 0.12;
                if (fl) flick++;
                list.Add(new Lamp { pos = pos, mercury = merc, flicker = fl, seed = i, inten = merc ? 11f : 14f,
                                    col = merc ? new Color(0.78f, 0.9f, 1f) : new Color(1f, 0.62f, 0.3f) });
            }
            _lamps = list.ToArray();
            _order = new int[_lamps.Length]; _dist = new float[_lamps.Length]; _mark = new int[_lamps.Length];
            BuildGlow();
            BuildPool();
            BuildWindows(data, frame, night);
            LastInfo = (night ? "ночь" : "вечер") + ": горят " + _lamps.Length + " из " + total + " фонарей (мерцают " + flick + "), источников " +
                       _slots.Length + " (с тенями " + CountShadow() + "), окон " + _windows;
            Debug.Log("[Severny] Огни города v12: " + LastInfo + ", плафонов " + _swapped);
        }

        int CountShadow() { int n = 0; foreach (var s in _slots) if (s.shadow) n++; return n; }

        // ------------------------------------------------------------------ lamp heads
        void SwapLamps(Transform root, float lampMul, Dictionary<Material, Material> variants)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                Material[] nm = null;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!variants.TryGetValue(m, out var v)) { v = LampVariant(m, lampMul); variants[m] = v; }
                    if (v == null) continue;
                    if (nm == null) nm = (Material[])mats.Clone();
                    nm[i] = v;
                }
                if (nm != null) { GfxSwap.Swap(r, nm); _swapped++; }
            }
        }

        static Material LampVariant(Material m, float lampMul)
        {
            string n = m.name;
            bool lamp = n.StartsWith("lamp_warm", StringComparison.OrdinalIgnoreCase);
            bool car = n.StartsWith("M_Headlight", StringComparison.OrdinalIgnoreCase) || n.StartsWith("M_Taillight", StringComparison.OrdinalIgnoreCase);
            if (!(lamp || car) || !m.HasProperty("_EmissionColor")) return null;
            var v = GfxSwap.Own(new Material(m) { name = n + " (v12)" });
            float k = lamp ? lampMul : 0f;
            if (k <= 0f) { v.SetColor("_EmissionColor", Color.black); v.DisableKeyword("_EMISSION"); }
            else
            {
                var e = m.GetColor("_EmissionColor");
                bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
                var l = (lin ? e.linear : e) * k;
                v.SetColor("_EmissionColor", lin ? l.gamma : l);
            }
            return v;
        }

        // ------------------------------------------------------------------ glows
        void BuildGlow()
        {
            var mat = GfxRes.FXMat("Glow");
            if (mat == null || _lamps.Length == 0) return;
            var go = new GameObject("LampGlow");
            go.transform.SetParent(transform, false);
            _glow = go.AddComponent<ParticleSystem>();
            _glow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _glow.main;
            main.loop = true; main.playOnAwake = false; main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1e6f; main.startSpeed = 0f; main.startSize = 1f;
            main.maxParticles = _lamps.Length * 2 + 4;
            var em = _glow.emission; em.enabled = false;
            var sh = _glow.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.None;          // additive: order does not matter
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.minParticleSize = 0f; r.maxParticleSize = 0.6f;
            bool thick = RaidOptions.Foggy || RaidOptions.Precip;
            float halo = thick ? 8f : 5.5f;
            byte haloA = (byte)(thick ? 70 : 46);
            _glow.Play();
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false, velocity = Vector3.zero, startLifetime = 1e6f };
            int pi = 0;
            for (int i = 0; i < _lamps.Length; i++)
            {
                var L = _lamps[i];
                var core = L.mercury ? new Color32(205, 228, 255, 255) : new Color32(255, 170, 92, 255);
                var hc = L.mercury ? new Color32(170, 205, 255, haloA) : new Color32(255, 140, 64, haloA);
                ep.position = L.pos + Vector3.down * 0.15f;
                ep.startSize = 1.3f; ep.startColor = core; _glow.Emit(ep, 1);
                ep.startSize = halo; ep.startColor = hc; _glow.Emit(ep, 1);
                if (L.flicker) { _flickLamp.Add(i); _flickPart.Add(pi); _flickCol.Add(core); _flickCol.Add(hc); }
                pi += 2;
            }
            _parts = new ParticleSystem.Particle[main.maxParticles];
            _partCount = _glow.GetParticles(_parts);
        }

        /// <summary>Flickering lamp: mostly on, sometimes stutters (deterministic per lamp, time-based).</summary>
        static float Flicker(int seed, float t)
        {
            float n = Mathf.PerlinNoise(seed * 0.173f, t * 0.9f);
            if (n < 0.55f) return 1f;
            float f = Mathf.PerlinNoise(seed * 0.71f + 13f, t * 14f);
            return f < 0.45f ? 0.08f : Mathf.Lerp(0.6f, 1f, f);
        }

        // ------------------------------------------------------------------ real lights
        void BuildPool()
        {
            foreach (var s in _slots) if (s.light != null) Destroy(s.light.gameObject);
            var q = GfxQuality.Current;
            int n = Mathf.Min(q.lampLights, _lamps.Length), ns = Mathf.Min(q.lampShadows, n);
            _slots = new Slot[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("LampLight" + i);
                go.transform.SetParent(transform, false);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // straight down
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.range = 26f; l.spotAngle = 128f; l.innerSpotAngle = 70f;
                l.intensity = 0f; l.enabled = false;
                l.shadows = i < ns ? LightShadows.Soft : LightShadows.None;
                l.shadowStrength = 0.85f;
                l.renderMode = LightRenderMode.ForcePixel;
                _slots[i] = new Slot { light = l, shadow = i < ns };
            }
            _nextPick = 0f;
        }

        /// <summary>Graphics preset changed: rebuild the light pool for the new count.</summary>
        public void OnQualityChanged()
        {
            if (_lamps.Length == 0) return;
            BuildPool();
            LastInfo = (RaidOptions.Night ? "ночь" : "вечер") + ": горят " + _lamps.Length + " фонарей, источников " + _slots.Length + " (с тенями " + CountShadow() + "), окон " + _windows;
        }

        /// <summary>Reflection probes: put the real lights around this point now (null = back to the camera).</summary>
        public void FocusAt(Vector3? p)
        {
            _focus = p;
            if (_slots.Length == 0) return;
            Vector3 c;
            if (p.HasValue) c = p.Value;
            else { var cam = Camera.main; if (cam == null) return; c = cam.transform.position; }
            Pick(c, true);
        }

        void Pick(Vector3 c, bool immediate)
        {
            int n = 0;
            float maxD = PickRange * PickRange, shD = ShadowRange * ShadowRange;
            for (int i = 0; i < _lamps.Length; i++)
            {
                float d = (_lamps[i].pos - c).sqrMagnitude;
                if (d < maxD) { _dist[n] = d; _order[n] = i; n++; }
            }
            Array.Sort(_dist, _order, 0, n);
            int ns = CountShadow(), sh = 0;
            while (sh < ns && sh < n && _dist[sh] < shD) sh++;
            Assign(true, 0, sh);
            Assign(false, sh, Mathf.Min(n, sh + (_slots.Length - ns)));
            if (immediate)
                foreach (var s in _slots)
                {
                    // reflection probes render this frame: position, colour, intensity and state right now
                    s.lamp = s.want; s.k = s.want >= 0 ? 1f : 0f; Place(s);
                    if (s.lamp >= 0) { s.light.intensity = _lamps[s.lamp].inten; s.light.enabled = true; }
                }
        }

        /// <summary>Slots of one kind keep their lamp if it is still wanted; free slots take the new lamps (fade out → in).</summary>
        void Assign(bool shadow, int from, int to)
        {
            int st = ++_stamp;
            for (int j = from; j < to; j++) _mark[_order[j]] = st;
            foreach (var s in _slots)
            {
                if (s.shadow != shadow) continue;
                if (s.lamp >= 0 && _mark[s.lamp] == st) { s.want = s.lamp; _mark[s.lamp] = -st; }
                else s.want = -1;
            }
            for (int j = from; j < to; j++)
            {
                int li = _order[j];
                if (_mark[li] != st) continue;
                Slot best = null;
                foreach (var s in _slots)
                    if (s.shadow == shadow && s.want < 0 && (best == null || s.k < best.k)) best = s;
                if (best == null) break;
                best.want = li; _mark[li] = -st;
            }
        }

        void Place(Slot s)
        {
            if (s.lamp < 0) { s.light.enabled = false; return; }
            var L = _lamps[s.lamp];
            s.light.transform.position = L.pos + Vector3.down * 0.35f;
            s.light.color = L.col;
        }

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            if (_slots.Length > 0 && !_focus.HasValue && t >= _nextPick)
            {
                _nextPick = t + 0.25f;
                var cam = Camera.main;
                if (cam != null) Pick(cam.transform.position, false);
            }
            foreach (var s in _slots)
            {
                if (s.want != s.lamp)
                {
                    s.k = Mathf.MoveTowards(s.k, 0f, dt / Fade);
                    if (s.k <= 0f) { s.lamp = s.want; Place(s); }
                }
                else if (s.lamp >= 0) s.k = Mathf.MoveTowards(s.k, 1f, dt / Fade);
                var l = s.light;
                if (s.lamp < 0 || s.k <= 0f) { if (l.enabled) l.enabled = false; continue; }
                var L = _lamps[s.lamp];
                l.intensity = L.inten * s.k * (L.flicker ? Flicker(L.seed, t) : 1f);
                if (!l.enabled) l.enabled = true;
            }
            if (_glow != null && _flickLamp.Count > 0 && t >= _nextFlick && _partCount > 0)
            {
                _nextFlick = t + 0.05f;
                for (int i = 0; i < _flickLamp.Count; i++)
                {
                    int p = _flickPart[i];
                    if (p + 1 >= _partCount) continue;
                    float f = Flicker(_lamps[_flickLamp[i]].seed, t);
                    _parts[p].startColor = Scale(_flickCol[i * 2], f);
                    _parts[p + 1].startColor = Scale(_flickCol[i * 2 + 1], f);
                }
                _glow.SetParticles(_parts, _partCount);
            }
        }

        static Color32 Scale(Color32 c, float f) => new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);

        // ------------------------------------------------------------------ windows
        void BuildWindows(Data data, Matrix4x4 frame, bool night)
        {
            var W = data.windows;
            if (W == null || W.Length < 8) return;
            var baseMat = GameAssets.Unlit(Color.white, false);
            if (baseMat == null || baseMat.shader == null) return;
            var rng = new System.Random(RaidOptions.Seed ^ 0x3D17);
            double frac = night ? 0.13 : 0.08;
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
            int count = W.Length / 8;
            for (int i = 0; i < count; i++)
            {
                double a = rng.NextDouble(), b = rng.NextDouble(), c = rng.NextDouble();
                if (a >= frac) continue;
                int o = i * 8;
                var p = frame.MultiplyPoint3x4(new Vector3(W[o], W[o + 1], W[o + 2]));
                var nrm = frame.MultiplyVector(new Vector3(W[o + 3], W[o + 4], W[o + 5]));
                nrm.y = 0f;
                if (nrm.sqrMagnitude < 1e-4f) continue;
                nrm.Normalize();
                if (MapAnchors.InsideZone(p - nrm * 1.2f)) continue;    // enterable interior: keep the real window
                float hw = W[o + 6] * 0.47f, hh = W[o + 7] * 0.47f;
                var right = Vector3.Cross(Vector3.up, -nrm);              // viewer's right when looking at the facade
                var ctr = p + nrm * 0.04f;
                int cls = b < 0.62 ? 0 : b < 0.86 ? 1 : 2;
                int variant = Mathf.Clamp((int)(c * 4.0), 0, 3);
                float u0 = variant * 0.25f + 0.004f, u1 = variant * 0.25f + 0.246f;
                int bi = v.Count;
                v.Add(ctr - right * hw - Vector3.up * hh); uv.Add(new Vector2(u0, 0f));
                v.Add(ctr - right * hw + Vector3.up * hh); uv.Add(new Vector2(u0, 1f));
                v.Add(ctr + right * hw + Vector3.up * hh); uv.Add(new Vector2(u1, 1f));
                v.Add(ctr + right * hw - Vector3.up * hh); uv.Add(new Vector2(u1, 0f));
                var t = tris[cls];
                t.Add(bi); t.Add(bi + 1); t.Add(bi + 2); t.Add(bi); t.Add(bi + 2); t.Add(bi + 3);
                _windows++;
            }
            if (_windows == 0) return;
            var mesh = new Mesh { name = "SEV_LitWindows" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 3;
            for (int k = 0; k < 3; k++) mesh.SetTriangles(tris[k], k, false);
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            var tex = WindowTexture();
            _owned.Add(tex);
            float e = night ? 1f : 0.8f;
            var cols = new[] { new Color(2.0f, 1.25f, 0.6f), new Color(1.1f, 1.35f, 1.6f), new Color(0.9f, 0.45f, 0.18f) };
            var mats = new Material[3];
            for (int k = 0; k < 3; k++)
            {
                var m = new Material(baseMat) { name = "SEV_LitWindows_" + k };
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                m.mainTexture = tex;
                var c = cols[k] * e;
                var g = GfxRes.Lin(c.r, c.g, c.b);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", g); else m.color = g;
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                mats[k] = m; _owned.Add(m);
            }
            var go = new GameObject("LitWindows");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>4 window looks in a 128×32 strip: open, curtains, half-closed blind, cross frame + curtain.</summary>
        static Texture2D WindowTexture()
        {
            const int S = 32;
            var tex = new Texture2D(S * 4, S, TextureFormat.RGBA32, true) { name = "SEV_WindowLook", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * 4 * S];
            for (int vi = 0; vi < 4; vi++)
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float u = (x + 0.5f) / S, w = (y + 0.5f) / S;
                        float l = 0.72f + 0.28f * w;                                     // lamp near the ceiling
                        l *= 1f - 0.35f * Mathf.Pow(Mathf.Abs(u - 0.5f) * 2f, 3f);       // darker towards the sides
                        bool frameP = x == 0 || x == S - 1 || y == 0 || y == S - 1;
                        bool mull = Mathf.Abs(x - S / 2 + 0.5f) < 1f || (y == (int)(S * 0.7f));
                        switch (vi)
                        {
                            case 1:                                                       // curtains left and right
                                float cu = Mathf.Min(u, 1f - u);
                                if (cu < 0.3f) l *= 0.42f + 0.16f * Mathf.Sin(u * 60f);
                                break;
                            case 2:                                                       // blind pulled half down
                                if (w > 0.58f) l *= (y % 3 == 0) ? 0.18f : 0.36f;
                                break;
                            case 3:                                                       // cross frame, curtain on the left
                                mull = Mathf.Abs(x - S / 2 + 0.5f) < 1f || Mathf.Abs(y - S / 2 + 0.5f) < 1f;
                                if (u < 0.42f) l *= 0.5f + 0.12f * Mathf.Sin(u * 50f);
                                break;
                        }
                        if (frameP || mull) l *= 0.12f;
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(l * 255f), 0, 255);
                        px[y * S * 4 + vi * S + x] = new Color32(b, b, b, 255);
                    }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear();
        }
    }
}
