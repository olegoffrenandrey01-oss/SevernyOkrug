// v12 «Графика и карта»: settings «Качество графики» (Низкое / Среднее / Высокое / Ультра) and «Сглаживание»
// (Выкл / FXAA / SMAA / TAA) + the working «Масштаб рендера» (below 100% → AMD FSR 1 upscaling).
// Applied at runtime to the active URP asset (shadows, render scale), the SSAO renderer feature, QualitySettings (LOD,
// anisotropic filtering) and every game camera (anti-aliasing, depth texture for soft particles).
// In the editor every original value is put back when Play Mode ends — the project assets are not changed by playing.
// Reflection probes, street lights and atmosphere read the preset when a raid loads (Current).
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Severny
{
    public static class GfxQuality
    {
        public static readonly string[] Names = { "Низкое", "Среднее", "Высокое", "Ультра" };
        public static readonly string[] AANames = { "Выкл", "FXAA", "SMAA", "TAA" };

        public struct Preset
        {
            public float shadowDistance, lodMul, probeCell, probeShadow;
            public int cascades, shadowRes, aniso, lampLights, lampShadows, probes, probeRes, zoneProbes, fx;
            public bool ssao, bloomHQ, probeHdr;
        }

        static readonly Preset[] P =
        {
            new Preset { shadowDistance = 80f,  cascades = 2, shadowRes = 1024, aniso = 2,  lodMul = 0.75f, ssao = false, bloomHQ = false,
                         lampLights = 6,  lampShadows = 0, probes = 0,  probeRes = 64,  zoneProbes = 0,  probeCell = 80f, probeHdr = false, probeShadow = 0f,  fx = 0 },
            new Preset { shadowDistance = 120f, cascades = 3, shadowRes = 2048, aniso = 4,  lodMul = 0.9f,  ssao = true,  bloomHQ = false,
                         lampLights = 10, lampShadows = 0, probes = 24, probeRes = 64,  zoneProbes = 8,  probeCell = 80f, probeHdr = false, probeShadow = 0f,  fx = 1 },
            new Preset { shadowDistance = 150f, cascades = 4, shadowRes = 2048, aniso = 8,  lodMul = 1f,    ssao = true,  bloomHQ = true,
                         lampLights = 16, lampShadows = 2, probes = 48, probeRes = 128, zoneProbes = 16, probeCell = 64f, probeHdr = false, probeShadow = 40f, fx = 2 },
            new Preset { shadowDistance = 200f, cascades = 4, shadowRes = 4096, aniso = 16, lodMul = 1.25f, ssao = true,  bloomHQ = true,
                         lampLights = 24, lampShadows = 4, probes = 80, probeRes = 128, zoneProbes = 24, probeCell = 52f, probeHdr = true,  probeShadow = 60f, fx = 2 },
        };

        public static int Level => Mathf.Clamp(GameSettings.GraphicsPreset, 0, P.Length - 1);
        public static Preset Current => P[Level];
        public static int AA => Mathf.Clamp(GameSettings.AntiAliasing, 0, 3);

        // ------------------------------------------------------------------ originals (restored when Play Mode ends)
        struct AssetOrig
        {
            public float shadowDistance, renderScale, fsrSharpness;
            public int cascades, shadowRes;
            public UpscalingFilterSelection filter;
            public bool fsrOverride, enclosing;
        }

        static bool _captured, _rtProbes = true;
        static float _lodBias = 1f;
        static AnisotropicFiltering _aniso = AnisotropicFiltering.Enable;
        static readonly Dictionary<UniversalRenderPipelineAsset, AssetOrig> _assets = new Dictionary<UniversalRenderPipelineAsset, AssetOrig>();
        static readonly Dictionary<ScriptableRendererFeature, bool> _features = new Dictionary<ScriptableRendererFeature, bool>();
        static readonly List<Camera> _cams = new List<Camera>();
        static int _appliedLevel = -1;

        static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        static void Capture(UniversalRenderPipelineAsset a)
        {
            if (!_captured)
            {
                _captured = true;
                _lodBias = QualitySettings.lodBias;
                _aniso = QualitySettings.anisotropicFiltering;
                _rtProbes = QualitySettings.realtimeReflectionProbes;
#if UNITY_EDITOR
                Application.quitting -= Restore;
                Application.quitting += Restore;
#endif
            }
            if (a == null || _assets.ContainsKey(a)) return;
            _assets[a] = new AssetOrig
            {
                shadowDistance = a.shadowDistance, cascades = a.shadowCascadeCount, shadowRes = a.mainLightShadowmapResolution,
                renderScale = a.renderScale, filter = a.upscalingFilter, fsrOverride = a.fsrOverrideSharpness, fsrSharpness = a.fsrSharpness,
                enclosing = a.conservativeEnclosingSphere
            };
            foreach (var rd in a.rendererDataList)
            {
                if (rd == null) continue;
                foreach (var f in rd.rendererFeatures)
                    if (f is ScreenSpaceAmbientOcclusion && !_features.ContainsKey(f)) _features[f] = f.isActive;
            }
        }

        /// <summary>Editor: put the URP asset / quality settings back (Play Mode must not change the project).</summary>
        static void Restore()
        {
            foreach (var kv in _assets)
            {
                var a = kv.Key; if (a == null) continue;
                var o = kv.Value;
                a.shadowDistance = o.shadowDistance; a.shadowCascadeCount = o.cascades; a.mainLightShadowmapResolution = o.shadowRes;
                a.renderScale = o.renderScale; a.upscalingFilter = o.filter; a.fsrOverrideSharpness = o.fsrOverride; a.fsrSharpness = o.fsrSharpness;
                a.conservativeEnclosingSphere = o.enclosing;
            }
            foreach (var kv in _features) if (kv.Key != null) kv.Key.SetActive(kv.Value);
            SsaoLook(false);
            _assets.Clear(); _features.Clear(); _cams.Clear();
            if (_captured)
            {
                QualitySettings.lodBias = _lodBias; QualitySettings.anisotropicFiltering = _aniso; QualitySettings.realtimeReflectionProbes = _rtProbes;
                Texture.SetGlobalAnisotropicFilteringLimits(9, 16);
            }
            _captured = false; _appliedLevel = -1;
            Application.quitting -= Restore;
        }

        /// <summary>GameSettings.Apply → preset + render scale + anti-aliasing (cheap, safe to call on every settings change).</summary>
        public static void Apply()
        {
            var q = Current;
            var a = Asset;
            Capture(a);
            if (a != null)
            {
                a.shadowDistance = q.shadowDistance;
                a.shadowCascadeCount = q.cascades;
                a.mainLightShadowmapResolution = q.shadowRes;
                a.conservativeEnclosingSphere = true;
                float rs = Mathf.Clamp(RenderScaleOverride > 0f ? RenderScaleOverride : GameSettings.RenderScale, 0.5f, 1f);
                a.renderScale = rs;
                // below 100%: AMD FSR 1 (edge-adaptive upscale + sharpening) instead of the blurry bilinear stretch
                a.upscalingFilter = rs < 0.99f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
                a.fsrOverrideSharpness = true; a.fsrSharpness = 0.8f;
                foreach (var rd in a.rendererDataList)
                {
                    if (rd == null) continue;
                    foreach (var f in rd.rendererFeatures)
                        if (f is ScreenSpaceAmbientOcclusion && f.isActive != q.ssao) f.SetActive(q.ssao);
                }
            }
            QualitySettings.lodBias = _lodBias * q.lodMul;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            // the street / interior probes are realtime probes rendered once from script — the quality level must allow them
            QualitySettings.realtimeReflectionProbes = q.probes > 0 || _rtProbes;
            Texture.SetGlobalAnisotropicFilteringLimits(q.aniso, q.aniso);
            _cams.RemoveAll(c => c == null);
            foreach (var c in _cams) ConfigureCameraAA(c);
            if (_appliedLevel != Level)
            {
                _appliedLevel = Level;
                EnvironmentSetup.OnQualityChanged();
                if (CityLights.I != null) CityLights.I.OnQualityChanged();
            }
        }

        /// <summary>Every game camera (player, menu, benchmark) — EnvironmentSetup.ConfigureCamera calls this.</summary>
        public static void ConfigureCamera(Camera cam)
        {
            if (cam == null) return;
            if (!_cams.Contains(cam)) _cams.Add(cam);
            ConfigureCameraAA(cam);
        }

        static void ConfigureCameraAA(Camera cam)
        {
            var d = cam.GetUniversalAdditionalCameraData();
            if (d == null) return;
            cam.allowMSAA = false;
            switch (AA)
            {
                case 1: d.antialiasing = AntialiasingMode.FastApproximateAntialiasing; break;
                case 2: d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; d.antialiasingQuality = AntialiasingQuality.High; break;
                case 3:
                    d.antialiasing = AntialiasingMode.TemporalAntiAliasing; d.taaSettings.quality = TemporalAAQuality.High;
                    d.taaSettings.contrastAdaptiveSharpening = Level >= 2 ? 0.4f : 0f;   // v13: TAA softens textures — CAS on «Высокое» / «Ультра»
                    break;
                default: d.antialiasing = AntialiasingMode.None; break;
            }
            d.requiresDepthOption = CameraOverrideOption.On;   // soft particles (smoke, mist, glows) need the depth texture
        }

        /// <summary>v13 screenshot tool: forces the render scale (1 = 100%) while > 0.</summary>
        public static float RenderScaleOverride;

        // ------------------------------------------------------------------ v13: stronger SSAO outside on «ТЦ «Волна»»
        static readonly Dictionary<ScriptableRendererFeature, float[]> _ssaoOrig = new Dictionary<ScriptableRendererFeature, float[]>();
        static readonly string[] SsaoFields = { "Intensity", "Radius", "DirectLightingStrength" };
        static readonly float[] SsaoVolna = { 1.6f, 0.5f, 0.35f };

        static object SsaoSettings(ScriptableRendererFeature f)
        {
            var fi = f.GetType().GetField("m_Settings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return fi != null ? fi.GetValue(f) : null;
        }

        static System.Reflection.FieldInfo SsaoField(object so, string n) =>
            so.GetType().GetField(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>on: SSAO intensity / radius / direct-lighting strength of the «Волна» look (never weaker than the project's);
        /// off: the project's values back. Runtime only (the asset is put back on raid end and when Play Mode ends).</summary>
        public static void SsaoLook(bool on)
        {
            var a = Asset;
            if (a == null) { if (!on) _ssaoOrig.Clear(); return; }
            try
            {
                foreach (var rd in a.rendererDataList)
                {
                    if (rd == null) continue;
                    foreach (var f in rd.rendererFeatures)
                    {
                        if (!(f is ScreenSpaceAmbientOcclusion)) continue;
                        var so = SsaoSettings(f);
                        if (so == null) continue;
                        if (on)
                        {
                            if (!_ssaoOrig.TryGetValue(f, out var o))
                            {
                                o = new float[SsaoFields.Length];
                                for (int i = 0; i < o.Length; i++) { var fi = SsaoField(so, SsaoFields[i]); o[i] = fi != null && fi.FieldType == typeof(float) ? (float)fi.GetValue(so) : -1f; }
                                _ssaoOrig[f] = o;
                            }
                            for (int i = 0; i < o.Length; i++) { var fi = SsaoField(so, SsaoFields[i]); if (fi != null && o[i] >= 0f) fi.SetValue(so, Mathf.Max(o[i], SsaoVolna[i])); }
                        }
                        else if (_ssaoOrig.TryGetValue(f, out var o))
                            for (int i = 0; i < o.Length; i++) { var fi = SsaoField(so, SsaoFields[i]); if (fi != null && o[i] >= 0f) fi.SetValue(so, o[i]); }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Severny] SSAO v13: " + e.Message); }
            if (!on) _ssaoOrig.Clear();
        }

        /// <summary>Reflection probes per pixel need Forward+ (menu Severny > 7 switches the renderer).</summary>
        public static bool ForwardPlus
        {
            get
            {
                var a = Asset;
                if (a == null) return false;
                bool any = false;
                foreach (var rd in a.rendererDataList)
                {
                    if (rd == null) continue;
                    if (!(rd is UniversalRendererData u)) return false;
                    string m = u.renderingMode.ToString();
                    if (m != "ForwardPlus" && m != "DeferredPlus") return false;
                    any = true;
                }
                return any;
            }
        }

        public static bool HasSsao
        {
            get
            {
                var a = Asset;
                if (a == null) return false;
                foreach (var rd in a.rendererDataList)
                    if (rd != null) foreach (var f in rd.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion) return true;
                return false;
            }
        }

        public static string Describe()
        {
            var q = Current; var a = Asset;
            var sb = new StringBuilder();
            sb.Append("Графика v12: «").Append(Names[Level]).Append("», сглаживание ").Append(AANames[AA]);
            sb.Append(", масштаб ").Append(Mathf.RoundToInt(GameSettings.RenderScale * 100f)).Append('%');
            if (a != null && a.renderScale < 0.99f) sb.Append(" (FSR)");
            sb.Append(", тени ").Append(Mathf.RoundToInt(q.shadowDistance)).Append(" м / ").Append(q.cascades).Append(" каск. / ").Append(q.shadowRes);
            sb.Append(", SSAO ").Append(HasSsao ? (q.ssao ? "вкл" : "выкл") : "нет (Severny > 7)");
            sb.Append(", рендер ").Append(ForwardPlus ? "Forward+" : "Forward (Severny > 7)");
            sb.Append("\nНебо: ").Append(GfxSky.Info).Append("; отражения: ").Append(ReflectionGrid.LastInfo).Append("; огни: ").Append(CityLights.LastInfo)
              .Append("; атмосфера: ").Append(AtmosphereFX.LastInfo);
            if (VolnaLook.Active) sb.Append("\nСвет: ").Append(VolnaLook.Info);
            return sb.ToString();
        }
    }
}
