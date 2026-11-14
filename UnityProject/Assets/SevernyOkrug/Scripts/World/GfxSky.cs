// v12: HDRI sky for the Severny city, the procedural polygon, the tutorial and the menu (PolygonKit keeps its own HDRI):
//  * Skybox/Panoramic with 4 CC0 Poly Haven skies (day — heavy overcast, morning — misty low sun, evening — sunset under
//    clouds, night — cloudy sky with moon glow), rotated so the bright spot of the HDRI sits where the sun light comes from;
//  * fog colour = sky colour just above the horizon (measured from the HDRI), ambient hue from the sky;
//  * horizon haze: a fog-coloured transparent band 1 km around the camera blends the sky into the fogged city (all maps);
//  * default reflection = cubemap of the same sky (pre-rotated for the presets).
// Brightness goes through _Tint (a colour, gamma-correct in both colour spaces), _Exposure stays 1.
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public static class GfxSky
    {
        struct Def { public string name; public float sunU; public Color zen, band, gnd; }
        // linear colours measured from the LDR sky textures (what Unity samples): zenith 50–90°, band 0–4° + 10–35°, ground −30…−2°
        static readonly Def[] Defs =
        {
            new Def { name = "Day",     sunU = 0.6104f, zen = new Color(.5868f, .5822f, .5876f), band = new Color(.3143f, .3013f, .2901f), gnd = new Color(.19f, .186f, .1831f) },
            new Def { name = "Morning", sunU = 0.6123f, zen = new Color(.1581f, .1886f, .2627f), band = new Color(.1834f, .189f, .2211f),  gnd = new Color(.1256f, .1274f, .1478f) },
            new Def { name = "Evening", sunU = 0.6455f, zen = new Color(.1394f, .1472f, .2272f), band = new Color(.2767f, .2309f, .2027f), gnd = new Color(.1796f, .1536f, .1589f) },
            new Def { name = "Night",   sunU = 0.6123f, zen = new Color(.2271f, .2604f, .3111f), band = new Color(.2674f, .2784f, .3017f), gnd = new Color(.16f, .1738f, .1968f) },
        };
        const int Day = 0, Morning = 1, Evening = 2, Night = 3;

        static readonly Material[] _mats = new Material[4];
        static readonly bool[] _failed = new bool[4];
        static Material _cur;
        static int _kind;
        static float _base = 1f;

        public static bool Active => _cur != null && RenderSettings.skybox == _cur;
        public static string Info => Active ? "HDRI " + Defs[_kind].name : "процедурное/своё небо";

        static int KindOf(EnvironmentSetup.Preset p) => p == EnvironmentSetup.Preset.Morning ? Morning : p == EnvironmentSetup.Preset.Evening ? Evening : p == EnvironmentSetup.Preset.Night ? Night : Day;
        // sky brightness (linear multiplier) = fog luminance of the v11 presets / measured band luminance
        static float BaseMul(EnvironmentSetup.Preset p) => p == EnvironmentSetup.Preset.Morning ? 1.65f : p == EnvironmentSetup.Preset.Evening ? 0.78f :
                                                          p == EnvironmentSetup.Preset.Night ? 0.018f : p == EnvironmentSetup.Preset.Menu ? 0.36f : 1f;

        static Material Mat(int k)
        {
            if (_mats[k] != null) return _mats[k];
            if (_failed[k]) return null;
            string n = Defs[k].name;
            Material m = null;
            var src = Resources.Load<Material>(GfxRes.Root + "Materials/SEV_SkyMat_" + n);
            if (src != null && src.shader != null && src.shader.isSupported) m = new Material(src);
            else
            {
                var sh = Shader.Find("Skybox/Panoramic");
                var tex = Resources.Load<Texture2D>(GfxRes.Root + "Sky/SEV_Sky_" + n);
                if (sh != null && tex != null)
                {
                    m = new Material(sh);
                    m.SetTexture("_MainTex", tex);
                    SetF(m, "_Mapping", 1f); SetF(m, "_ImageType", 0f); SetF(m, "_MirrorOnBack", 0f); SetF(m, "_Layout", 0f);
                    m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
                }
            }
            if (m == null)
            {
                _failed[k] = true;
                Debug.Log("[Severny] Графика v12: нет неба SEV_Sky_" + n + " (Resources/SevernyGfx, меню Severny > 7) — процедурное небо v11.");
                return null;
            }
            m.name = "SEV_Sky_" + n + " (runtime)";
            m.hideFlags = HideFlags.DontSave;
            SetF(m, "_Exposure", 1f);
            _mats[k] = m;
            return m;
        }

        /// <summary>Base step (EnvironmentSetup.Apply): puts the HDRI sky of the preset into RenderSettings.skybox and returns the
        /// measured colours scaled to the preset brightness (linear). False → no sky assets, the caller keeps the procedural sky.</summary>
        public static bool Begin(EnvironmentSetup.Preset p, out Color zen, out Color band, out Color gnd)
        {
            int k = KindOf(p);
            var m = Mat(k);
            zen = band = gnd = Color.gray;
            if (m == null) { _cur = null; return false; }
            _kind = k; _base = BaseMul(p); _cur = m;
            RenderSettings.skybox = m;
            var d = Defs[k];
            zen = d.zen * _base; band = d.band * _base; gnd = d.gnd * _base;
            zen.a = band.a = gnd.a = 1f;
            return true;
        }

        /// <summary>Night fog = horizon of the night sky (dark blue instead of v11's black), gamma colour.</summary>
        public static Color NightFog(bool winter)
        {
            var c = Defs[Night].band * (BaseMul(EnvironmentSetup.Preset.Night) * (winter ? 1.6f : 1f));
            c = new Color(c.r * 0.8f, c.g * 0.9f, c.b * 1.15f, 1f);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
        }

        /// <summary>Final step after the time-of-day / weather modifiers: brightness + tint, rotation to the sun, reflections, haze.</summary>
        public static void Finish(bool night, bool winter, int weather, float skyMul)
        {
            if (Active)
            {
                var d = Defs[_kind];
                float mul = _base * skyMul;
                Color tint = _kind == Night ? new Color(0.78f, 0.88f, 1.12f) : Color.white;
                if (winter) tint = new Color(tint.r * 0.97f, tint.g, tint.b * 1.04f);
                // Panoramic: colour = tex × _Tint × unity_ColorSpaceDouble (4.59 in Linear, 2 in Gamma); _Exposure (gamma-flagged) stays 1
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    float k = mul / 4.594794f;
                    _cur.SetColor("_Tint", new Color(tint.r * k, tint.g * k, tint.b * k, 1f).gamma);
                }
                else _cur.SetColor("_Tint", new Color(tint.r * mul * 0.5f, tint.g * mul * 0.5f, tint.b * mul * 0.5f, 1f));
                var sun = RenderSettings.sun;
                if (sun != null)
                {
                    // Panoramic maps direction (x,z) to u = 0.5 − atan2(z,x)/2π and turns the sky by +_Rotation around Y
                    Vector3 to = -sun.transform.forward;
                    float phiSun = Mathf.Atan2(to.z, to.x), phiB = (0.5f - d.sunU) * 2f * Mathf.PI;
                    _cur.SetFloat("_Rotation", Mathf.Repeat((phiSun - phiB) * Mathf.Rad2Deg, 360f));
                }
                var cube = Resources.Load<Cubemap>(GfxRes.Root + "Sky/SEV_SkyR_" + d.name);
                if (cube != null)
                {
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = cube;
                    RenderSettings.reflectionIntensity = Mathf.Clamp(mul * 0.75f, 0.01f, 1f);
                }
            }
            // horizon haze (every map, PolygonKit too): thicker in rain / fog / winter
            float strength = 0.85f, e0 = 0f, e1 = 11f;
            if (weather == RaidOptions.WeatherPrecip) { strength = 0.95f; e1 = 20f; }
            else if (weather == RaidOptions.WeatherFog) { strength = 1f; e0 = 4f; e1 = 36f; }
            else if (weather == RaidOptions.WeatherClear || weather == RaidOptions.WeatherAfterRain) { strength = 0.8f; e1 = 9f; }
            if (winter) e1 += 4f;
            if (night) strength *= 0.95f;
            if (ShotFlags.NoHaze) GfxHaze.Hide();   // screenshot tool: v12 without the horizon haze
            else GfxHaze.Set(RenderSettings.fogColor, strength, e0, e1);
        }

        static void SetF(Material m, string n, float v) { if (m.HasProperty(n)) m.SetFloat(n, v); }
    }

    /// <summary>Fog-coloured band (−25°…+50°) 1 km around whichever camera renders (game, menu, reflection probes):
    /// transparent, drawn before every other transparent object, fully fogged at that distance → the sky melts into the fog.</summary>
    public static class GfxHaze
    {
        const float Radius = 1000f, Bottom = -25f, Top = 50f;
        static GameObject _go;
        static Material _mat;
        static Texture2D _tex;
        static Mesh _mesh;
        static bool _hooked, _failed;
        static readonly Color32[] _px = new Color32[2 * 128];

        public static void Set(Color fogGamma, float strength, float e0, float e1)
        {
            GfxSkyline.Hide();          // v13.1: the distant skyline belongs to «Волна» only — VolnaLook shows it again right after this call
            if (_failed) return;
            if (_go == null && !Create()) return;
            for (int y = 0; y < 128; y++)
            {
                float e = Mathf.Lerp(Bottom, Top, y / 127f);
                float a = e <= e0 ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e0, e1, e));
                a *= strength;
                var c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                _px[y * 2] = c; _px[y * 2 + 1] = c;
            }
            _tex.SetPixels32(_px); _tex.Apply(false, false);
            fogGamma.a = 1f;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", fogGamma); else _mat.color = fogGamma;
            _go.SetActive(true);
        }

        public static void Hide() { GfxSkyline.Hide(); if (_go != null) _go.SetActive(false); }
        public static bool Visible { get => _go != null && _go.activeSelf; set { GfxSkyline.Visible = value; if (_go != null) _go.SetActive(value); } }

        static bool Create()
        {
            var baseMat = GameAssets.Unlit(Color.white, true);
            if (baseMat == null || baseMat.shader == null) { _failed = true; return false; }
            _mat = new Material(baseMat) { name = "SEV_HorizonHaze", hideFlags = HideFlags.DontSave };
            _tex = new Texture2D(2, 128, TextureFormat.RGBA32, false, true) { name = "SEV_HazeRamp", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _tex);
            _mat.mainTexture = _tex;
            if (_mat.HasProperty("_Cull")) _mat.SetFloat("_Cull", 0f);
            _mat.renderQueue = 2501;      // first transparent: smoke, rain, glass are drawn over it
            _mesh = BuildMesh();
            _go = new GameObject("SEV_HorizonHaze");
            Object.DontDestroyOnLoad(_go);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            if (!_hooked) { RenderPipelineManager.beginCameraRendering += OnCamera; _hooked = true; }
            return true;
        }

        static void OnCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_go == null || cam == null) return;
            if (cam.cameraType == CameraType.Preview) return;
            _go.transform.position = cam.transform.position;
            // short far plane (reflection probes 220 m): pull the band inside it, it is fully fogged there anyway
            float s = cam.farClipPlane < Radius * 1.05f ? Mathf.Max(0.05f, cam.farClipPlane * 0.9f / Radius) : 1f;
            if (Mathf.Abs(_go.transform.localScale.x - s) > 1e-4f) _go.transform.localScale = new Vector3(s, s, s);
        }

        static Mesh BuildMesh()
        {
            float[] rings = { -25f, -8f, -2f, 0f, 2f, 4f, 7f, 10f, 14f, 19f, 25f, 32f, 40f, 50f };
            const int seg = 48;
            var v = new Vector3[rings.Length * (seg + 1)];
            var uv = new Vector2[v.Length];
            var idx = new int[(rings.Length - 1) * seg * 6];
            int n = 0;
            for (int r = 0; r < rings.Length; r++)
            {
                float e = rings[r] * Mathf.Deg2Rad, ce = Mathf.Cos(e), se = Mathf.Sin(e);
                float vv = (rings[r] - Bottom) / (Top - Bottom);
                for (int s = 0; s <= seg; s++)
                {
                    float t = s * Mathf.PI * 2f / seg;
                    v[n] = new Vector3(Mathf.Cos(t) * ce, se, Mathf.Sin(t) * ce) * Radius;
                    uv[n] = new Vector2(0.5f, vv);
                    n++;
                }
            }
            int k = 0;
            for (int r = 0; r < rings.Length - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    idx[k++] = a; idx[k++] = c; idx[k++] = b;
                    idx[k++] = b; idx[k++] = c; idx[k++] = d;
                }
            var m = new Mesh { name = "SEV_HazeBand", hideFlags = HideFlags.DontSave };
            m.vertices = v; m.uv = uv; m.triangles = idx;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 2.2f);
            return m;
        }
    }
}
