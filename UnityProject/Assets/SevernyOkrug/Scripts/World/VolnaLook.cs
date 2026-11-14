// v13.0 «ТЦ «Волна»: свет, небо, пост» — the look of the map «ТЦ «Волна»» (map id "polygon") by day, morning and evening:
//  * own CC0 skies (Poly Haven, Resources/VolnaLook): clear morning with a low sun in the east-north-east, day with cumulus,
//    warm evening, overcast for rain / fog; the bright spot of every sky sits where the sun light comes from (as GfxSky v12);
//  * light balance of the references: sun 2.0–2.8 under ACES, ambient = a spherical-harmonics probe calibrated per direction
//    (up ≈ 0.30–0.40, horizon, ground bounce) instead of the over-bright v12 trilight (> 1), full-strength soft shadows,
//    stronger SSAO outside (GfxQuality.SsaoLook);
//  * exp² fog of the horizon colour (silhouettes at 400–600 m melt into the haze) + the GfxHaze horizon band, sky reflections;
//  * «Волна» grade: neutral shadows, slightly warm mid-tones, blue sky, lower saturation, bloom + lens dirt, a soft sun flare;
//  * weather «После дождя» (RaidOptions.WeatherAfterRain): sun + wet patches on asphalt / paving (BuildWetFilm, AtmosphereFX).
// Everything is data (Resources/VolnaLook/vol_look.json). No data, night or winter → the v12 look stays (one log line).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    [Serializable] public class VolSky { public string name; public float sunU, sunEl, skyMul = 2f; public float[] band, zen, gnd; }

    [Serializable] public class VolLook
    {
        public string name, sky;
        public float sunAz, sunEl, sunLum = 2f, sunMul = 1f, sunWhite, shadow = 1f, wDim = 1f;
        public float[] sunColor;                                   // gamma (as Light.color)
        public float ambUp = 0.3f, ambHor = 0.28f, ambDown = 0.08f; // luminance of the ambient light per direction (linear)
        public float[] tintUp, tintHor, tintDown;                  // linear hue of the ambient (normalised to luminance 1)
        public float fog = 0.0016f, haze = 0.75f, hazeE0, hazeE1 = 8f, skyBright = 1f, refl = 1f;
        public float exposure, contrast, saturation, temperature, tint, bloom = 0.4f, threshold = 1.1f, scatter = 0.7f, dirt = 1f,
                     vignette = 0.2f, grain, flare = 1f, sslf, chromAb, ssao = 1f;
        public float[] filter, shadows, midtones, highlights;      // filter = gamma colour; SMH = (r, g, b, offset)
    }

    [Serializable] public class VolWet { public string name, pattern; public float tile = 24f, albedo = 0.025f, smooth = 0.93f, lift = 0.012f; }

    /// <summary>Extra reflection probe of the «Волна» look (puddles of the plaza, the parking, along the mall facades).</summary>
    [Serializable] public class VolProbe { public string name; public float[] pos, size; }
    [Serializable] public class VolData { public int version; public VolSky[] skies; public VolLook[] looks; public VolWet[] wet; public VolProbe[] probes; }

    /// <summary>Debug switches of the screenshot tool (ShotTool): v12 look («до») and the v12 effects one at a time.</summary>
    public static class ShotFlags
    {
        public static bool ForceV12, NoProbes, NoHaze, NoWet, V11Grade;
        public static bool Any => ForceV12 || NoProbes || NoHaze || NoWet || V11Grade;
        public static void Reset() { ForceV12 = NoProbes = NoHaze = NoWet = V11Grade = false; }
    }

    public static class VolnaLook
    {
        public const string Res = "VolnaLook/";
        static VolData _data;
        static bool _tried, _logged;
        static readonly Dictionary<string, Material> _skyMats = new Dictionary<string, Material>();
        static SphericalHarmonicsL2 _sh;
        static Color _up, _hor, _dn;
        static LensFlareComponentSRP _flare;
        static LensFlareDataSRP _flareData;
        static string _info = "нет";

        /// <summary>True while the v13 look is applied (raid on «ТЦ «Волна»», day / morning / evening, no winter).</summary>
        public static bool Active { get; private set; }
        public static bool ProbeOk { get; private set; }
        public static string Info => _info;
        public static VolLook Current { get; private set; }
        /// <summary>Denser reflection probes for ReflectionGrid while the v13 look is on (null otherwise).</summary>
        public static VolProbe[] ExtraProbes => Active && _data != null ? _data.probes : null;

        public static VolData Data
        {
            get
            {
                if (_tried) return _data;
                _tried = true;
                var t = Resources.Load<TextAsset>(Res + "vol_look");
                if (t == null) return null;
                try { _data = JsonUtility.FromJson<VolData>(t.text); }
                catch (Exception e) { Debug.LogError("[Severny] «Волна» v13: vol_look.json не читается: " + e.Message); _data = null; }
                if (_data != null && (_data.skies == null || _data.looks == null || _data.looks.Length == 0)) { Debug.LogError("[Severny] «Волна» v13: vol_look.json неполный"); _data = null; }
                Resources.UnloadAsset(t);
                return _data;
            }
        }

        static VolLook Look(string n) { if (Data != null) foreach (var l in Data.looks) if (l != null && l.name == n) return l; return null; }
        static VolSky Sky(string n) { if (Data != null) foreach (var s in Data.skies) if (s != null && s.name == n) return s; return null; }
        public static VolWet Wet(string n) { if (Data != null && Data.wet != null) foreach (var w in Data.wet) if (w != null && w.name == n) return w; return null; }

        static Color Lin(float[] a, Color def) => a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2], 1f) : def;
        static Vector4 V4(float[] a, Vector4 def) => a != null && a.Length >= 4 ? new Vector4(a[0], a[1], a[2], a[3]) : def;
        static bool LinearCS => QualitySettings.activeColorSpace == ColorSpace.Linear;
        static Color ToGamma(Color lin) { lin.a = 1f; return LinearCS ? lin.gamma : lin; }
        static void SetF(Material m, string n, float v) { if (m != null && m.HasProperty(n)) m.SetFloat(n, v); }

        static void Once(string msg)
        {
            if (_logged) return;
            _logged = true;
            Debug.Log("[Severny] «Волна» v13: " + msg);
        }

        // ------------------------------------------------------------------ apply
        /// <summary>Call after EnvironmentSetup.Apply + PolygonKit.ApplyEnvironment + EnvironmentSetup.ApplyModifiers (polygon only).
        /// False → nothing changed, the v12 look stays.</summary>
        public static bool Apply(EnvironmentSetup.Preset p, bool night, bool winter, int weather)
        {
            Active = false; Current = null;
            if (ShotFlags.ForceV12) { _info = "выкл (фототочки «до», свет v12)"; return false; }
            if (night || p == EnvironmentSetup.Preset.Night) { _info = "ночь — свет v12"; return false; }
            if (winter) { _info = "зима — свет v12"; return false; }
            if (p == EnvironmentSetup.Preset.Menu) return false;
            var d = Data;
            if (d == null) { _info = "нет данных — свет v12"; Once("нет Resources/VolnaLook/vol_look.json — свет и небо v12."); return false; }
            string tod = p == EnvironmentSetup.Preset.Morning ? "Morning" : p == EnvironmentSetup.Preset.Evening ? "Evening" : "Day";
            var tl = Look(tod);
            if (tl == null) { _info = "нет вида " + tod + " — свет v12"; Once("в vol_look.json нет вида «" + tod + "» — свет v12."); return false; }
            var L = weather == RaidOptions.WeatherPrecip ? Look("Precip") ?? tl : weather == RaidOptions.WeatherFog ? Look("Fog") ?? tl : tl;
            bool wx = L != tl;
            var sky = Sky(string.IsNullOrEmpty(L.sky) ? tod : L.sky);
            var mat = sky != null ? SkyMat(sky.name) : null;
            var sun = EnvironmentSetup.Sun;
            if (mat == null || sun == null)
            {
                _info = "нет неба " + (sky != null ? sky.name : L.sky) + " — свет v12";
                Once("нет неба VOL_Sky_" + (sky != null ? sky.name : L.sky) + " (Resources/VolnaLook) — свет и небо v12.");
                return false;
            }
            float dim = wx ? Mathf.Max(0.05f, tl.wDim) : 1f;

            // ---- sun: direction from the time of day, colour / brightness from the look (weather: whiter, weaker)
            float az = tl.sunAz * Mathf.Deg2Rad, el = Mathf.Clamp(tl.sunEl, 1f, 89f) * Mathf.Deg2Rad;
            var to = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
            sun.transform.rotation = Quaternion.LookRotation(-to);
            var sc = Lin(tl.sunColor, Color.white);
            float sunLum = tl.sunLum;
            if (wx) { sc = Color.Lerp(sc, Color.white, Mathf.Clamp01(L.sunWhite)); sunLum = tl.sunLum * L.sunMul * dim; }
            sc.a = 1f;
            float lum = GfxRes.Lum(LinearCS ? sc.linear : sc);
            sun.color = sc;
            sun.intensity = sunLum / Mathf.Max(0.05f, lum);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = Mathf.Clamp01(L.shadow);
            sun.enabled = true;
            RenderSettings.sun = sun;

            // ---- ambient: SH probe (up / horizon / down), Trilight colours as the fallback
            float ka = wx ? dim : 1f;
            _up = Tinted(L.ambUp * ka, L.tintUp); _hor = Tinted(L.ambHor * ka, L.tintHor); _dn = Tinted(L.ambDown * ka, L.tintDown);
            RenderSettings.ambientSkyColor = ToGamma(_up); RenderSettings.ambientEquatorColor = ToGamma(_hor); RenderSettings.ambientGroundColor = ToGamma(_dn);
            RenderSettings.ambientIntensity = 1f;
            _sh = BuildSH(_up, _hor, _dn);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = _sh;
            ProbeOk = true;

            // ---- sky: brightness via _Tint (gamma-correct), rotation so the sun spot of the HDRI matches the light
            float skyB = L.skyBright * (wx ? dim : 1f);
            float mul = Mathf.Max(0.01f, sky.skyMul * skyB);
            if (LinearCS) { float k = mul / 4.594794f; mat.SetColor("_Tint", new Color(k, k, k, 1f).gamma); }
            else mat.SetColor("_Tint", new Color(mul * 0.5f, mul * 0.5f, mul * 0.5f, 1f));
            float phiSun = Mathf.Atan2(to.z, to.x), phiB = (0.5f - sky.sunU) * 2f * Mathf.PI;
            mat.SetFloat("_Rotation", Mathf.Repeat((phiSun - phiB) * Mathf.Rad2Deg, 360f));
            RenderSettings.skybox = mat;
            var cube = Resources.Load<Cubemap>(Res + "VOL_SkyR_" + sky.name);
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = Mathf.Clamp(L.refl * skyB, 0.05f, 1f);
            }
            else RenderSettings.reflectionIntensity = Mathf.Clamp(L.refl * skyB * 0.8f, 0.05f, 1f);

            // ---- fog = sky just above the horizon (linear, measured from the texture) + horizon haze band
            var band = Lin(sky.band, new Color(0.45f, 0.48f, 0.52f)) * skyB;
            var fogC = ToGamma(band);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Max(0f, L.fog);
            RenderSettings.fogColor = fogC;
            if (ShotFlags.NoHaze) GfxHaze.Hide(); else GfxHaze.Set(fogC, Mathf.Clamp01(L.haze), L.hazeE0, Mathf.Max(L.hazeE0 + 0.5f, L.hazeE1));
            if (!ShotFlags.NoHaze) GfxSkyline.Set(fogC, Mathf.Max(0f, L.fog), Mathf.Clamp01(L.haze));     // v13.1: distant city on the horizon

            // ---- grade, SSAO, sun flare
            EnvironmentSetup.ApplyGrade(new EnvironmentSetup.GradeDef
            {
                exposure = L.exposure, contrast = L.contrast, saturation = L.saturation, temperature = L.temperature, tint = L.tint,
                bloom = L.bloom, threshold = L.threshold, vignette = L.vignette, dirt = L.dirt, grain = L.grain,
                filter = Lin(L.filter, Color.white), splitS = Color.grey, splitH = Color.grey, splitBalance = 0f,
                shadows = V4(L.shadows, new Vector4(1f, 1f, 1f, 0f)), midtones = V4(L.midtones, new Vector4(1f, 1f, 1f, 0f)),
                highlights = V4(L.highlights, new Vector4(1f, 1f, 1f, 0f)),
                v13 = true, scatter = L.scatter, chromAb = L.chromAb, sslf = L.sslf
            });
            GfxQuality.SsaoLook(L.ssao > 0f);
            SunFlare(sun, L.flare);

            Active = true; Current = L;
            _info = "«Волна» v13: небо " + sky.name + (wx ? " (" + L.name + ")" : "") + ", солнце " + sunLum.ToString("0.0#") + " @ " + tl.sunEl.ToString("0") + "°" +
                    ", амбиент ↑" + GfxRes.Lum(_up).ToString("0.00") + " ↔" + GfxRes.Lum(_hor).ToString("0.00") + " ↓" + GfxRes.Lum(_dn).ToString("0.00") +
                    ", туман " + L.fog.ToString("0.0000") + ", горизонт " + GfxSkyline.Info;
            return true;
        }

        static Color Tinted(float lum, float[] tint)
        {
            var t = Lin(tint, Color.white);
            float l = GfxRes.Lum(t);
            if (l < 1e-4f) t = Color.white; else t *= 1f / l;
            var c = t * Mathf.Max(0f, lum); c.a = 1f;
            return c;
        }

        // ------------------------------------------------------------------ SH ambient
        static readonly Vector3[] _dirs = { Vector3.up, Vector3.right, Vector3.down };
        static readonly Color[] _res = new Color[3];

        static void Eval(SphericalHarmonicsL2 sh, out float u, out float h, out float d)
        {
            sh.Evaluate(_dirs, _res);
            u = _res[0].r; h = _res[1].r; d = _res[2].r;
        }

        /// <summary>Probe whose irradiance (what Unity's shaders sample) is exactly up / hor / down at +Y / horizon / −Y:
        /// the zonal basis (constant, light from above, light from below) is measured with Evaluate and solved per channel.</summary>
        public static SphericalHarmonicsL2 BuildSH(Color up, Color hor, Color down)
        {
            var A = new SphericalHarmonicsL2(); A.AddAmbientLight(Color.white);
            var B = new SphericalHarmonicsL2(); B.AddDirectionalLight(Vector3.up, Color.white, 1f);
            var C = new SphericalHarmonicsL2(); C.AddDirectionalLight(Vector3.down, Color.white, 1f);
            Eval(A, out float au, out float ah, out float ad);
            Eval(B, out float bu, out float bh, out float bd);
            Eval(C, out float cu, out float ch, out float cd);
            // | au bu cu | |wa|   |U|
            // | ah bh ch | |wb| = |H|
            // | ad bd cd | |wc|   |D|
            float det = au * (bh * cd - ch * bd) - bu * (ah * cd - ch * ad) + cu * (ah * bd - bh * ad);
            var r = new SphericalHarmonicsL2();
            if (Mathf.Abs(det) < 1e-8f)
            {
                r.AddAmbientLight(new Color((up.r + hor.r + down.r) / 3f, (up.g + hor.g + down.g) / 3f, (up.b + hor.b + down.b) / 3f));
                return r;
            }
            for (int c = 0; c < 3; c++)
            {
                float U = up[c], H = hor[c], D = down[c];
                float wa = (U * (bh * cd - ch * bd) - bu * (H * cd - ch * D) + cu * (H * bd - bh * D)) / det;
                float wb = (au * (H * cd - ch * D) - U * (ah * cd - ch * ad) + cu * (ah * D - H * ad)) / det;
                float wc = (au * (bh * D - H * bd) - bu * (ah * D - H * ad) + U * (ah * bd - bh * ad)) / det;
                for (int i = 0; i < 9; i++) r[c, i] = wa * A[c, i] + wb * B[c, i] + wc * C[c, i];
            }
            return r;
        }

        static bool Same(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 9; i++)
                    if (Mathf.Abs(a[c, i] - b[c, i]) > 0.002f + 0.02f * Mathf.Abs(b[c, i])) return false;
            return true;
        }

        /// <summary>One frame after Apply: Unity must not have replaced the custom probe (skybox ambient / environment update).
        /// Re-assign once; if it is still lost, fall back to Trilight with the same colours. Logs the light balance.</summary>
        public static IEnumerator Calibrate()
        {
            if (!Active) yield break;
            yield return null;
            if (RenderSettings.ambientMode != AmbientMode.Custom || !Same(RenderSettings.ambientProbe, _sh))
            {
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientProbe = _sh;
                yield return null;
                if (RenderSettings.ambientMode != AmbientMode.Custom || !Same(RenderSettings.ambientProbe, _sh))
                {
                    ProbeOk = false;
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = ToGamma(_up); RenderSettings.ambientEquatorColor = ToGamma(_hor); RenderSettings.ambientGroundColor = ToGamma(_dn);
                    Debug.LogWarning("[Severny] «Волна» v13: SH-проба амбиента не удержалась — запасной вариант Trilight с теми же цветами.");
                    _info += " (Trilight)";
                }
            }
            Debug.Log("[Severny] " + _info + "\n" + BalanceLine());
        }

        /// <summary>«Light / shadow» of the current lighting (any look, v12 too): sun on the ground and on a wall facing it
        /// against the ambient there — the references have 3.5–5 on the ground (prompt v13, 3a.2).</summary>
        public static string BalanceLine()
        {
            var sun = RenderSettings.sun != null ? RenderSettings.sun : EnvironmentSetup.Sun;
            var sh = RenderSettings.ambientProbe;
            var res = new Color[3];
            sh.Evaluate(_dirs, res);
            float aU = Mathf.Max(1e-4f, GfxRes.Lum(res[0])), aH = Mathf.Max(1e-4f, GfxRes.Lum(res[1])), aD = GfxRes.Lum(res[2]);
            float sunE = 0f, sinh = 0f;
            if (sun != null && sun.enabled)
            {
                var c = sun.color; c.a = 1f;
                sunE = sun.intensity * GfxRes.Lum(LinearCS ? c.linear : c);
                sinh = Mathf.Max(0f, -sun.transform.forward.y);
            }
            float cosh = Mathf.Sqrt(Mathf.Max(0f, 1f - sinh * sinh));
            float ground = 1f + sunE * sinh / aU, wall = 1f + sunE * cosh / aH;
            var sb = new StringBuilder(160);
            sb.Append("Баланс света: солнце ").Append(sunE.ToString("0.00")).Append(" на ").Append((Mathf.Asin(sinh) * Mathf.Rad2Deg).ToString("0")).Append("°, тени ")
              .Append(sun != null ? sun.shadowStrength.ToString("0.00") : "-").Append("; амбиент ↑").Append(aU.ToString("0.000")).Append(" ↔").Append(aH.ToString("0.000"))
              .Append(" ↓").Append(aD.ToString("0.000")).Append(" (").Append(RenderSettings.ambientMode).Append("); свет/тень: земля ").Append(ground.ToString("0.0"))
              .Append(" (цель 3,5–5), стена к солнцу ").Append(wall.ToString("0.0")).Append("; туман ").Append(RenderSettings.fogDensity.ToString("0.0000"))
              .Append(", отражения ").Append(RenderSettings.reflectionIntensity.ToString("0.00"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ sky material
        static Material SkyMat(string n)
        {
            if (_skyMats.TryGetValue(n, out var m) && m != null) return m;
            m = null;
            var src = Resources.Load<Material>(Res + "Materials/VOL_SkyMat_" + n);
            if (src != null && src.shader != null && src.shader.isSupported && src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null) m = new Material(src);
            else
            {
                var sh = Shader.Find("Skybox/Panoramic");
                var tex = Resources.Load<Texture2D>(Res + "VOL_Sky_" + n);
                if (sh == null || tex == null) return null;
                m = new Material(sh);
                m.SetTexture("_MainTex", tex);
                SetF(m, "_Mapping", 1f); SetF(m, "_ImageType", 0f); SetF(m, "_MirrorOnBack", 0f); SetF(m, "_Layout", 0f);
                m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            }
            m.name = "VOL_Sky_" + n + " (runtime)";
            m.hideFlags = HideFlags.DontSave;
            SetF(m, "_Exposure", 1f);
            _skyMats[n] = m;
            return m;
        }

        // ------------------------------------------------------------------ sun flare (URP data-driven lens flare, no own shader)
        static LensFlareDataSRP BuildFlare()
        {
            var d = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            d.name = "VOL_SunFlare"; d.hideFlags = HideFlags.DontSave;
            LensFlareDataElementSRP E(float pos, float intensity, float scale, Color tint)
            {
                var e = new LensFlareDataElementSRP();
                e.flareType = SRPLensFlareType.Circle;
                e.position = pos; e.localIntensity = intensity; e.uniformScale = scale; e.sizeXY = Vector2.one;
                e.tint = tint; e.blendMode = SRPLensFlareBlendMode.Additive; e.modulateByLightColor = true; e.count = 1;
                return e;
            }
            d.elements = new[]
            {
                E(0f, 0.16f, 1.6f, new Color(1f, 0.9f, 0.76f)),      // soft glow around the sun
                E(0f, 0.08f, 4.2f, new Color(1f, 0.92f, 0.82f)),     // wide veil
                E(0.55f, 0.025f, 0.32f, new Color(0.75f, 0.88f, 1f)), // two faint ghosts on the flare axis
                E(1.25f, 0.018f, 0.5f, new Color(0.85f, 1f, 0.85f)),
            };
            return d;
        }

        static void SunFlare(Light sun, float k)
        {
            if (k <= 0.001f || sun == null) { if (_flare != null) _flare.enabled = false; return; }
            if (_flareData == null) _flareData = BuildFlare();
            if (_flare == null)
            {
                _flare = sun.GetComponent<LensFlareComponentSRP>();
                if (_flare == null) _flare = sun.gameObject.AddComponent<LensFlareComponentSRP>();
            }
            _flare.enabled = false;
            _flare.lensFlareData = _flareData;
            _flare.intensity = k; _flare.scale = 1f;
            _flare.useOcclusion = true; _flare.occlusionRadius = 0.6f;
            _flare.allowOffScreen = false; _flare.environmentOcclusion = false;
            _flare.enabled = true;
        }

        // ------------------------------------------------------------------ wet film («после дождя» / rain)
        static Texture2D _flatMask;

        /// <summary>Wet patches on asphalt / paving: up-facing triangles of the road materials of the kit (outside interiors),
        /// lifted by ~1 cm, world-space UV, a copy of M_Puddle (same keywords → same shader variants in builds) with the
        /// VOL_Wet_* alpha pattern, dark albedo and high smoothness. Returns the triangle count (0 = not built).</summary>
        public static int BuildWetFilm(Transform parent, Transform kitRoot, bool afterRain)
        {
            if (!Active || kitRoot == null) return 0;
            var w = Wet(afterRain ? "AfterRain" : "Rain");
            if (w == null) return 0;
            var pattern = Resources.Load<Texture2D>(Res + (string.IsNullOrEmpty(w.pattern) ? "VOL_Wet_" + w.name : w.pattern));
            var map = PolygonKit.Map;
            PKMaterial pd = null;
            if (map != null) foreach (var m in map.materials) if (m != null && m.name == "M_Puddle") { pd = m; break; }
            var puddle = pd != null ? PolygonKit.MaterialFor(pd) : null;
            if (pattern == null || puddle == null || !puddle.HasProperty("_BaseMap")) { Debug.Log("[Severny] «Волна» v13: нет VOL_Wet / M_Puddle — мокрые пятна не построены."); return 0; }
            var mat = GfxSwap.Own(new Material(puddle) { name = "VOL_WetFilm_" + w.name });
            mat.SetTexture("_BaseMap", pattern);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", pattern);
            mat.SetTextureScale("_BaseMap", Vector2.one); mat.SetTextureOffset("_BaseMap", Vector2.zero);
            var c = ToGamma(new Color(w.albedo, w.albedo, w.albedo)); c.a = 1f;
            mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (_flatMask == null)
            {
                _flatMask = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "VOL_FlatMask", hideFlags = HideFlags.DontSave };
                var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(0, 255, 0, 255);
                _flatMask.SetPixels32(px); _flatMask.Apply(false, false);
            }
            if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null) mat.SetTexture("_MetallicGlossMap", _flatMask);
            if (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") != null) mat.SetTexture("_OcclusionMap", _flatMask);
            SetF(mat, "_Smoothness", Mathf.Clamp01(w.smooth)); SetF(mat, "_Metallic", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent - 52;   // under grime (−50), puddles (−49) and paint

            var verts = new List<Vector3>(4096); var uvs = new List<Vector2>(4096); var idx = new List<int>(8192);
            int tris = 0;
            float inv = 1f / Mathf.Max(1f, w.tile);
            foreach (var mf in kitRoot.GetComponentsInChildren<MeshFilter>(false))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var mesh = mf.sharedMesh;
                if (mr == null || mesh == null || !mesh.isReadable) continue;
                var hint = Winter.HintOf(mf.transform, kitRoot);
                if (hint == Winter.Hint.Skip || hint == Winter.Hint.Far) continue;
                var mats = mr.sharedMaterials;
                Vector3[] mv = null;
                var tr = mf.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    var sm = mats[s];
                    if (sm == null || sm.renderQueue >= 2450 || Winter.SlotKind(sm, hint) != Winter.Kind.Road) continue;
                    if (mv == null) mv = mesh.vertices;
                    var ti = mesh.GetTriangles(s);
                    for (int t = 0; t + 2 < ti.Length; t += 3)
                    {
                        Vector3 a = tr.MultiplyPoint3x4(mv[ti[t]]), b = tr.MultiplyPoint3x4(mv[ti[t + 1]]), cc = tr.MultiplyPoint3x4(mv[ti[t + 2]]);
                        var n = Vector3.Cross(b - a, cc - a);
                        float len = n.magnitude;
                        if (len < 1e-6f || n.y / len < 0.8f) continue;
                        if (MapAnchors.InsideZone((a + b + cc) / 3f)) continue;
                        if (verts.Count + 3 > 60000) { Flush(parent, mat, verts, uvs, idx); }
                        int i0 = verts.Count;
                        verts.Add(new Vector3(a.x, a.y + w.lift, a.z)); uvs.Add(new Vector2(a.x * inv, a.z * inv));
                        verts.Add(new Vector3(b.x, b.y + w.lift, b.z)); uvs.Add(new Vector2(b.x * inv, b.z * inv));
                        verts.Add(new Vector3(cc.x, cc.y + w.lift, cc.z)); uvs.Add(new Vector2(cc.x * inv, cc.z * inv));
                        idx.Add(i0); idx.Add(i0 + 1); idx.Add(i0 + 2);
                        tris++;
                    }
                }
            }
            if (verts.Count > 0) Flush(parent, mat, verts, uvs, idx);
            return tris;
        }

        static void Flush(Transform parent, Material mat, List<Vector3> verts, List<Vector2> uvs, List<int> idx)
        {
            var m = new Mesh { name = "VOL_WetFilm", hideFlags = HideFlags.DontSave };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(idx, 0);
            var nrm = new Vector3[verts.Count]; for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.up;
            m.normals = nrm;
            var tan = new Vector4[verts.Count]; for (int i = 0; i < tan.Length; i++) tan[i] = new Vector4(1f, 0f, 0f, -1f);
            m.tangents = tan;
            m.RecalculateBounds();
            var go = new GameObject("VOL_WetFilm");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            _filmMeshes.Add(m);
            verts.Clear(); uvs.Clear(); idx.Clear();
        }

        static readonly List<Mesh> _filmMeshes = new List<Mesh>();

        // ------------------------------------------------------------------ restore
        /// <summary>Raid end (GameBootstrap.Cleanup, before EnvironmentSetup.Apply(Menu)).</summary>
        public static void Restore()
        {
            if (_flare != null) _flare.enabled = false;
            GfxQuality.SsaoLook(false);
            foreach (var m in _filmMeshes) if (m != null) UnityEngine.Object.Destroy(m);
            _filmMeshes.Clear();
            foreach (var kv in _skyMats) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _skyMats.Clear();
            if (RenderSettings.ambientMode == AmbientMode.Custom) RenderSettings.ambientMode = AmbientMode.Trilight;
            Active = false; Current = null;
            _info = "нет";
        }
    }
}
