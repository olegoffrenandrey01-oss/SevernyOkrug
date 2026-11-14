// v13.0 «ТЦ «Волна»»: screenshot points (anchors kind "shot" in pk_map.json, MapAnchors.Shots).
// Start: menu Severny > Volna: Screenshots (v13), F7 in the main menu, or the command line  Severny.exe -severnyShots [all]
// For every variant the map is loaded like a raid (no bots, seed 1337): v13 morning / day / evening «после дождя» and
// «до» = morning, clear, the v12 look; "all" adds the v12 checks on the R shots (no reflection probes, no horizon haze,
// the v11.1 grade, rain with and without wet surfaces). Per shot: <shot>__<variant>.png 1920×1080, _sky.png (sky = #FF00FF)
// and _dist.png (96×54 raycasts, value × 2 = metres, 255 = no hit) for look_metrics.py, a 4×4 contact sheet per variant
// and shots_summary.txt (triangles / batches / SetPass / FPS, light balance, a subset of the look metrics of prompt 3a).
// Output: <persistentDataPath>/shots/<yyyyMMdd_HHmmss>/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Severny
{
    public static class ShotTool
    {
        struct Variant
        {
            public string id, title; public int tod, weather; public bool v12, noProbes, noHaze, noWet, v11Grade, rOnly;
        }

        public static bool Running { get; private set; }
        public static string LastFolder { get; private set; }
        const int W = 1920, H = 1080, MW = 960, MH = 540, DW = 96, DH = 54, TW = 480, TH = 270;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        static List<Variant> Variants(bool all)
        {
            var v = new List<Variant>
            {
                new Variant { id = "v13_morning", title = "v13 утро, после дождя", tod = RaidOptions.TodMorning, weather = RaidOptions.WeatherAfterRain },
                new Variant { id = "v13_day", title = "v13 день, после дождя", tod = RaidOptions.TodDay, weather = RaidOptions.WeatherAfterRain },
                new Variant { id = "v13_evening", title = "v13 вечер, после дождя", tod = RaidOptions.TodEvening, weather = RaidOptions.WeatherAfterRain },
                new Variant { id = "v12_morning", title = "до: v12 утро, ясно", tod = RaidOptions.TodMorning, weather = RaidOptions.WeatherClear, v12 = true },
            };
            if (all)
            {
                v.Add(new Variant { id = "v12_noprobes", title = "v12 утро без проб отражений", tod = 0, weather = RaidOptions.WeatherClear, v12 = true, noProbes = true, rOnly = true });
                v.Add(new Variant { id = "v12_nohaze", title = "v12 утро без дымки горизонта", tod = 0, weather = RaidOptions.WeatherClear, v12 = true, noHaze = true, rOnly = true });
                v.Add(new Variant { id = "v12_v11grade", title = "v12 утро с цветокоррекцией v11.1", tod = 0, weather = RaidOptions.WeatherClear, v12 = true, v11Grade = true, rOnly = true });
                v.Add(new Variant { id = "v12_rain", title = "v12 утро, дождь", tod = 0, weather = RaidOptions.WeatherPrecip, v12 = true, rOnly = true });
                v.Add(new Variant { id = "v12_rain_nowet", title = "v12 утро, дождь без мокрых поверхностей", tod = 0, weather = RaidOptions.WeatherPrecip, v12 = true, noWet = true, rOnly = true });
                v.Add(new Variant { id = "v13_morning_clear", title = "v13 утро, ясно", tod = 0, weather = RaidOptions.WeatherClear, rOnly = true });
                v.Add(new Variant { id = "v13_morning_rain", title = "v13 утро, дождь", tod = 0, weather = RaidOptions.WeatherPrecip, rOnly = true });
                v.Add(new Variant { id = "v13_morning_fog", title = "v13 утро, туман", tod = 0, weather = RaidOptions.WeatherFog, rOnly = true });
            }
            return v;
        }

        static void SetFlags(Variant v)
        {
            ShotFlags.Reset();
            ShotFlags.ForceV12 = v.v12; ShotFlags.NoProbes = v.noProbes; ShotFlags.NoHaze = v.noHaze; ShotFlags.NoWet = v.noWet; ShotFlags.V11Grade = v.v11Grade;
        }

        static bool IsR(string n) => n.StartsWith("shot_R", StringComparison.Ordinal) && n.Length > 6 && char.IsDigit(n[6]);

        public static IEnumerator Run(GameBootstrap gb, bool all, bool quit)
        {
            if (Running) yield break;
            if (!PolygonKit.Available) { Debug.LogError("[Severny] Фототочки: нет карты «ТЦ «Волна»» (Resources/PolygonKit)."); Finish(gb, quit, "Нет карты «ТЦ «Волна»»."); yield break; }
            Running = true;
            string dir = Path.Combine(Application.persistentDataPath, "shots", DateTime.Now.ToString("yyyyMMdd_HHmmss", CI));
            Directory.CreateDirectory(dir);
            LastFolder = dir;
            var sum = new StringBuilder(16384);
            sum.Append("Северный Округ v13.0 — фототочки «ТЦ «Волна»»\n").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CI))
               .Append(" · Unity ").Append(Application.unityVersion).Append(" · ").Append(SystemInfo.graphicsDeviceName)
               .Append(" · ").Append(SystemInfo.processorType).Append("\n");
            int vsync = QualitySettings.vSyncCount, fps = Application.targetFrameRate;
            float vol = AudioListener.volume;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; AudioListener.volume = 0f;
            GfxQuality.RenderScaleOverride = 1f; GfxQuality.Apply();
            sum.Append(GfxQuality.Describe().Replace("\n", "\n  ")).Append("\n\n");
            int done = 0, failed = 0;
            var variants = Variants(all);
            for (int vi = 0; vi < variants.Count; vi++)
            {
                var v = variants[vi];
                SetFlags(v);
                yield return gb.RaidForShots(v.tod, v.weather);
                if (!gb.InRaid) { failed++; sum.Append("== ").Append(v.title).Append(": карта не загрузилась (см. Console)\n\n"); ShotFlags.Reset(); break; }
                var ctx = new Ctx(gb.transform, dir, v);
                yield return ctx.ShootAll(vi, variants.Count, sum);
                done += ctx.Count;
                ctx.Dispose();
                yield return gb.EndRaidForShots();
            }
            ShotFlags.Reset();
            GfxQuality.RenderScaleOverride = 0f; GfxQuality.Apply();
            QualitySettings.vSyncCount = vsync; Application.targetFrameRate = fps; AudioListener.volume = vol;
            sum.Append("Кадров: ").Append(done).Append(failed > 0 ? " (с ошибками, см. Console)" : "").Append('\n');
            sum.Append("\nЧто прислать: shots_summary.txt, sheet_*.jpg и shot_R*__v13_morning*.png (+ _sky/_dist) — или всю папку архивом.\n");
            sum.Append("Метрики лука целиком: python3 look_metrics.py shot_R*__v13_morning.png --out look_report.md\n");
            try { File.WriteAllText(Path.Combine(dir, "shots_summary.txt"), sum.ToString(), new UTF8Encoding(false)); }
            catch (Exception e) { Debug.LogError("[Severny] Фототочки: сводка не записана: " + e.Message); }
            Debug.Log("[Severny] Фототочки v13: " + done + " кадров → " + dir);
            Running = false;
            Finish(gb, quit, "Готово: " + done + " кадров.\n" + dir);
        }

        static void Finish(GameBootstrap gb, bool quit, string msg)
        {
            if (quit)
            {
#if UNITY_EDITOR
                if (LastFolder != null) UnityEditor.EditorUtility.RevealInFinder(LastFolder);
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }
            if (UIManager.I != null) { UIManager.I.ShowMainMenu(); UIManager.I.ShowMessage("Фототочки", msg); }
        }

        // ------------------------------------------------------------------ one variant
        class Ctx
        {
            readonly Variant _v;
            readonly string _dir;
            readonly Camera _cam;
            readonly UniversalAdditionalCameraData _data;
            readonly RenderTexture _rt, _rtM, _rtT;
            readonly Texture2D _tex, _texM, _sheet, _dist;
            readonly Color32[] _distPx = new Color32[DW * DH];
            Color32[] _px;
            ProfilerRecorder _batches, _setpass, _tris;
            public int Count;

            public Ctx(Transform parent, string dir, Variant v)
            {
                _v = v; _dir = dir;
                if (PlayerController.Local != null) PlayerController.Local.gameObject.SetActive(false);
                var go = new GameObject("ShotCamera");
                go.transform.SetParent(parent, false);
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
                _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 1500f; _cam.fieldOfView = GameSettings.Fov;
                go.AddComponent<AudioListener>();
                EnvironmentSetup.ConfigureCamera(_cam);
                _data = _cam.GetUniversalAdditionalCameraData();
                Ballistics.Listener = go.transform;
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "SEV_Shot", antiAliasing = 1 };
                _rtM = new RenderTexture(MW, MH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "SEV_ShotHalf", antiAliasing = 1 };
                _rtT = new RenderTexture(TW, TH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "SEV_ShotTile" };
                _tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
                _texM = new Texture2D(MW, MH, TextureFormat.RGB24, false, false);
                _sheet = new Texture2D(TW * 4, TH * 4, TextureFormat.RGB24, false, false);
                var black = new Color32[TW * 4 * TH * 4]; _sheet.SetPixels32(black); _sheet.Apply(false);
                _dist = new Texture2D(DW, DH, TextureFormat.RGB24, false, true);
                _cam.targetTexture = _rt;
                _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            }

            public void Dispose()
            {
                _batches.Dispose(); _setpass.Dispose(); _tris.Dispose();
                if (_cam != null) { _cam.targetTexture = null; UnityEngine.Object.Destroy(_cam.gameObject); }
                foreach (var r in new[] { _rt, _rtM, _rtT }) if (r != null) { r.Release(); UnityEngine.Object.Destroy(r); }
                foreach (var t in new[] { _tex, _texM, _sheet, _dist }) if (t != null) UnityEngine.Object.Destroy(t);
            }

            bool Place(MapAnchors.Shot s)
            {
                // the anchor is the ground under the camera: find the floor below (y + 1.2 m down to 8 m), eye at 1.65 m
                var p = s.pos;
                if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
                _cam.transform.SetPositionAndRotation(p + Vector3.up * 1.65f, Quaternion.Euler(-s.pitch, s.yaw, 0f));
                _cam.fieldOfView = s.fov > 1f ? s.fov : GameSettings.Fov;
                return true;
            }

            public IEnumerator ShootAll(int vi, int vn, StringBuilder sum)
            {
                var ui = UIManager.I;
                sum.Append("== ").Append(_v.title).Append(" (").Append(_v.id).Append(")\n");
                sum.Append("  ").Append(VolnaLook.Active ? VolnaLook.Info : "свет v12: " + GfxSky.Info).Append('\n');
                sum.Append("  ").Append(VolnaLook.BalanceLine()).Append('\n');
                sum.Append("  атмосфера: ").Append(AtmosphereFX.LastInfo).Append("; отражения: ").Append(ReflectionGrid.LastInfo).Append('\n');
                var shots = new List<MapAnchors.Shot>();
                foreach (var s in MapAnchors.Shots) if (!_v.rOnly || IsR(s.name)) shots.Add(s);
                if (shots.Count == 0) { sum.Append("  нет фототочек (якоря shot_* в pk_map.json)\n\n"); yield break; }
                for (int i = 0; i < shots.Count; i++)
                {
                    var s = shots[i];
                    if (ui != null) ui.SetLoading((vi + i / (float)shots.Count) / vn, "Фототочки: " + _v.title + " — " + (i + 1) + "/" + shots.Count + " " + s.title);
                    Place(s);
                    InteriorZones.SnapNext = true;
                    if (_data != null) _data.resetHistory = true;
                    // converge: TAA history, LOD cross-fades (0.5 s), interior exposure
                    float t0 = Time.realtimeSinceStartup, ft = 0f; int frames = 0, measured = 0;
                    long bat = 0, sp = 0, tri = 0;
                    while (frames < 16 || Time.realtimeSinceStartup - t0 < 0.9f)
                    {
                        yield return null;
                        frames++;
                        if (frames > 6) { ft += Time.unscaledDeltaTime; measured++; bat = Math.Max(bat, _batches.LastValue); sp = Math.Max(sp, _setpass.LastValue); tri = Math.Max(tri, _tris.LastValue); }
                        if (frames > 600) break;
                    }
                    yield return new WaitForEndOfFrame();
                    string baseName = s.name + "__" + _v.id;
                    Read(_rt, _tex, W, H);
                    Save(_tex.EncodeToPNG(), baseName + ".png");
                    Tile(i);
                    // half-size copy for the metrics
                    Graphics.Blit(_rt, _rtM);
                    Read(_rtM, _texM, MW, MH);
                    _px = _texM.GetPixels32();
                    var beauty = (Color32[])_px.Clone();
                    // sky mask: no fog / haze / post, clear to magenta
                    yield return Mask();
                    Read(_rtM, _texM, MW, MH);
                    Save(_texM.EncodeToPNG(), baseName + "_sky.png");
                    var mask = _texM.GetPixels32();
                    Distances();
                    Save(_dist.EncodeToPNG(), baseName + "_dist.png");
                    float fpsAvg = measured > 0 && ft > 0f ? measured / ft : 0f;
                    var met = Metrics(beauty, mask);
                    var p = _cam.transform.position;
                    sum.Append("  ").Append(s.name).Append(" «").Append(s.title).Append("» (").Append(p.x.ToString("0.0", CI)).Append(", ").Append(p.y.ToString("0.0", CI)).Append(", ")
                       .Append(p.z.ToString("0.0", CI)).Append(") yaw ").Append(s.yaw.ToString("0", CI)).Append(" pitch ").Append(s.pitch.ToString("0", CI))
                       .Append(" fov ").Append(_cam.fieldOfView.ToString("0", CI)).Append(": треуг. ").Append((tri / 1000).ToString(CI)).Append(" тыс., батчей ").Append(bat)
                       .Append(", SetPass ").Append(sp).Append(", FPS ").Append(fpsAvg.ToString("0", CI)).Append("\n    ").Append(met).Append('\n');
                    Count++;
                }
                SaveSheet();
                sum.Append('\n');
            }

            IEnumerator Mask()
            {
                bool fog = RenderSettings.fog, haze = GfxHaze.Visible, post = _data != null && _data.renderPostProcessing;
                var aa = _data != null ? _data.antialiasing : AntialiasingMode.None;
                var clear = _cam.clearFlags; var bg = _cam.backgroundColor;
                RenderSettings.fog = false; GfxHaze.Visible = false;
                if (_data != null) { _data.renderPostProcessing = false; _data.antialiasing = AntialiasingMode.None; }
                _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
                _cam.targetTexture = _rtM;
                yield return null; yield return null;
                yield return new WaitForEndOfFrame();
                _cam.targetTexture = _rt;
                RenderSettings.fog = fog; GfxHaze.Visible = haze;
                if (_data != null) { _data.renderPostProcessing = post; _data.antialiasing = aa; }
                _cam.clearFlags = clear; _cam.backgroundColor = bg;
            }

            void Distances()
            {
                for (int y = 0; y < DH; y++)
                    for (int x = 0; x < DW; x++)
                    {
                        var ray = _cam.ViewportPointToRay(new Vector3((x + 0.5f) / DW, 1f - (y + 0.5f) / DH, 0f));
                        byte v = 255;
                        if (Physics.Raycast(ray, out var hit, 508f, ~0, QueryTriggerInteraction.Ignore)) v = (byte)Mathf.Clamp(Mathf.RoundToInt(hit.distance * 0.5f), 0, 254);
                        _distPx[(DH - 1 - y) * DW + x] = new Color32(v, v, v, 255);   // texture row 0 = bottom of the PNG
                    }
                _dist.SetPixels32(_distPx); _dist.Apply(false);
            }

            static void Read(RenderTexture rt, Texture2D tex, int w, int h)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = prev;
            }

            void Tile(int i)
            {
                if (i >= 16) return;
                Graphics.Blit(_rt, _rtM);
                Graphics.Blit(_rtM, _rtT);
                var prev = RenderTexture.active;
                RenderTexture.active = _rtT;
                _sheet.ReadPixels(new Rect(0, 0, TW, TH), (i % 4) * TW, (3 - i / 4) * TH, false);
                RenderTexture.active = prev;
            }

            void SaveSheet()
            {
                _sheet.Apply(false);
                Save(_sheet.EncodeToJPG(88), "sheet_" + _v.id + ".jpg");
            }

            void Save(byte[] data, string file)
            {
                try { File.WriteAllBytes(Path.Combine(_dir, file), data); }
                catch (Exception e) { Debug.LogError("[Severny] Фототочки: " + file + ": " + e.Message); }
            }

            // -------------------------------------------------------------- metrics subset (look_metrics.py is the reference)
            static float[] _lin;
            static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

            static string Metrics(Color32[] px, Color32[] mask)
            {
                if (_lin == null) { _lin = new float[256]; for (int i = 0; i < 256; i++) { float c = i / 255f; _lin[i] = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f); } }
                var hist = new int[1001];
                int n = px.Length, sky = 0, ns = 0, shadow = 0, dark = 0, hi = 0, shN = 0;
                double sat = 0, bsh = 0;
                var gY = new List<float>(64000);
                int gx0 = (int)(0.08f * MW), gx1 = (int)(0.97f * MW), gy0 = (int)(0.72f * MH), gy1 = (int)(0.97f * MH);
                for (int i = 0; i < n; i++)
                {
                    var m = mask[i];
                    var c = px[i];
                    float r = _lin[c.r], g = _lin[c.g], b = _lin[c.b];
                    float Y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float X = 0.4124f * r + 0.3576f * g + 0.1805f * b, Z = 0.0193f * r + 0.1192f * g + 0.9505f * b;
                    float fy = F(Y), L = 116f * fy - 16f, bb = 200f * (fy - F(Z / 1.08883f));
                    if (L > 85f) hi++;
                    if (m.r > 200 && m.g < 60 && m.b > 200) { sky++; continue; }
                    ns++;
                    hist[Mathf.Clamp(Mathf.RoundToInt(L * 10f), 0, 1000)]++;
                    if (L < 25f) shadow++;
                    if (L < 12f) dark++;
                    if (L < 25f && L > 3f) { bsh += bb; shN++; }
                    int mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b));
                    sat += mx > 0 ? (mx - mn) / (double)mx : 0.0;
                    int y = (MH - 1) - i / MW, x = i % MW;       // GetPixels32: row 0 = bottom
                    if (x >= gx0 && x < gx1 && y >= gy0 && y < gy1) gY.Add(Y);
                    _ = X;
                }
                float P(float q)
                {
                    int target = Mathf.RoundToInt(q * ns), acc = 0;
                    for (int k = 0; k <= 1000; k++) { acc += hist[k]; if (acc >= target) return k / 10f; }
                    return 100f;
                }
                string ground = "-";
                if (gY.Count > 500)
                {
                    gY.Sort();
                    float p20 = gY[(int)(gY.Count * 0.2f)], p80 = gY[(int)(gY.Count * 0.8f)];
                    ground = (p80 / Mathf.Max(p20, 1e-4f)).ToString("0.0", CI);
                }
                if (ns == 0) ns = 1;
                return "небо " + (sky / (float)n).ToString("0.00", CI) + ", L5/L50/L95 " + P(0.05f).ToString("0", CI) + "/" + P(0.5f).ToString("0", CI) + "/" + P(0.95f).ToString("0", CI) +
                       ", тень " + (shadow / (float)ns).ToString("0.00", CI) + ", глуб. тень " + (dark / (float)ns).ToString("0.00", CI) + ", пересвет " + (hi / (float)n).ToString("0.000", CI) +
                       ", насыщ. " + (sat / ns).ToString("0.000", CI) + ", b* теней " + (shN > 0 ? (bsh / shN).ToString("0.0", CI) : "-") + ", земля свет/тень " + ground +
                       "  [коридор: L50 14–28, тень 0,45–0,80, насыщ. 0,08–0,18, земля ≥ 2]";
            }
        }
    }
}
