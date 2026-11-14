// v12 «Графика и карта»: atmosphere of a raid (every map except the tutorial). Placement is LAN-deterministic from
// RaidOptions.Seed (the random numbers are consumed the same way on every client and every quality preset):
//  * rain (not winter): wet streets — asphalt / ground / walls darker and glossy, puddles almost mirror-like (material copies);
//    v13 «после дождя»: the same, a bit drier; with the «Волна» look also wet patches on asphalt / paving (VolnaLook.BuildWetFilm);
//  * smoke from 1–3 roofs drifting with the wind; in the evening / at night with a fire glow at its base;
//  * ground mist around the player in the morning, in fog, in rain and at night («Среднее» and above);
//  * dust motes in interiors, visible in the flashlight beam («Среднее» and above);
//  * crows circling over the city on calm days.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public class AtmosphereFX : MonoBehaviour
    {
        public static string LastInfo { get; internal set; } = "нет";

        readonly Dictionary<Material, Material> _wet = new Dictionary<Material, Material>();
        bool _after;
        readonly List<ParticleSystem> _smoke = new List<ParticleSystem>();
        readonly List<Light> _fireLights = new List<Light>();
        ParticleSystem _mist, _dust;
        Vector3 _wind;
        float _nextWind, _nextInside;
        bool _inside;

        public static AtmosphereFX Create(Transform parent, Bounds nav, params Transform[] roots)
        {
            var go = new GameObject("SEV_Atmosphere");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AtmosphereFX>();
            a.Init(nav, roots);
            return a;
        }

        void Init(Bounds nav, Transform[] roots)
        {
            var q = GfxQuality.Current;
            var rng = new System.Random(RaidOptions.Seed ^ 0x6F2B);
            float windYaw = (float)(rng.NextDouble() * 360.0), windSpeed = 0.6f + (float)rng.NextDouble() * 1.2f;
            _wind = Quaternion.Euler(0f, windYaw, 0f) * Vector3.forward * windSpeed;
            int tod = RaidOptions.TimeOfDay;
            bool night = RaidOptions.Night, evening = tod == RaidOptions.TodEvening, morning = tod == RaidOptions.TodMorning;
            bool winter = RaidOptions.Winter, rain = RaidOptions.Precip && !winter, fog = RaidOptions.Foggy;
            bool after = RaidOptions.AfterRain && !winter;
            var info = new StringBuilder();

            // 1) wet surfaces in rain / after rain (v13); the wet patches first — they look at the original materials
            if (((rain && !ShotFlags.NoWet) || after) && roots != null && roots.Length > 0 && VolnaLook.Active)
            {
                int ft = VolnaLook.BuildWetFilm(transform, roots[0], after);
                if (ft > 0) info.Append("мокрые пятна: ").Append(ft).Append(" треуг.; ");
            }
            if ((rain && !ShotFlags.NoWet) || after)
            {
                _after = after;
                int n = 0;
                if (roots != null) foreach (var r in roots) n += Wet(r);
                info.Append(after ? "после дождя: " : "мокрые поверхности: ").Append(n).Append(" объектов; ");
            }

            // 2) roof smoke
            var roofs = FindRoofs(nav, rng, 3);
            int show = q.fx == 0 ? Mathf.Min(1, roofs.Count) : roofs.Count;
            for (int i = 0; i < show; i++) MakeSmoke(roofs[i], i, night || evening, night && q.fx >= 2);
            info.Append("дым: ").Append(show).Append("; ");

            // 3) crows (random numbers always consumed)
            int flocks = rng.NextDouble() < 0.5 ? 1 : 2;
            var fc = new Vector3[2]; var fr = new float[2]; var fn = new int[2]; var fs = new float[2];
            for (int i = 0; i < 2; i++)
            {
                fc[i] = new Vector3(Mathf.Lerp(nav.min.x, nav.max.x, 0.2f + 0.6f * (float)rng.NextDouble()),
                                    nav.min.y + 32f + 16f * (float)rng.NextDouble(),
                                    Mathf.Lerp(nav.min.z, nav.max.z, 0.2f + 0.6f * (float)rng.NextDouble()));
                fr[i] = 25f + 20f * (float)rng.NextDouble();
                fn[i] = 6 + (int)(rng.NextDouble() * 5.0);
                fs[i] = 7f + 3f * (float)rng.NextDouble();
            }
            if (!night && !RaidOptions.Precip && !fog && nav.size.x > 60f)
            {
                for (int i = 0; i < flocks; i++) MakeCrows(fc[i], fr[i], fn[i], fs[i], i);
                info.Append("вороны: ").Append(flocks).Append("; ");
            }

            // 4) ground mist, 5) indoor dust
            if (q.fx >= 1 && (morning || fog || rain || night)) { MakeMist(night, fog); info.Append("туман у земли; "); }
            if (q.fx >= 1 && MapAnchors.Zones.Count > 0) { MakeDust(); info.Append("пыль в помещениях"); }
            LastInfo = info.Length > 0 ? info.ToString().TrimEnd(' ', ';') : "нет";
        }

        // ------------------------------------------------------------------ wet surfaces
        int Wet(Transform root)
        {
            if (root == null) return 0;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                var hint = Winter.HintOf(r.transform, root);
                if (hint == Winter.Hint.Skip || hint == Winter.Hint.Far) continue;     // interiors, glass, far backdrop
                var mats = r.sharedMaterials;
                Material[] nm = null;
                for (int i = 0; i < mats.Length; i++)
                {
                    var v = WetOf(mats[i], hint);
                    if (v == null) continue;
                    if (nm == null) nm = (Material[])mats.Clone();
                    nm[i] = v;
                }
                if (nm != null) { GfxSwap.Swap(r, nm); n++; }
            }
            return n;
        }

        Material WetOf(Material m, Winter.Hint h)
        {
            if (m == null) return null;
            if (_wet.TryGetValue(m, out var v)) return v;
            v = null;
            if (m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline/Lit") && m.HasProperty("_Smoothness") && m.HasProperty("_BaseColor"))
            {
                string n = m.name.ToLowerInvariant();
                float mul = 0f, smooth = 0f;
                if (n.Contains("puddle")) { mul = 0.55f; smooth = 0.95f; }
                else if (n.Contains("grounddecal")) { mul = _after ? 0.85f : 0.7f; smooth = _after ? 0.4f : 0.6f; }   // v13.1: cracks / stains / sand get wet with the road
                else if (m.renderQueue < 2450)
                {
                    // after rain (v13): drying — less dark, less glossy; the wet patches carry the reflections
                    var k = Winter.SlotKind(m, h);
                    if (k == Winter.Kind.Road) { mul = _after ? 0.85f : 0.6f; smooth = _after ? 0.45f : 0.82f; }   // v13 r3: 0.55 -> 0.45 (glare against the sun)
                    else if (k == Winter.Kind.Ground) { mul = _after ? 0.82f : 0.74f; smooth = _after ? 0.38f : 0.5f; }
                    else if (k == Winter.Kind.Other) { mul = _after ? 0.95f : 0.86f; smooth = _after ? 0.3f : 0.38f; }
                }
                if (mul > 0f)
                {
                    v = GfxSwap.Own(new Material(m) { name = m.name + " (wet)" });
                    var c = m.GetColor("_BaseColor");
                    bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    var l = (lin ? c.linear : c) * mul;
                    var g = lin ? l.gamma : l; g.a = c.a;
                    v.SetColor("_BaseColor", g);
                    if (v.HasProperty("_Color")) v.SetColor("_Color", g);
                    v.SetFloat("_Smoothness", Mathf.Max(m.GetFloat("_Smoothness"), smooth));
                }
            }
            _wet[m] = v;
            return v;
        }

        // ------------------------------------------------------------------ roof smoke
        static List<Vector3> FindRoofs(Bounds nav, System.Random rng, int want)
        {
            var res = new List<Vector3>();
            for (int i = 0; i < 48; i++)
            {
                float u = (float)rng.NextDouble(), w = (float)rng.NextDouble();
                if (res.Count >= want) continue;                 // keep consuming the same amount of numbers
                var o = new Vector3(Mathf.Lerp(nav.min.x + 20f, nav.max.x - 20f, u), nav.max.y + 150f, Mathf.Lerp(nav.min.z + 20f, nav.max.z - 20f, w));
                if (!Physics.Raycast(o, Vector3.down, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y - nav.min.y < 9f || hit.normal.y < 0.7f) continue;
                bool far = true;
                foreach (var p in res) if ((p - hit.point).sqrMagnitude < 60f * 60f) { far = false; break; }
                if (far) res.Add(hit.point);
            }
            return res;
        }

        static ParticleSystem NewSystem(Transform parent, string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.minParticleSize = 0f;
            return ps;
        }

        static Gradient Fade(float a0, float t1, float a1, float t2, float a2)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, t1), new GradientAlphaKey(a2, t2), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>Unlit particles: the colour a grey puff gets in this light (ambient + part of the sun), gamma.</summary>
        static Color LitGrey(float albedo)
        {
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color L(Color c) => lin ? c.linear : c;
            Color amb = L(RenderSettings.ambientEquatorColor) * 0.6f + L(RenderSettings.ambientSkyColor) * 0.4f;
            var sun = RenderSettings.sun;
            if (sun != null && sun.enabled) amb += L(sun.color) * sun.intensity * 0.3f;
            var c = amb * albedo; c.a = 1f;
            return lin ? c.gamma : c;
        }

        void MakeSmoke(Vector3 p, int idx, bool dark, bool fireLight)
        {
            var mat = GfxRes.FXMat("Smoke");
            if (mat == null) return;
            var ps = NewSystem(transform, "RoofSmoke" + idx, mat, 90);
            ps.transform.position = p + Vector3.up * 0.4f;
            var main = ps.main;
            main.duration = 10f; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(16f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var col = LitGrey(dark ? 0.6f : 0.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(col, col * 0.8f);
            var em = ps.emission; em.rateOverTime = 3.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.8f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 0.08f; lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = false;
            SetDrift(ps, _wind);
            var no = ps.noise; no.enabled = true; no.strength = 0.45f; no.frequency = 0.15f; no.scrollSpeed = 0.1f; no.damping = true; no.quality = ParticleSystemNoiseQuality.Medium;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 3.2f));
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var cl = ps.colorOverLifetime; cl.enabled = true; cl.color = new ParticleSystem.MinMaxGradient(Fade(0f, 0.08f, 0.55f, 0.5f, 0.35f));
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 2; ts.numTilesY = 2;
            ts.animation = ParticleSystemAnimationType.WholeSheet; ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.Distance; r.maxParticleSize = 1.5f;
            ps.Play();
            _smoke.Add(ps);
            if (!dark) return;
            // fire glow at the base (evening / night)
            var gm = GfxRes.FXMat("Glow");
            if (gm == null) return;
            var fire = NewSystem(ps.transform, "FireGlow", gm, 24);
            var fm = fire.main;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f); fm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            fm.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            fm.startColor = new ParticleSystem.MinMaxGradient(new Color32(255, 120, 40, 150), new Color32(255, 80, 24, 110));
            var fe = fire.emission; fe.rateOverTime = 7f;
            var fsh = fire.shape; fsh.shapeType = ParticleSystemShapeType.Sphere; fsh.radius = 0.6f;
            var fcl = fire.colorOverLifetime; fcl.enabled = true; fcl.color = new ParticleSystem.MinMaxGradient(Fade(0f, 0.2f, 1f, 0.6f, 0.6f));
            fire.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.8f;
            fire.Play();
            if (!fireLight) return;
            var lg = new GameObject("FireLight");
            lg.transform.SetParent(ps.transform, false);
            lg.transform.localPosition = Vector3.up * 1.2f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.intensity = 2.5f; l.color = new Color(1f, 0.55f, 0.25f); l.shadows = LightShadows.None;
            _fireLights.Add(l);
        }

        static void SetDrift(ParticleSystem ps, Vector3 wind)
        {
            if (ps == null) return;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(wind.z);
        }

        // ------------------------------------------------------------------ mist / dust / crows
        void MakeMist(bool night, bool fog)
        {
            var mat = GfxRes.FXMat("Mist");
            if (mat == null) return;
            _mist = NewSystem(transform, "GroundMist", mat, 260);
            var main = _mist.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 14f); main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var c = RenderSettings.fogColor; c.a = fog ? 0.1f : night ? 0.08f : 0.07f;
            main.startColor = c;
            var em = _mist.emission; em.rateOverTime = 22f;
            var sh = _mist.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(70f, 1.2f, 70f);
            SetDrift(_mist, _wind * 0.35f);
            var cl = _mist.colorOverLifetime; cl.enabled = true; cl.color = new ParticleSystem.MinMaxGradient(Fade(0f, 0.25f, 1f, 0.7f, 1f));
            var rol = _mist.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var r = _mist.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.Distance; r.maxParticleSize = 1.5f;
        }

        void MakeDust()
        {
            var mat = GfxRes.FXMat("Dust");
            if (mat == null) return;
            _dust = NewSystem(transform, "IndoorDust", mat, 220);
            var main = _dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f); main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new Color(0.8f, 0.78f, 0.72f, 0.55f);
            var em = _dust.emission; em.rateOverTime = 24f; em.enabled = false;
            var sh = _dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(9f, 3.2f, 9f);
            var no = _dust.noise; no.enabled = true; no.strength = 0.05f; no.frequency = 0.4f; no.scrollSpeed = 0.05f; no.quality = ParticleSystemNoiseQuality.Low;
            var cl = _dust.colorOverLifetime; cl.enabled = true; cl.color = new ParticleSystem.MinMaxGradient(Fade(0f, 0.2f, 1f, 0.8f, 1f));
            var r = _dust.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.None; r.maxParticleSize = 0.05f;
            _dust.Play();
        }

        void MakeCrows(Vector3 center, float radius, int count, float speed, int idx)
        {
            var mat = GfxRes.FXMat("Crow");
            if (mat == null) return;
            var ps = NewSystem(transform, "Crows" + idx, mat, count);
            ps.transform.position = center;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;    // orbital velocity turns around the flock centre
            main.duration = 5f;
            main.startLifetime = 1e5f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.25f);
            main.startColor = Color.white;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.35f; sh.rotation = new Vector3(90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f); vel.orbitalY = new ParticleSystem.MinMaxCurve(speed / radius); vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            vel.radial = new ParticleSystem.MinMaxCurve(0f);
            var no = ps.noise; no.enabled = true; no.strength = 0.8f; no.frequency = 0.06f; no.scrollSpeed = 0.05f; no.quality = ParticleSystemNoiseQuality.Low;
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 4; ts.numTilesY = 1;
            ts.animation = ParticleSystemAnimationType.WholeSheet; ts.timeMode = ParticleSystemAnimationTimeMode.FPS; ts.fps = 9f;
            ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.None; r.maxParticleSize = 0.3f;
            ps.Play();
        }

        void Follow(ParticleSystem ps, float below)
        {
            if (ps == null) return;
            var cam = Camera.main;
            if (cam != null) ps.transform.position = cam.transform.position + Vector3.down * below;
        }

        void Update()
        {
            float t = Time.time;
            if (t >= _nextWind)
            {
                _nextWind = t + 2f;
                var wf = WeatherFX.I;
                if (wf != null && wf.Wind.sqrMagnitude > 0.01f)
                {
                    var w = wf.Wind; w.y = 0f;
                    if ((w - _wind).sqrMagnitude > 0.04f)
                    {
                        _wind = w;
                        foreach (var s in _smoke) SetDrift(s, _wind);
                        SetDrift(_mist, _wind * 0.35f);
                    }
                }
            }
            if (t >= _nextInside)
            {
                _nextInside = t + 0.5f;
                var cam = Camera.main;
                bool inside = cam != null && MapAnchors.InsideZone(cam.transform.position);
                if (inside != _inside)
                {
                    _inside = inside;
                    if (_mist != null) { var e = _mist.emission; e.enabled = !inside; }
                    if (_dust != null) { var e = _dust.emission; e.enabled = inside; }
                }
            }
            for (int i = 0; i < _fireLights.Count; i++)
                if (_fireLights[i] != null) _fireLights[i].intensity = 1.8f + 1.4f * Mathf.PerlinNoise(i * 7.3f, t * 3.1f);
        }

        void LateUpdate()
        {
            // mist starts around the player camera (not while reflection probes are rendered during loading)
            if (_mist != null && !_mist.isPlaying && Camera.main != null) { Follow(_mist, 1.2f); _mist.Play(); }
            Follow(_mist, 1.2f);
            Follow(_dust, 0.2f);
        }
    }
}
