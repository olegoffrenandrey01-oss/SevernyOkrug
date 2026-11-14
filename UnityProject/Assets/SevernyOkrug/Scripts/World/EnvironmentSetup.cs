// Overcast, cold, desaturated look: sun, trilight ambient, exp² fog, sky, URP post-processing.
// v12: HDRI sky (GfxSky — fog = horizon colour, ambient hue from the sky, horizon haze, sky reflections) and a colour grade per
// time of day × winter / rain / fog / night (ShadowsMidtonesHighlights: cold shadows, warm lights; split toning at dawn/dusk;
// bloom with lens dirt; finer grain). Without the v12 assets the procedural sky of v11 is used.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Severny
{
    public static class EnvironmentSetup
    {
        static GameObject _root;
        static Light _sun;
        static Volume _volume;
        static VolumeProfile _profile;
        static Preset _preset = Preset.Menu;
        public static Light Sun => _sun;
        public static ColorAdjustments ColorAdjust => _profile != null && _profile.TryGet(out ColorAdjustments c) ? c : null;
        public static Preset Current => _preset;

        public enum Preset { Menu, Morning, Day, Evening, Night }   // Night: base = Day, darkened by ApplyModifiers

        public static void Apply(Preset p, float fogDensity = 0.011f)
        {
            if (_root == null)
            {
                _root = new GameObject("SEV_Environment");
                Object.DontDestroyOnLoad(_root);
                var sg = new GameObject("Sun");
                sg.transform.SetParent(_root.transform, false);
                _sun = sg.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.Soft;
                var vg = new GameObject("PostFX");
                vg.transform.SetParent(_root.transform, false);
                _volume = vg.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 50f;
                _profile = BuildProfile();
                _volume.sharedProfile = _profile;
            }
            _preset = p;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != _sun && l.type == LightType.Directional && l.enabled) l.enabled = false;

            float elev, yaw, inten, fogMul = 1f; Color sunC, sky, eq, gnd, fog;
            switch (p)
            {
                case Preset.Morning: elev = 14f; yaw = 110f; inten = 0.65f; sunC = new Color(1f, 0.86f, 0.74f); sky = new Color(0.44f, 0.47f, 0.52f); eq = new Color(0.36f, 0.36f, 0.36f); gnd = new Color(0.17f, 0.17f, 0.16f); fog = new Color(0.58f, 0.6f, 0.62f); fogMul = 1.35f; break;
                case Preset.Evening: elev = 8f; yaw = 250f; inten = 0.5f; sunC = new Color(1f, 0.72f, 0.52f); sky = new Color(0.36f, 0.37f, 0.42f); eq = new Color(0.3f, 0.28f, 0.27f); gnd = new Color(0.14f, 0.13f, 0.12f); fog = new Color(0.47f, 0.46f, 0.47f); fogMul = 1.1f; break;
                case Preset.Menu: elev = 20f; yaw = 200f; inten = 0.45f; sunC = new Color(0.85f, 0.88f, 0.95f); sky = new Color(0.3f, 0.32f, 0.35f); eq = new Color(0.24f, 0.24f, 0.25f); gnd = new Color(0.1f, 0.1f, 0.1f); fog = new Color(0.35f, 0.36f, 0.38f); break;
                default: elev = 38f; yaw = 160f; inten = 0.85f; sunC = new Color(0.88f, 0.91f, 1f); sky = new Color(0.47f, 0.5f, 0.54f); eq = new Color(0.38f, 0.39f, 0.4f); gnd = new Color(0.18f, 0.18f, 0.17f); fog = new Color(0.56f, 0.59f, 0.61f); break;
            }
            // v12: HDRI sky → fog = sky above the horizon, ambient keeps the v11 brightness but takes the sky's hue
            bool hdri = GfxSky.Begin(p, out var zenL, out var bandL, out var gndL);
            if (hdri)
            {
                sky = Hue(sky, zenL, 0.6f); eq = Hue(eq, bandL, 0.6f); gnd = Hue(gnd, gndL, 0.5f);
                fog = Gamma(bandL);
            }
            _sun.transform.rotation = Quaternion.Euler(elev, yaw, 0f);
            _sun.intensity = inten; _sun.color = sunC; _sun.shadowStrength = 0.75f;
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = eq; RenderSettings.ambientGroundColor = gnd;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fogDensity * fogMul;
            RenderSettings.fogColor = fog;
            if (!hdri)
            {
                var sky0 = GameAssets.Skybox();
                if (sky0 != null)
                {
                    SetF(sky0, "_SunSize", 0.025f); SetF(sky0, "_SunSizeConvergence", 3f); SetF(sky0, "_AtmosphereThickness", 0.62f);
                    SetF(sky0, "_Exposure", p == Preset.Evening ? 0.75f : 0.95f);
                    if (sky0.HasProperty("_SkyTint")) sky0.SetColor("_SkyTint", new Color(0.42f, 0.44f, 0.47f));
                    if (sky0.HasProperty("_GroundColor")) sky0.SetColor("_GroundColor", fog * 0.8f);
                    RenderSettings.skybox = sky0;
                }
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = 0.55f;
            }
            RenderSettings.ambientIntensity = 1f;
            SetGrade(Grade(p == Preset.Night ? Preset.Day : p));
            GfxSky.Finish(false, false, 0, 1f);
            DynamicGI.UpdateEnvironment();
        }

        static Color Gamma(Color lin) { lin.a = 1f; return QualitySettings.activeColorSpace == ColorSpace.Linear ? lin.gamma : lin; }

        /// <summary>Keep the luminance of a v11 ambient colour (gamma), move its hue towards a measured sky colour (linear).</summary>
        static Color Hue(Color oldGamma, Color skyLin, float mix)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return oldGamma;
            var o = oldGamma.linear;
            float lo = GfxRes.Lum(o), ls = GfxRes.Lum(skyLin);
            if (ls < 1e-5f || lo < 1e-6f) return oldGamma;
            var s = skyLin * (lo / ls);
            var r = Color.Lerp(o, s, mix); r.a = 1f;
            return r.gamma;
        }

        // ------------------------------------------------------------------ colour grade
        /// <summary>Colour grade of the post-processing volume. v13 = true: bloom scatter, chromatic aberration and the
        /// screen-space lens flare come from the struct too (VolnaLook), otherwise the v12 defaults are used.</summary>
        public struct GradeDef
        {
            public float exposure, contrast, saturation, temperature, tint, bloom, threshold, vignette, dirt, grain;
            public Color filter, splitS, splitH; public float splitBalance;
            public Vector4 shadows, midtones, highlights;
            public bool v13; public float scatter, chromAb, sslf;
        }

        /// <summary>v13: a complete grade from outside (VolnaLook «ТЦ «Волна»»).</summary>
        public static void ApplyGrade(GradeDef g) => SetGrade(g);

        /// <summary>Screenshot tool «v11 grade»: the v11.1 colour correction (no shadows/midtones/highlights, split toning,
        /// lens dirt) — to see what the v12 grade changed.</summary>
        static GradeDef GradeV11(bool night, bool winter, int weather)
        {
            var g = new GradeDef
            {
                exposure = 0.15f, contrast = 14f, saturation = -28f, temperature = -9f, tint = 2f, bloom = 0.3f, threshold = 1.05f, vignette = 0.24f, dirt = 0f, grain = 0.1f,
                filter = new Color(0.94f, 0.98f, 1f), splitS = Color.grey, splitH = Color.grey, splitBalance = 0f,
                shadows = new Vector4(1f, 1f, 1f, 0f), midtones = new Vector4(1f, 1f, 1f, 0f), highlights = new Vector4(1f, 1f, 1f, 0f)
            };
            if (winter) { g.exposure = -0.05f; g.saturation = -22f; g.temperature = -15f; }
            if (weather == 2) g.saturation -= 6f; else if (weather == 3) g.saturation -= 4f;
            if (night) { g.exposure = 0.6f; g.saturation = -45f; g.temperature = -5f; g.bloom = 0.5f; g.vignette = 0.32f; }
            return g;
        }

        static GradeDef Grade(Preset p)
        {
            var g = new GradeDef
            {
                exposure = 0.15f, contrast = 16f, saturation = -24f, temperature = -8f, tint = 2f, bloom = 0.3f, threshold = 1.05f, vignette = 0.24f, dirt = 0.8f, grain = 0.1f,
                filter = new Color(0.95f, 0.98f, 1f), splitS = Color.grey, splitH = Color.grey, splitBalance = 0f,
                shadows = new Vector4(0.96f, 0.985f, 1.05f, 0f), midtones = new Vector4(1f, 1f, 1f, 0f), highlights = new Vector4(1.02f, 1f, 0.97f, 0f)
            };
            switch (p)
            {
                case Preset.Menu:
                    g.exposure = 0f; g.contrast = 12f; g.saturation = -26f; g.vignette = 0.3f; g.dirt = 0.6f; break;
                case Preset.Morning:
                    g.exposure = 0.2f; g.contrast = 13f; g.saturation = -16f; g.temperature = -3f; g.tint = 1f; g.bloom = 0.4f; g.threshold = 1f; g.vignette = 0.22f; g.dirt = 1f;
                    g.filter = new Color(0.97f, 0.98f, 1f); g.shadows = new Vector4(0.95f, 0.98f, 1.07f, 0f); g.highlights = new Vector4(1.05f, 1f, 0.94f, 0f);
                    g.splitS = new Color(0.44f, 0.48f, 0.58f); g.splitH = new Color(0.58f, 0.52f, 0.45f); g.splitBalance = 10f; break;
                case Preset.Evening:
                    g.exposure = 0.25f; g.contrast = 14f; g.saturation = -10f; g.temperature = 5f; g.tint = 3f; g.bloom = 0.45f; g.threshold = 1f; g.vignette = 0.26f; g.dirt = 1.1f;
                    g.filter = new Color(1f, 0.97f, 0.95f); g.shadows = new Vector4(0.95f, 0.97f, 1.08f, 0f); g.highlights = new Vector4(1.06f, 1f, 0.92f, 0f);
                    g.splitS = new Color(0.42f, 0.44f, 0.6f); g.splitH = new Color(0.62f, 0.52f, 0.4f); g.splitBalance = -10f; break;
            }
            return g;
        }

        static Texture2D _dirt;
        static bool _dirtTried;

        static void SetGrade(GradeDef g)
        {
            if (_profile == null) return;
            var visual = V14Settings.Current;
            if (visual.clearImage)
            {
                g.grain = Mathf.Min(g.grain, visual.grain);
                g.bloom *= visual.bloomMultiplier;
                g.dirt *= 0.3f;
                g.chromAb = 0f;
                g.vignette = Mathf.Min(g.vignette, 0.18f);
                g.sslf *= 0.4f;
            }
            if (_profile.TryGet(out ColorAdjustments ca)) { ca.postExposure.value = g.exposure; ca.contrast.value = g.contrast; ca.saturation.value = g.saturation; ca.colorFilter.value = g.filter; }
            if (_profile.TryGet(out WhiteBalance wb)) { wb.temperature.value = g.temperature; wb.tint.value = g.tint; }
            if (_profile.TryGet(out Bloom bl))
            {
                bl.intensity.value = g.bloom; bl.threshold.value = g.threshold;
                if (!_dirtTried) { _dirtTried = true; _dirt = GfxRes.Tex("SEV_LensDirt"); }
                if (_dirt != null) { bl.dirtTexture.Override(_dirt); bl.dirtIntensity.Override(g.dirt); }
                bl.highQualityFiltering.Override(GfxQuality.Current.bloomHQ);
                bl.scatter.Override(g.v13 ? Mathf.Clamp01(g.scatter) : 0.62f);
            }
            if (_profile.TryGet(out ChromaticAberration cab)) cab.intensity.Override(visual.clearImage ? 0f : g.v13 ? Mathf.Clamp01(g.chromAb) : 0.035f);
            // v13: screen-space lens flare (URP 17), added to the profile on first use
            float sslf = g.v13 ? Mathf.Max(0f, g.sslf) : 0f;
            if (sslf > 0f || _profile.Has<ScreenSpaceLensFlare>())
            {
                if (!_profile.TryGet(out ScreenSpaceLensFlare sl)) sl = _profile.Add<ScreenSpaceLensFlare>(false);
                sl.active = sslf > 0f;
                sl.intensity.Override(sslf); sl.streaksIntensity.Override(0f);
                sl.resolution.Override(ScreenSpaceLensFlareResolution.Quarter);
            }
            if (_profile.TryGet(out Vignette vg)) vg.intensity.value = g.vignette;
            if (_profile.TryGet(out FilmGrain fg)) fg.intensity.value = g.grain;
            if (_profile.TryGet(out ShadowsMidtonesHighlights smh)) { smh.shadows.Override(g.shadows); smh.midtones.Override(g.midtones); smh.highlights.Override(g.highlights); }
            if (_profile.TryGet(out SplitToning st))
            {
                bool on = g.splitS != Color.grey || g.splitH != Color.grey;
                st.active = on;
                st.shadows.Override(g.splitS); st.highlights.Override(g.splitH); st.balance.Override(g.splitBalance);
            }
        }

        /// <summary>Graphics preset changed (GfxQuality.Apply): post-processing quality switches.</summary>
        public static void OnQualityChanged()
        {
            if (_profile != null && _profile.TryGet(out Bloom bl)) bl.highQualityFiltering.Override(GfxQuality.Current.bloomHQ);
        }

        static Material _raidSky;
        static Color Mul(Color a, float r, float g, float b) => new Color(a.r * r, a.g * g, a.b * b, a.a);

        /// <summary>Winter / weather / night on top of the time-of-day preset (call after Apply and PolygonKit.ApplyEnvironment).
        /// weather: 1 clear, 2 rain/snowfall, 3 fog, 4 after rain (= clear here; v13 wet streets in AtmosphereFX).</summary>
        public static void ApplyModifiers(bool night, bool winter, int weather)
        {
            if (_sun == null) return;
            bool hdri = GfxSky.Active;
            Color sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, gnd = RenderSettings.ambientGroundColor;
            Color fog = RenderSettings.fogColor;
            float dens = RenderSettings.fogDensity, refl = RenderSettings.reflectionIntensity, skyExp = 1f, hdriMul = 1f;
            var g = Grade(_preset == Preset.Night ? Preset.Day : _preset);
            if (winter)
            {
                // snow bounces light up: the ground ambient approaches the horizon colour, everything turns colder and whiter
                gnd = Mul(Color.Lerp(gnd, eq, 0.85f), 0.97f, 1f, 1.06f);
                eq = Mul(eq, 1.02f, 1.05f, 1.12f);
                sky = Mul(sky, 0.96f, 1f, 1.08f);
                float fl = Mathf.Max(fog.grayscale, 0.5f);
                fog = Color.Lerp(fog, new Color(0.8f, 0.83f, 0.88f) * Mathf.Clamp(fl / 0.6f, 0.85f, 1.25f), 0.7f);
                dens *= 1.4f;
                _sun.color = Color.Lerp(_sun.color, new Color(0.9f, 0.94f, 1f), 0.6f);
                _sun.intensity *= 0.85f;
                hdriMul *= 1.12f;
                g.exposure -= 0.2f; g.temperature -= 6f; g.saturation -= 2f; g.contrast -= 2f;
                g.shadows = new Vector4(0.95f, 0.98f, 1.07f, 0f); g.highlights = new Vector4(1f, 1f, 1f, 0f);
            }
            if (weather == 2)
            {   // rain / snowfall: low overcast, soft shadows, denser haze
                _sun.intensity *= 0.5f; _sun.shadowStrength *= 0.6f;
                dens *= winter ? 1.55f : 1.3f;
                sky *= 0.9f; eq *= 0.92f; gnd *= 0.92f; fog *= winter ? 0.97f : 0.88f; skyExp *= 0.8f; hdriMul *= 0.8f;
                g.saturation -= 6f; g.contrast -= 2f; g.exposure -= 0.05f; g.vignette += 0.02f;
            }
            else if (weather == 3)
            {
                _sun.intensity *= 0.6f; _sun.shadowStrength *= 0.7f;
                dens *= 2.3f; skyExp *= 0.85f; hdriMul *= 0.85f;
                g.saturation -= 4f; g.contrast -= 5f; g.bloom += 0.05f;
            }
            else if (weather == RaidOptions.WeatherClear || weather == RaidOptions.WeatherAfterRain) { dens *= 0.85f; _sun.intensity *= 1.06f; }
            if (night)
            {
                // moonlight: very dark bluish ambient (brighter on snow), dark fog, dim moon as the "sun"
                float k = (winter ? 1.9f : 1f) * (weather == 2 ? 0.75f : weather == 3 ? 0.85f : 1f);
                sky = new Color(0.045f, 0.055f, 0.085f) * k; eq = new Color(0.032f, 0.037f, 0.052f) * k; gnd = new Color(0.018f, 0.02f, 0.026f) * (winter ? 2.6f : 1f);
                // v12 HDRI: the fog is the dark-blue horizon of the night sky (silhouettes stay readable); v11 sky: almost black
                fog = hdri ? GfxSky.NightFog(winter) : winter ? new Color(0.055f, 0.062f, 0.08f) : new Color(0.03f, 0.035f, 0.046f);
                if (hdri && weather == 2) fog *= 0.85f;
                dens *= 0.85f;
                _sun.transform.rotation = Quaternion.Euler(32f, 215f, 0f);
                _sun.color = new Color(0.55f, 0.65f, 1f);
                _sun.intensity = (winter ? 0.12f : 0.08f) * (weather == 2 ? 0.45f : weather == 3 ? 0.6f : 1f);
                _sun.shadowStrength = 0.6f;
                refl *= 0.15f; skyExp *= 0.05f;
                hdriMul = (winter ? 1.6f : 1f) * (weather == 2 ? 0.75f : weather == 3 ? 0.85f : 1f);
                g.exposure = winter ? 0.5f : 0.6f; g.contrast = 10f; g.saturation = -42f; g.temperature = -6f; g.tint = 0f;
                g.bloom = 0.6f; g.threshold = 0.95f; g.vignette = 0.32f; g.dirt = 1.4f; g.filter = new Color(0.92f, 0.96f, 1.04f);
                g.shadows = new Vector4(0.94f, 0.98f, 1.09f, 0f); g.midtones = new Vector4(0.99f, 1f, 1.02f, 0f); g.highlights = new Vector4(1.05f, 1f, 0.94f, 0f);
                g.splitS = Color.grey; g.splitH = Color.grey; g.splitBalance = 0f;
            }
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = eq; RenderSettings.ambientGroundColor = gnd;
            RenderSettings.fogColor = fog; RenderSettings.fogDensity = dens;
            RenderSettings.reflectionIntensity = refl;
            var skyM = RenderSettings.skybox;
            if (!hdri && skyM != null && skyExp < 0.999f)
            {
                // per-raid copy: never touch the shared material
                if (skyM != _raidSky)
                {
                    if (_raidSky != null) Object.Destroy(_raidSky);
                    _raidSky = new Material(skyM) { name = skyM.name + " (raid)" };
                    RenderSettings.skybox = skyM = _raidSky;
                }
                if (skyM.HasProperty("_Exposure")) skyM.SetFloat("_Exposure", skyM.GetFloat("_Exposure") * skyExp);
                if (night && skyM.HasProperty("_SkyTint")) skyM.SetColor("_SkyTint", new Color(0.2f, 0.24f, 0.33f));
                if (skyM.HasProperty("_GroundColor")) skyM.SetColor("_GroundColor", fog);
            }
            if (ShotFlags.V11Grade) g = GradeV11(night, winter, weather);
            SetGrade(g);
            GfxSky.Finish(night, winter, weather, hdriMul);
            DynamicGI.UpdateEnvironment();
        }

        static void SetF(Material m, string n, float v) { if (m.HasProperty(n)) m.SetFloat(n, v); }

        static VolumeProfile BuildProfile()
        {
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            prof.name = "SEV_PostFX";
            var tm = prof.Add<Tonemapping>(false); tm.mode.Override(TonemappingMode.ACES);
            var ca = prof.Add<ColorAdjustments>(false);
            ca.postExposure.Override(0.15f); ca.contrast.Override(14f); ca.saturation.Override(-28f);
            ca.colorFilter.Override(new Color(0.94f, 0.98f, 1f));
            var wb = prof.Add<WhiteBalance>(false); wb.temperature.Override(-9f); wb.tint.Override(2f);
            var smh = prof.Add<ShadowsMidtonesHighlights>(false);
            smh.shadows.Override(new Vector4(1f, 1f, 1f, 0f)); smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f)); smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f));
            var st = prof.Add<SplitToning>(false); st.shadows.Override(Color.grey); st.highlights.Override(Color.grey); st.balance.Override(0f); st.active = false;
            var bl = prof.Add<Bloom>(false); bl.threshold.Override(1.05f); bl.intensity.Override(0.3f); bl.scatter.Override(0.62f);
            var vg = prof.Add<Vignette>(false); vg.intensity.Override(0.24f); vg.smoothness.Override(0.42f);
            var fg = prof.Add<FilmGrain>(false); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0.1f); fg.response.Override(0.8f);
            var cab = prof.Add<ChromaticAberration>(false); cab.intensity.Override(0.035f);
            return prof;
        }

        public static void ConfigureCamera(Camera cam)
        {
            if (cam == null) return;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            var d = cam.GetUniversalAdditionalCameraData();
            if (d != null)
            {
                d.renderPostProcessing = true;
                d.renderShadows = true;
                d.dithering = true;
            }
            GfxQuality.ConfigureCamera(cam);   // v12: anti-aliasing from the settings, depth texture for soft particles
        }
    }
}
