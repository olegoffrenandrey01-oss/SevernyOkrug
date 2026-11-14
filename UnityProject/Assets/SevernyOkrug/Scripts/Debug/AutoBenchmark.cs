// Repeatable performance run: a camera walks a fixed street-level route (NavMesh paths between seeded points),
// records frame times, GPU time (FrameTimingManager), batches/SetPass/triangles and memory, then writes
//   <persistentDataPath>/benchmarks/bench_<map>_<date>.csv  (per second)  and  .txt  (summary).
// Start: main menu > НАСТРОЙКИ > «Бенчмарк», or command line:  Severny.exe -severnyBenchmark severny
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    public class AutoBenchmark : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string LastSummary { get; private set; }
        public static string LastFile { get; private set; }

        public float speed = 5.5f, eyeHeight = 1.65f, warmup = 4f, maxDuration = 120f;

        Bounds _b; MapDef _map; bool _quit;
        readonly List<Vector3> _route = new List<Vector3>();
        readonly List<float> _cum = new List<float>();
        float _length;
        Camera _cam;
        ProfilerRecorder _batches, _setpass, _tris, _verts, _sysMem, _gfxMem;
        readonly List<float> _frames = new List<float>(16384), _gpu = new List<float>(16384);
        long _maxBatches, _maxSetpass, _maxTris; double _sumBatches, _sumSetpass, _sumTris; int _samples;
        readonly FrameTiming[] _ft = new FrameTiming[1];
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public void Begin(Bounds b, MapDef map, bool quitAfter)
        {
            _b = b; _map = map; _quit = quitAfter;
            StartCoroutine(Run());
        }

        void BuildRoute()
        {
            var rng = new System.Random(20240611);
            var pts = new List<Vector3>();
            for (int i = 0; i < 400 && pts.Count < 14; i++)
            {
                var q = new Vector3(Mathf.Lerp(_b.min.x + 10f, _b.max.x - 10f, (float)rng.NextDouble()), _b.max.y,
                                    Mathf.Lerp(_b.min.z + 10f, _b.max.z - 10f, (float)rng.NextDouble()));
                if (Physics.Raycast(q + Vector3.up * 5f, Vector3.down, out var hit, _b.size.y + 60f, ~0, QueryTriggerInteraction.Ignore)) q = hit.point;
                if (!NavMesh.SamplePosition(q, out var nh, 3f, NavMesh.AllAreas)) continue;
                bool near = false; foreach (var p in pts) if ((p - nh.position).sqrMagnitude < 40f * 40f) { near = true; break; }
                if (!near) pts.Add(nh.position);
            }
            if (pts.Count < 2) return;
            // nearest-neighbour order starting at the first point
            var order = new List<Vector3> { pts[0] }; pts.RemoveAt(0);
            while (pts.Count > 0)
            {
                int bi = 0; float bd = float.MaxValue; var last = order[order.Count - 1];
                for (int i = 0; i < pts.Count; i++) { float d = (pts[i] - last).sqrMagnitude; if (d < bd) { bd = d; bi = i; } }
                order.Add(pts[bi]); pts.RemoveAt(bi);
            }
            var path = new NavMeshPath();
            _route.Add(order[0]);
            for (int i = 1; i < order.Count; i++)
            {
                if (!NavMesh.CalculatePath(_route[_route.Count - 1], order[i], NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                for (int c = 1; c < path.corners.Length; c++) _route.Add(path.corners[c]);
            }
            _cum.Add(0f);
            for (int i = 1; i < _route.Count; i++) _cum.Add(_cum[i - 1] + Vector3.Distance(_route[i - 1], _route[i]));
            _length = _cum[_cum.Count - 1];
        }

        Vector3 At(float s)
        {
            if (_route.Count == 0) return _b.center;
            s = Mathf.Clamp(s, 0f, _length);
            int i = _cum.BinarySearch(s); if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, _route.Count - 1);
            float seg = _cum[i] - _cum[i - 1];
            return Vector3.Lerp(_route[i - 1], _route[i], seg > 1e-4f ? (s - _cum[i - 1]) / seg : 0f);
        }

        IEnumerator Run()
        {
            Running = true;
            BuildRoute();
            bool orbit = _length < 60f;
            float duration = orbit ? 45f : Mathf.Min(maxDuration, _length / speed);

            if (PlayerController.Local != null) PlayerController.Local.gameObject.SetActive(false);
            var go = new GameObject("BenchmarkCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            _cam = go.AddComponent<Camera>();
            _cam.fieldOfView = GameSettings.Fov; _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 1200f;
            go.AddComponent<AudioListener>();
            EnvironmentSetup.ConfigureCamera(_cam);
            Ballistics.Listener = go.transform;

            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            _gfxMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");

            var csv = new StringBuilder("t,fps,frame_ms_max,gpu_ms,batches,setpass,tris,verts,sys_mem_mb,gfx_mem_mb,x,y,z\n");
            float t = -warmup, secT = 0f; int secFrames = 0; float secMax = 0f, secGpu = 0f; int secGpuN = 0;
            Quaternion look = Quaternion.identity; bool first = true;
            while (t < duration)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                float s = Mathf.Max(0f, t) * speed;
                Vector3 pos, ahead;
                if (orbit)
                {
                    float a = Mathf.Max(0f, t) / duration * Mathf.PI * 2f, r = Mathf.Max(_b.extents.x, _b.extents.z) * 0.6f;
                    pos = _b.center + new Vector3(Mathf.Cos(a) * r, _b.extents.y + 15f, Mathf.Sin(a) * r);
                    ahead = new Vector3(_b.center.x, _b.min.y, _b.center.z);
                }
                else { pos = At(s) + Vector3.up * eyeHeight; ahead = At(s + 7f) + Vector3.up * eyeHeight; }
                var dir = ahead - pos; if (dir.sqrMagnitude < 0.01f) dir = go.transform.forward;
                var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
                look = first ? target : Quaternion.Slerp(look, target, 1f - Mathf.Exp(-dt * 3f)); first = false;
                go.transform.SetPositionAndRotation(pos, look);
                Hud.Progress("Бенчмарк " + Mathf.Max(0f, t).ToString("F0") + " / " + duration.ToString("F0") + " c", Mathf.Clamp01(t / duration));

                if (t > 0f)
                {
                    float ms = dt * 1000f; _frames.Add(ms);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, _ft) > 0 && _ft[0].gpuFrameTime > 0.0)
                    { float g = (float)_ft[0].gpuFrameTime; _gpu.Add(g); secGpu += g; secGpuN++; }
                    long bt = _batches.Valid ? _batches.LastValue : 0, sp = _setpass.Valid ? _setpass.LastValue : 0, tr = _tris.Valid ? _tris.LastValue : 0;
                    _maxBatches = Math.Max(_maxBatches, bt); _maxSetpass = Math.Max(_maxSetpass, sp); _maxTris = Math.Max(_maxTris, tr);
                    _sumBatches += bt; _sumSetpass += sp; _sumTris += tr; _samples++;
                    secFrames++; secMax = Mathf.Max(secMax, ms); secT += dt;
                    if (secT >= 1f)
                    {
                        csv.Append(t.ToString("F1", CI)).Append(',').Append((secFrames / secT).ToString("F1", CI)).Append(',').Append(secMax.ToString("F2", CI)).Append(',')
                           .Append(secGpuN > 0 ? (secGpu / secGpuN).ToString("F2", CI) : "").Append(',').Append(bt).Append(',').Append(sp).Append(',').Append(tr).Append(',')
                           .Append(_verts.Valid ? _verts.LastValue : 0).Append(',').Append(Mb(_sysMem)).Append(',').Append(Mb(_gfxMem)).Append(',')
                           .Append(pos.x.ToString("F1", CI)).Append(',').Append(pos.y.ToString("F1", CI)).Append(',').Append(pos.z.ToString("F1", CI)).Append('\n');
                        secT = 0f; secFrames = 0; secMax = 0f; secGpu = 0f; secGpuN = 0;
                    }
                }
                yield return null;
            }
            Finish(csv, duration, orbit);
        }

        static string Mb(ProfilerRecorder r) => r.Valid ? (r.LastValue / (1024.0 * 1024.0)).ToString("F0", CI) : "";

        static float Pct(List<float> sorted, float p) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];

        void Finish(StringBuilder csv, float duration, bool orbit)
        {
            var sorted = new List<float>(_frames); sorted.Sort();
            float total = 0f; foreach (var f in _frames) total += f;
            float avgFps = _frames.Count > 0 ? 1000f * _frames.Count / total : 0f;
            // "1% low" = average FPS of the slowest 1% frames
            float Low(float frac)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(sorted.Count * frac)); float s = 0f;
                for (int i = sorted.Count - n; i < sorted.Count; i++) s += sorted[i];
                return sorted.Count == 0 ? 0f : 1000f * n / s;
            }
            var gs = new List<float>(_gpu); gs.Sort();
            float gAvg = 0f; foreach (var g in _gpu) gAvg += g; gAvg = _gpu.Count > 0 ? gAvg / _gpu.Count : 0f;
            float low1 = Low(0.01f), low01 = Low(0.001f);
            string verdict = avgFps >= 60f && low1 >= 45f ? "OK: цель 60 FPS выдерживается" : avgFps >= 60f ? "СРЕДНИЙ FPS ≥ 60, но есть просадки (1% low < 45)" : "НИЖЕ ЦЕЛИ 60 FPS";
            string bound = _gpu.Count > 10 ? (gAvg > 0.85f * (total / Mathf.Max(1, _frames.Count)) ? "упор в GPU" : "упор в CPU/скрипты (GPU не загружен полностью)") : "GPU-время недоступно (включите Frame Timing Stats)";

            var sb = new StringBuilder();
            sb.AppendLine("SEVERNY OKRUG — benchmark " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CI));
            sb.AppendLine("Карта: " + (_map != null ? _map.title : "?") + (orbit ? " (облёт: NavMesh маршрут слишком короткий)" : " | маршрут " + _length.ToString("F0", CI) + " м"));
            sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsMemorySize + " MB, " + SystemInfo.graphicsDeviceType + ")");
            sb.AppendLine("CPU: " + SystemInfo.processorType + " (" + SystemInfo.processorCount + " потоков), RAM " + SystemInfo.systemMemorySize + " MB");
            sb.AppendLine("Экран: " + Screen.width + "x" + Screen.height + ", render scale " + GameSettings.RenderScale.ToString("F2", CI) + ", quality «" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "», VSync выкл. на время теста");
            sb.AppendLine(GfxQuality.Describe());   // v12: graphics preset, AA, shadows, SSAO, Forward+, sky / reflections / lights
            sb.AppendLine("Длительность: " + duration.ToString("F0", CI) + " c, кадров " + _frames.Count);
            sb.AppendLine("FPS: средний " + avgFps.ToString("F1", CI) + " | 1% low " + low1.ToString("F1", CI) + " | 0.1% low " + low01.ToString("F1", CI));
            sb.AppendLine("Кадр, мс: p50 " + Pct(sorted, 0.5f).ToString("F2", CI) + " | p95 " + Pct(sorted, 0.95f).ToString("F2", CI) + " | p99 " + Pct(sorted, 0.99f).ToString("F2", CI) + " | max " + (sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f).ToString("F2", CI));
            sb.AppendLine(_gpu.Count > 0 ? "GPU, мс: средн. " + gAvg.ToString("F2", CI) + " | p95 " + Pct(gs, 0.95f).ToString("F2", CI) : "GPU, мс: нет данных");
            if (_samples > 0)
                sb.AppendLine("Batches: средн. " + (_sumBatches / _samples).ToString("F0", CI) + " / max " + _maxBatches + " | SetPass: " + (_sumSetpass / _samples).ToString("F0", CI) + " / " + _maxSetpass +
                              " | Треугольники: " + (_sumTris / _samples / 1e6).ToString("F2", CI) + "M / " + (_maxTris / 1e6).ToString("F2", CI) + "M" + (_maxBatches == 0 ? " (счётчики рендера доступны только в Editor/Development build)" : ""));
            sb.AppendLine("Память: system " + Mb(_sysMem) + " MB, gfx " + Mb(_gfxMem) + " MB");
            sb.AppendLine("Итог: " + verdict + "; " + bound);
            LastSummary = sb.ToString();

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "benchmarks");
                Directory.CreateDirectory(dir);
                string stem = Path.Combine(dir, "bench_" + (_map != null ? _map.id : "map") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CI));
                File.WriteAllText(stem + ".csv", csv.ToString());
                File.WriteAllText(stem + ".txt", LastSummary);
                LastFile = stem + ".txt";
            }
            catch (Exception e) { Debug.LogWarning("[Severny] benchmark write failed: " + e.Message); }
            Debug.Log("[Severny] BENCHMARK\n" + LastSummary + "\nfile: " + LastFile);

            _batches.Dispose(); _setpass.Dispose(); _tris.Dispose(); _verts.Dispose(); _sysMem.Dispose(); _gfxMem.Dispose();
            GameSettings.Apply();
            Hud.Progress(null, 0f);
            Running = false;
            if (GameBootstrap.I != null) GameBootstrap.I.ReturnToMenu("Бенчмарк завершён", LastSummary + "\nФайл: " + LastFile);
        }

        void OnDestroy()
        {
            if (!Running) return;
            Running = false;
            _batches.Dispose(); _setpass.Dispose(); _tris.Dispose(); _verts.Dispose(); _sysMem.Dispose(); _gfxMem.Dispose();
            GameSettings.Apply();
        }
    }
}
